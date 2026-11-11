using Rice.Server.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rice.Game;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Drift shop (F4): pintura, neon, vidro, adesivos, aero, aerofolio, rodas, boosters...
    // Layouts conforme a documentacao do emulador openDC (cliente 2016):
    //   1200 VisualItemList -> 1201 (u32 0x40000, u32 n, n x XiStrMyVSItem 120 bytes)
    //   1203 BuyVisualItem (u32 TableIdx, u32 carro, texto 40 bytes, u32 periodo, u8 milhagem, i64 CC)
    //        -> 1202 VSItemModList (u32 n, n x (item 120, i32 estado: 1 novo, 3 mudou, 2 removido))
    //        -> 1204 BuyVisualItemAck (0, TableIdx, carro, InvenIdx, dias, mito pago, CC pago, 0, milhagem)
    //   1205 Equip (InvenIdx, slot, carro) -> 1206 ; 1207 UnEquip (InvenIdx, carro) -> 1208 ; 1211 Drop -> 1212
    // Neste servidor TODAS as pecas sao permanentes (dias = 0). Preco: o "para sempre" da tabela, se houver; senao
    // o maior periodo x VShopPermanentMultiplier. Itens de CC: paga em CC; sem CC suficiente, em mito (VShopCcToMito).
    // Guardado em vsitems.json. Bonus de atributos (Bonus Speed/Accel/Boost/Crash) somam no carro em que estao.
    public static class VisualShop
    {
        public class Shop
        {
            public string item, name, market, value;
            public int cat, useMito, mito, useCC, instant;
            public int m7D, m30D, m90D, m365D, m0D, c7D, c30D, c90D, c365D, c0D;
            public int[] bonus;
            public int index;                 // numero do item dentro da categoria (tabela do cliente); 0 = escolhido pelo texto
            public List<int> cars;
        }

        public class VsItem
        {
            public int Inven, Uid, Car, State;
            public string Text = "";
            public int Ctime;
        }

        const string FileName = "vsitems.json";
        const int New = 1, Changed = 3, Removed = 2, OnCar = 1;
        // 20 = Sticker Glue (cola do sticker): vale no carro em que foi comprada (sem carro o jogo dizia "expirada")
        static readonly HashSet<int> Equippable = new HashSet<int>(Enumerable.Range(1, 13).Concat(new[] { 32, 38, 47, 48, 52, 58 }));
        static readonly Dictionary<int, string> FixedSlots = new Dictionary<int, string> { { 1, "paint" }, { 32, "paint" }, { 2, "neon" }, { 3, "tint" } };
        static readonly object Sync = new object();
        static Dictionary<int, Shop> table;
        static Dictionary<string, List<VsItem>> store;

        public static Dictionary<int, Shop> Table()
        {
            if (table != null) return table;
            try
            {
                string json = null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "VisualShop.json");
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("VisualShop.json"))
                    using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                table = JsonConvert.DeserializeObject<Dictionary<int, Shop>>(json) ?? new Dictionary<int, Shop>();
                Log.WriteLine("Drift shop: {0} itens.", table.Count);
            }
            catch (Exception ex) { Log.WriteError("Drift shop: " + ex.Message); table = new Dictionary<int, Shop>(); }
            return table;
        }

        // ---- drinks da Drift Shop (Drift Item) -------------------------------------------------------------------
        // EXP drink (10406): +30% de exp jogando; MITO drink (10412): +50% de mito jogando (os dois nao valem na
        // recompensa de quest, como diz o jogo); Item Drink (10462): dobra os itens que caem (HUV da cidade e da Neo City).
        public const int ExpDrink = 10406, MitoDrink = 10412, ItemDrink = 10462;
        public static bool Owns(Rice.Game.Character ch, int uid)
        {
            if (ch == null) return false;
            try { lock (Sync) return ItemsOf(ch).Any(x => x.Uid == uid); } catch (Exception) { return false; }
        }
        public static double ExpMult(Rice.Game.Character ch) { return Owns(ch, ExpDrink) ? 1 + RiceServer.Config.DrinkExpBonus : 1; }
        public static double MitoMult(Rice.Game.Character ch) { return Owns(ch, MitoDrink) ? 1 + RiceServer.Config.DrinkMitoBonus : 1; }
        public static int DropCount(Rice.Game.Character ch) { return Owns(ch, ItemDrink) ? Math.Max(1, RiceServer.Config.DrinkItemDrops) : 1; }

        // ---- opcoes passivas das fileiras de docks de nivel -----------------------------------------------------------
        // O Turbo Start / Drift Booster da fileira de docks aparece no dock, mas o jogo so liga a opcao pelo item
        // "PassiveAssist" (categoria 41) da Drift Shop no carro. Estes itens sao nossos (texto DockTag): entram quando a
        // fileira fica completa e saem quando ela desfaz.
        public const string DockTag = "dock_row";
        // vazio: o PassiveAssist da Drift Shop nao ligou o Turbo Start / Drift Booster (Atualizacao 202); os que foram postos saem
        static readonly Dictionary<string, int> PassiveUids = new Dictionary<string, int>();
        public static int PassiveUidOf(string assist)
        {
            int u;
            if (string.IsNullOrEmpty(assist) || !PassiveUids.TryGetValue(assist, out u) || !Table().ContainsKey(u)) return 0;
            return u;
        }

        public static void SyncDockPassives(Rice.Game.Character ch, Dictionary<int, HashSet<int>> want, RiceClient client)
        {
            if (ch == null) return;
            var mods = new List<KeyValuePair<VsItem, int>>();
            var cars = new HashSet<int>();
            lock (Sync)
            {
                var items = ItemsOf(ch);
                var missing = new Dictionary<int, HashSet<int>>();
                foreach (var kv in want ?? new Dictionary<int, HashSet<int>>()) missing[kv.Key] = new HashSet<int>(kv.Value);
                foreach (var o in items.Where(o => o.Text == DockTag).ToList())
                {
                    HashSet<int> w;
                    if (missing.TryGetValue(o.Car, out w) && w.Remove(o.Uid)) continue;
                    items.Remove(o);
                    mods.Add(new KeyValuePair<VsItem, int>(o, Removed));
                    cars.Add(o.Car);
                }
                foreach (var kv in missing)
                    foreach (int uid in kv.Value)
                    {
                        int inven = (items.Count == 0 ? 61 : Math.Max(61, items.Max(x => x.Inven))) + 1;
                        var v = new VsItem { Inven = inven, Uid = uid, Car = kv.Key, State = OnCar, Text = DockTag, Ctime = Environment.TickCount };
                        items.Add(v);
                        mods.Add(new KeyValuePair<VsItem, int>(v, New));
                        cars.Add(kv.Key);
                        Shop s; Table().TryGetValue(uid, out s);
                        Log.WriteLine("Fileira de docks: {0} no carro {1} ({2}).", s != null ? s.market : uid.ToString(), kv.Key, ch.Name);
                    }
                if (mods.Count > 0) Save();
            }
            if (client == null || mods.Count == 0) return;
            SendMods(client, mods);
            foreach (int car in cars) AfterChange(client, ch, car);
        }

        static List<VsItem> ItemsOf(Rice.Game.Character ch) { return ItemsOfCid((long) ch.CID); }

        static List<VsItem> ItemsOfCid(long cid)
        {
            if (store == null)
            {
                try { store = CharFiles.Exists(FileName) ? JsonConvert.DeserializeObject<Dictionary<string, List<VsItem>>>(CharFiles.ReadAllText(FileName)) : null; }
                catch (Exception) { store = null; }
                if (store == null) store = new Dictionary<string, List<VsItem>>();
            }
            List<VsItem> l;
            if (!store.TryGetValue(cid.ToString(), out l)) { l = new List<VsItem>(); store[cid.ToString()] = l; }
            return l;
        }

        static void Save()
        {
            try { CharFiles.WriteAllText(FileName, JsonConvert.SerializeObject(store)); }
            catch (Exception ex) { Log.WriteError("vsitems.json: " + ex.Message); }
        }

        static readonly HashSet<int> TimedCats = new HashSet<int> { 20, 15, 17, 28 };

        static int CatOf(VsItem v) { Shop s; return Table().TryGetValue(v.Uid, out s) ? s.cat : -1; }
        static string SlotOf(VsItem v) { int c = CatOf(v); string f; return FixedSlots.TryGetValue(c, out f) ? f : c.ToString(); }

        static void WriteItem(PacketWriter w, VsItem v)
        {
            w.Write(v.Car);
            w.Write(v.State);
            w.Write((uint) v.Uid);
            w.Write((uint) v.Inven);
            var t = new byte[40];
            var e = Encoding.Unicode.GetBytes(v.Text ?? "");
            Array.Copy(e, t, Math.Min(e.Length, 38));
            w.Write(t);
            // Cola de sticker: o jogo (DriftCity.exe 0x6e58a0 / 0x7856f0) so aceita a cola com prazo > 0 (dias que faltam =
            // prazo - dias desde a compra); com 0 ("para sempre") dizia "adhesive spray has expired". Vai com 12000 dias.
            // O mesmo vale para os drinks (EXP 15, MITO 17, Item 28): sem prazo o jogo nao mostra o icone do efeito no canto
            // inferior direito e trata como vencido.
            bool glue = TimedCats.Contains(CatOf(v));
            w.Write(glue ? 12000 : 0);   // periodo 0 = para sempre
            w.Write(0);
            w.Write(glue ? (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() : v.Ctime);
            w.Write(new byte[48]);   // assists / pontos do Aero
            w.Write(new byte[4]);
        }

        static void SendMods(RiceClient c, List<KeyValuePair<VsItem, int>> mods)
        {
            var pk = new RicePacket(1202);
            pk.Writer.Write((uint) mods.Count);
            foreach (var m in mods) { WriteItem(pk.Writer, m.Key); pk.Writer.Write(m.Value); }
            c.Send(pk);
        }

        // Bonus de atributos dos itens no carro: speed, accel, boost, crash
        public static int[] BonusOn(Rice.Game.Character ch, int carId)
        {
            var b = new int[4];
            if (ch == null) return b;
            lock (Sync)
                foreach (var v in ItemsOf(ch).Where(x => x.Car == carId && x.State == OnCar))
                {
                    Shop s;
                    if (Table().TryGetValue(v.Uid, out s) && s.bonus != null)
                        for (int i = 0; i < 4 && i < s.bonus.Length; i++) b[i] += s.bonus[i];
                }
            return b;
        }

        // Pintura com cor escolhida (texto = cor decimal 0xRRGGBB): vira a cor do carro (0x83RRGGBB)
        static void ApplyPaint(Rice.Game.Character ch, VsItem v)
        {
            int cat = CatOf(v);
            if (cat != 1 && cat != 32) return;
            string t = (v.Text ?? "").Trim();
            long rgb;
            if (t.Length == 0 || !long.TryParse(t, out rgb)) return;
            if (t.Length == 9 && rgb > 0xffffff)
                rgb = ((long.Parse(t.Substring(0, 3)) & 0xff) << 16) | ((long.Parse(t.Substring(3, 3)) & 0xff) << 8) | (long.Parse(t.Substring(6)) & 0xff);
            uint color = 0x83000000u | (uint) (rgb & 0xffffff);
            var car = ch.Garage.FirstOrDefault(x => x.CarID == v.Car);
            if (car == null) return;
            car.Color = color;
            try
            {
                using (var rc = Rice.Database.GetContext())
                {
                    var db = rc.Vehicles.FirstOrDefault(x => x.CID == (long) ch.CID && x.CarID == car.CarID);
                    if (db != null) { db.Color = (int) color; rc.SaveChanges(); }
                }
            }
            catch (Exception ex) { Log.WriteError("Pintura: " + ex.Message); }
        }

        static ushort age = 1000;
        static void AfterChange(RiceClient c, Rice.Game.Character ch, int carId)
        {
            var car = ch.Garage.FirstOrDefault(x => x.CarID == carId);
            var stat = new RicePacket(760);
            stat.Writer.Write(ch.GetStatUpdate());
            c.Send(stat);
            if (car == null) return;
            var p = new RicePacket(1061);
            p.Writer.Write(ch.Serial);
            p.Writer.Write(age++);
            p.Writer.Write((uint) car.CarID);
            p.Writer.Write((byte) 0);
            p.Writer.Write(car.GetInfo());
            c.Send(p);
        }

        // poe o item no carro; o do mesmo lugar sai (pintura/neon/vidro: o antigo some)
        static void PutOn(List<VsItem> items, VsItem v, int carId, List<KeyValuePair<VsItem, int>> mods)
        {
            string slot = SlotOf(v);
            foreach (var o in items.Where(o => o != v && o.Car == carId && o.State == OnCar && SlotOf(o) == slot).ToList())
            {
                if (FixedSlots.ContainsKey(CatOf(o))) { items.Remove(o); mods.Add(new KeyValuePair<VsItem, int>(o, Removed)); }
                else { o.State = 0; mods.Add(new KeyValuePair<VsItem, int>(o, Changed)); }
            }
            v.Car = carId;
            v.State = OnCar;
        }

        static bool Fits(Shop s, Vehicle car)
        {
            if (s == null || car == null || !Equippable.Contains(s.cat)) return false;
            return s.cars == null || s.cars.Count == 0 || s.cars.Contains((int) car.CarType);
        }

        // preco permanente: (mito, CC)
        static void Price(Shop s, out long mito, out long cc)
        {
            var cfg = RiceServer.Config;
            mito = 0; cc = 0;
            long ccForever = s.c0D > 0 ? s.c0D : (long) Math.Round(new[] { s.c365D, s.c90D, s.c30D, s.c7D }.Max() * cfg.VShopPermanentMultiplier);
            if (s.useCC != 0 && s.useMito == 0) { cc = ccForever; return; }
            if (s.m0D > 0) mito = s.m0D;
            else if (s.mito > 0) mito = s.mito;
            else mito = (long) Math.Round(new[] { s.m365D, s.m90D, s.m30D, s.m7D }.Max() * cfg.VShopPermanentMultiplier);
            if (mito <= 0 && s.useCC != 0) cc = ccForever;
        }

        [RicePacket(1400, RiceServer.ServerType.Game)]
        public static void GetMyHancoin(RicePacket packet)
        {
            GetMyHancoinTo(packet.Sender);
        }

        static void GetMyHancoinTo(RiceClient c)
        {
            var ack = new RicePacket(1401);
            ack.Writer.Write((uint) c.Player.User.Credits);
            ack.Writer.Write(0L); // milhagem
            c.Send(ack);
        }

        [RicePacket(1200, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void VisualItemList(RicePacket packet)
        {
            var ch = packet.Sender.Player.ActiveCharacter;
            List<VsItem> items;
            lock (Sync)
            {
                // cola de sticker: o jogo (DriftCity.exe 0x6e58a0 / 0x557600 / 0x634f80) so procura a cola na lista dos
                // itens FORA do carro (estado 0). Posta no carro ela "sumia" e o jogo dizia vencida / sem cola.
                var all = ItemsOf(ch);
                bool fixedGlue = false;
                foreach (var g in all.Where(x => CatOf(x) == 20 && (x.State != 0 || x.Car != 0)).ToList())
                { g.Car = 0; g.State = 0; fixedGlue = true; }
                if (fixedGlue) Save();
                items = all.ToList();
            }
            var ack = new RicePacket(1201);
            ack.Writer.Write(0x40000);
            ack.Writer.Write(items.Count);
            foreach (var v in items) WriteItem(ack.Writer, v);
            packet.Sender.Send(ack);
            foreach (var v in items) if (IsDrink(v)) SendUseAck(packet.Sender, v);   // drinks ja "bebidos": icone no canto
        }

        // Drinks (EXP 15, MITO 17, Item 28): o jogo so marca o drink como em uso com o Cmd_VisualItemUseAck (1210,
        // DriftCity.exe 0x52a760: u32 inven -> item em uso). O servidor manda sozinho (na lista e na compra) e responde
        // o pedido de usar (1209).
        static bool IsDrink(VsItem v) { int c = CatOf(v); return c == 15 || c == 17 || c == 28; }

        static void SendUseAck(RiceClient c, VsItem v)
        {
            var use = new RicePacket(1210);
            use.Writer.Write((uint) v.Inven);
            use.Writer.Write(0);
            c.Send(use);
        }

        [RicePacket(1209, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void VisualItemUse(RicePacket packet)
        {
            var buf = packet.Buffer;
            Log.WriteLine("Drift shop: usar item ({0} bytes): {1}", buf.Length, BitConverter.ToString(buf, 0, Math.Min(buf.Length, 32)));
            if (buf.Length < 4) return;
            int inven = BitConverter.ToInt32(buf, 0);
            VsItem v;
            lock (Sync) v = ItemsOf(packet.Sender.Player.ActiveCharacter).FirstOrDefault(x => x.Inven == inven);
            if (v != null) SendUseAck(packet.Sender, v);
        }

        [RicePacket(1203, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void BuyVisualItem(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 0x30) return;
            int uid = BitConverter.ToInt32(buf, 0);
            int carId = BitConverter.ToInt32(buf, 4);
            string text = Encoding.Unicode.GetString(buf, 8, 40).Split('\0')[0];
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            Shop s;
            if (!Table().TryGetValue(uid, out s)) { Chat.SendSystemMessage(c, "Drift shop: item " + uid + " nao existe."); return; }
            var car = ch.Garage.FirstOrDefault(x => x.CarID == carId) ?? ch.Vehicle;

            long mito, cc;
            Price(s, out mito, out cc);
            long ccPaid = 0, mitoPaid = 0;
            var user = c.Player.User;
            if (cc > 0)
            {
                if (user.Credits >= cc) { user.Credits -= (uint) cc; ccPaid = cc; SaveCredits(user); }
                else mito += cc * RiceServer.Config.VShopCcToMito;      // sem CC: paga em mito
            }
            if (mito > 0)
            {
                if (ch.Mito < mito || !ch.SpendMito((ulong) mito))
                {
                    if (ccPaid > 0) { user.Credits += (uint) ccPaid; SaveCredits(user); }
                    Chat.SendSystemMessage(c, string.Format("Mito insuficiente: {0} precisa de {1:N0} mito.", s.market, mito));
                    return;
                }
                mitoPaid = mito;
            }

            var mods = new List<KeyValuePair<VsItem, int>>();
            VsItem v;
            lock (Sync)
            {
                var items = ItemsOf(ch);
                int inven = (items.Count == 0 ? 61 : Math.Max(61, items.Max(x => x.Inven))) + 1;
                v = new VsItem { Inven = inven, Uid = uid, Car = 0, State = 0, Text = text, Ctime = Environment.TickCount };
                items.Add(v);
                var extra = new List<KeyValuePair<VsItem, int>>();
                if (car != null && Fits(s, car)) PutOn(items, v, car.CarID, extra);
                else if (car != null && Equippable.Contains(s.cat)) v.Car = car.CarID;
                mods.Add(new KeyValuePair<VsItem, int>(v, New));
                mods.AddRange(extra);
                if (v.State == OnCar) ApplyPaint(ch, v);
                Save();
            }
            SendMods(c, mods);
            var ack = new RicePacket(1204);
            ack.Writer.Write(0);
            ack.Writer.Write(uid);
            ack.Writer.Write(v.Car);
            ack.Writer.Write(v.Inven);
            ack.Writer.Write(0);                      // dias: 0 = para sempre
            ack.Writer.Write((int) mitoPaid);
            ack.Writer.Write((int) ccPaid);
            ack.Writer.Write(0);
            ack.Writer.Write(0);
            c.Send(ack);
            if (IsDrink(v)) SendUseAck(c, v);
            if (ccPaid > 0) GetMyHancoinTo(c);
            AfterChange(c, ch, v.Car);
            Log.WriteLine("Drift shop: {0} comprou {1} ({2}) para sempre por {3} mito e {4} CC{5}.", ch.Name, s.market, uid,
                          mitoPaid, ccPaid, v.State == OnCar ? ", no carro " + v.Car : "");
        }

        static void SaveCredits(User user)
        {
            try
            {
                using (var rc = Rice.Database.GetContext())
                {
                    var u = rc.Users.Find((long) user.UID);
                    if (u != null) { u.Credits = user.Credits; rc.SaveChanges(); }
                }
            }
            catch (Exception ex) { Log.WriteError("CC: " + ex.Message); }
        }

        [RicePacket(1205, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Equip(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            int inven = BitConverter.ToInt32(buf, 0), carId = BitConverter.ToInt32(buf, 8);
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            var mods = new List<KeyValuePair<VsItem, int>>();
            lock (Sync)
            {
                var items = ItemsOf(ch);
                var v = items.FirstOrDefault(x => x.Inven == inven);
                var car = ch.Garage.FirstOrDefault(x => x.CarID == carId);
                Shop s;
                if (v == null || car == null || !Table().TryGetValue(v.Uid, out s) || !Fits(s, car) || (v.Car != 0 && v.Car != carId))
                {
                    Chat.SendSystemMessage(c, "Esse item nao serve nesse carro.");
                    return;
                }
                PutOn(items, v, carId, mods);
                mods.Add(new KeyValuePair<VsItem, int>(v, Changed));
                ApplyPaint(ch, v);
                Save();
            }
            var ack = new RicePacket(1206);
            ack.Writer.Write(buf, 0, 12);
            c.Send(ack);
            SendMods(c, mods);
            AfterChange(c, ch, carId);
        }

        [RicePacket(1207, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void UnEquip(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            int inven = BitConverter.ToInt32(buf, 0);
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            VsItem v;
            lock (Sync)
            {
                v = ItemsOf(ch).FirstOrDefault(x => x.Inven == inven && x.State == OnCar);
                if (v == null || FixedSlots.ContainsKey(CatOf(v)))
                {
                    Chat.SendSystemMessage(c, "Pintura, neon e vidro nao saem: compre outro para trocar.");
                    return;
                }
                v.State = 0;
                Save();
            }
            var ack = new RicePacket(1208);
            ack.Writer.Write(buf, 0, 8);
            c.Send(ack);
            SendMods(c, new List<KeyValuePair<VsItem, int>> { new KeyValuePair<VsItem, int>(v, Changed) });
            AfterChange(c, ch, v.Car);
        }

        [RicePacket(1211, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Drop(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            int inven = BitConverter.ToInt32(buf, 4);
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            VsItem v;
            bool wasOn;
            lock (Sync)
            {
                var items = ItemsOf(ch);
                v = items.FirstOrDefault(x => x.Inven == inven);
                if (v == null) return;
                wasOn = v.State == OnCar;
                items.Remove(v);
                Save();
            }
            var ack = new RicePacket(1212);
            ack.Writer.Write(buf, 0, 8);
            c.Send(ack);
            SendMods(c, new List<KeyValuePair<VsItem, int>> { new KeyValuePair<VsItem, int>(v, Removed) });
            AfterChange(c, ch, wasOn ? v.Car : 0);
        }

        // ---- caixa de peca visual (para trocar com bot) -----------------------------------------------------------
        // As pecas do drift shop nao aparecem na janela de troca. !!visual lista as suas; !!visual N tira a peca N e
        // poe no inventario uma caixa (presente "vshopbox") que lembra qual peca tem dentro (visualboxes.json).
        // A caixa entra na troca (o bot equipa) ou, usada no inventario, devolve a peca.
        public class VBox { public int Uid; public string Text = ""; }
        const string VBoxFile = "visualboxes.json";
        static Dictionary<string, VBox> vboxes;

        static Dictionary<string, VBox> VBoxes()
        {
            if (vboxes != null) return vboxes;
            try { vboxes = CharFiles.Exists(VBoxFile) ? JsonConvert.DeserializeObject<Dictionary<string, VBox>>(CharFiles.ReadAllText(VBoxFile)) : null; }
            catch (Exception) { vboxes = null; }
            return vboxes ?? (vboxes = new Dictionary<string, VBox>());
        }

        static void SaveVBoxes()
        {
            try { CharFiles.WriteAllText(VBoxFile, JsonConvert.SerializeObject(vboxes)); }
            catch (Exception ex) { Log.WriteError(VBoxFile + ": " + ex.Message); }
        }

        static string VKey(Rice.Game.Character ch, uint invIdx) { return ch.CID + ":" + invIdx; }

        // Visual de um carro do jogador para um bot (!!visualbot), como o XiVisualItem dos outros carros (doc. openDC):
        // um i16 por lugar com o numero do item na categoria; cor do adesivo @6 (ARGB do texto), cor do neon @0xa (RGB
        // do texto), texto da placa @0x26. Itens com numero 0 na tabela (Tire S5, AeroWing S5...): o numero vem do texto.
        public static readonly Dictionary<int, int> LookOffsets = new Dictionary<int, int>
        {
            { 2, 0x00 }, { 9, 0x02 }, { 11, 0x04 }, { 4, 0x0e }, { 5, 0x10 }, { 6, 0x12 }, { 10, 0x14 }, { 8, 0x16 }, { 48, 0x16 },
            { 7, 0x18 }, { 47, 0x1a }, { 52, 0x1c }
        };

        static uint Rgb(string t)
        {
            t = (t ?? "").Trim();
            long v;
            if (t.Length == 9 && long.TryParse(t, out v) && v > 0xffffff)
                return (uint) (((long.Parse(t.Substring(0, 3)) & 0xff) << 16) | ((long.Parse(t.Substring(3, 3)) & 0xff) << 8) | (long.Parse(t.Substring(6)) & 0xff));
            return long.TryParse(t, out v) ? (uint) (v & 0xffffff) : 0u;
        }

        public static Bots.BotLook LookOf(Rice.Game.Character ch, Rice.Game.Vehicle car)
        {
            return LookOf((long) ch.CID, car.CarID, car.Color);
        }

        // visual do carro de qualquer corredor (pelo CID; usado nos seus corredores que viram bot)
        public static Bots.BotLook LookOf(long cid, int carId, uint carColor)
        {
            var look = new Bots.BotLook();
            List<VsItem> items;
            lock (Sync) items = ItemsOfCid(cid).Where(x => x.State == OnCar && x.Car == carId).ToList();
            foreach (var v in items)
            {
                Shop sh;
                if (!Table().TryGetValue(v.Uid, out sh)) continue;
                string text = (v.Text ?? "").Trim();
                if (sh.cat == 3) { uint t = Rgb(text); if (t != 0) look.Tint = Bots.Bgr(0x82000000u | t); continue; }
                int at;
                if (!LookOffsets.TryGetValue(sh.cat, out at)) continue;
                int n = sh.index;
                int tn;
                if (n <= 0 && sh.cat != 9 && text.Length <= 4 && int.TryParse(text, out tn)) n = tn;
                if (n <= 0 && sh.cat == 6 && text.Length >= 5)
                {
                    int uid; Shop model;
                    if (int.TryParse(text.Substring(text.Length - 5), out uid) && Table().TryGetValue(uid, out model)) n = model.index;
                }
                if (n <= 0) continue;
                look.Slots[at.ToString("x")] = n;
                long sv;
                if (sh.cat == 11 && long.TryParse(text, out sv)) look.DecalColor = (uint) (sv & 0xffffffff);
                if (sh.cat == 2) { uint c = Rgb(text); if (c != 0) look.NeonColor = c; }
                if (sh.cat == 9) look.Plate = text.Length > 8 ? text.Substring(0, 8) : text;
            }
            if ((carColor >> 24) == 0x83) look.Paint = Bots.Bgr(carColor);
            return look;
        }

        public static string NameOf(int uid) { Shop sh; return Table().TryGetValue(uid, out sh) ? sh.name : "peca " + uid; }
        // numero do item no fim do id (i_d_n_00003 -> 3), usado na aparencia do carro do bot
        public static int NumberOf(int uid)
        {
            Shop sh;
            if (!Table().TryGetValue(uid, out sh) || sh.item == null) return 0;
            if (sh.index > 0) return sh.index;
            var m = System.Text.RegularExpressions.Regex.Match(sh.item, @"(\d+)$");
            int n; return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : 0;
        }
        // numeros de aerofolio (categoria 7) feitos para esse modelo de carro
        public static List<int> SpoilersFor(int car)
        {
            return Table().Values.Where(s => s.cat == 7 && s.index > 0 && s.cars != null && s.cars.Contains(car)).Select(s => s.index).Distinct().ToList();
        }
        public static int CatOfUid(int uid) { Shop sh; return Table().TryGetValue(uid, out sh) ? sh.cat : -1; }
        public static string SlotOfUid(int uid) { int c = CatOfUid(uid); string f; return FixedSlots.TryGetValue(c, out f) ? f : c.ToString(); }

        public static VBox BoxOf(Rice.Game.Character ch, uint invIdx)
        {
            lock (Sync) { VBox v; return VBoxes().TryGetValue(VKey(ch, invIdx), out v) ? v : null; }
        }

        public static void ForgetBox(Rice.Game.Character ch, uint invIdx)
        {
            lock (Sync) if (VBoxes().Remove(VKey(ch, invIdx))) SaveVBoxes();
        }

        // devolve uma peca visual ao corredor (nova no inventario do drift shop)
        public static void GiveVisual(RiceClient c, Rice.Game.Character ch, int uid, string text)
        {
            VsItem v;
            lock (Sync)
            {
                var items = ItemsOf(ch);
                int inven = (items.Count == 0 ? 61 : Math.Max(61, items.Max(x => x.Inven))) + 1;
                v = new VsItem { Inven = inven, Uid = uid, Car = 0, State = 0, Text = text ?? "", Ctime = Environment.TickCount };
                items.Add(v);
                Save();
            }
            SendMods(c, new List<KeyValuePair<VsItem, int>> { new KeyValuePair<VsItem, int>(v, New) });
        }

        static string BoxItemFor(int cat)
        {
            if (cat == 8 || cat == 48) return "i_n_2010_sumfest_vshopbox_tire";
            if (cat == 1 || cat == 32) return "i_n_2010_sumfest_vshopbox_paint";
            if (cat == 7) return "i_n_2010_sumfest_vshopbox_wing";
            return "i_n_2010_sumfest_vshopbox_boost";
        }

        public static bool HandleCommand(RiceClient c, string lower, string body)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(lower, @"^visual\s*\d*$")) return false;
            var ch = c.Player.ActiveCharacter;
            List<VsItem> list;
            // so as do inventario do drift shop (as que estao no carro ficam no carro)
            lock (Sync) list = ItemsOf(ch).Where(v => Equippable.Contains(CatOf(v)) && v.State != OnCar).OrderBy(v => v.Inven).ToList();
            int n;
            if (!int.TryParse(lower.Substring(6).Trim(), out n))
            {
                if (list.Count == 0) { Chat.SendSystemMessage(c, "Nenhuma peca visual no inventario do drift shop (tire do carro antes)."); return true; }
                Chat.SendSystemMessage(c, "Guardar peca visual numa caixa (para trocar com bot): !!visual <numero>");
                for (int i = 0; i < list.Count; i++)
                    Chat.SendSystemMessage(c, string.Format("  {0} - {1}", i + 1, NameOf(list[i].Uid)));
                return true;
            }
            if (n < 1 || n > list.Count) { Chat.SendSystemMessage(c, "Numero invalido. Digite !!visual para ver a lista."); return true; }
            var it = list[n - 1];
            bool wasOn; int car = it.Car;
            lock (Sync)
            {
                var items = ItemsOf(ch);
                if (!items.Remove(it)) return true;
                wasOn = it.State == OnCar;
                Save();
            }
            Rice.Game.Item box;
            if (!ch.GrantItem(BoxItemFor(CatOf(it)), 1, out box) || box == null)
            {
                GiveVisual(c, ch, it.Uid, it.Text);
                Chat.SendSystemMessage(c, "Nao consegui criar a caixa; a peca voltou.");
                return true;
            }
            lock (Sync) { VBoxes()[VKey(ch, box.invIdx)] = new VBox { Uid = it.Uid, Text = it.Text }; SaveVBoxes(); }
            SendMods(c, new List<KeyValuePair<VsItem, int>> { new KeyValuePair<VsItem, int>(it, Removed) });
            AfterChange(c, ch, wasOn ? car : 0);
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, NameOf(it.Uid) + " foi guardada numa caixa no inventario. Coloque a caixa na troca, ou use a caixa para ter a peca de volta.");
            return true;
        }

        // caixa usada no inventario: a peca volta
        public static bool OpenBox(RiceClient c, Rice.Game.Character ch, Rice.Game.Item box, int invIdx)
        {
            var vb = BoxOf(ch, (uint) invIdx);
            if (vb == null) return false;
            Rice.Game.Item used;
            if (!ch.DropItem(invIdx, out used, 1, true)) return true;
            ForgetBox(ch, (uint) invIdx);
            var ack = new RicePacket(418);
            ack.Writer.Write((uint) invIdx); ack.Writer.Write(1u); ack.Writer.Write(0x23u);
            ack.Writer.Write(0u); ack.Writer.Write(0u); ack.Writer.Write(0u);
            c.Send(ack);
            ch.FlushModInfo(c);
            GiveVisual(c, ch, vb.Uid, vb.Text);
            Chat.SendSystemMessage(c, "A peca " + NameOf(vb.Uid) + " voltou para o seu inventario do drift shop.");
            return true;
        }

        [RicePacket(1300, RiceServer.ServerType.Game)]
        public static void BuyHistoryList(RicePacket packet)
        {
            // historico de compras: nao guardado
        }

        [RicePacket(1306, RiceServer.ServerType.Game)]
        public static void IsValidCharName(RicePacket packet)
        {
        }
    }
}
