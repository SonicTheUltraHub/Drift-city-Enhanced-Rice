using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rice.Game;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Gotcha (capsulas) e caixas de presente de carro. Layouts da documentacao do emulador openDC (cliente 2016):
    //   3500 Gotcha (u32 moeda 0 mito / 1 CC, u32 preco, u32 maquina 0 carro / 1 pecas, u32 CC, u32 0, u32 temporada)
    //        -> o premio como item (402), o dinheiro (138 MoneyMessage) e a janela do premio:
    //           137 NotifyEvent (u32 23, u32 TableIdx do item, u32 quantidade, u32 0)
    //   Capsulas de carro: RewardGroup rw_mito_gacha_2015 / rw_cash_gacha_2015 (caixas de carro NT/BT/ST V1 ou pontos).
    //   417 ItemUse numa caixa de carro (giftbox_car): a caixa vira o carro -> 418 (InvenIdx, qtd, 0x30, 0, 0, 0),
    //       98 RewardCar (CarInfo, u32 1) e a chave (402).
    public static class Gotcha
    {
        static Dictionary<string, List<string[]>> groups;     // grupo -> linhas (tipo, item, periodo, quantidade, chance)
        static readonly Random Rng = new Random();

        static Dictionary<string, List<string[]>> Groups()
        {
            if (groups != null) return groups;
            groups = new Dictionary<string, List<string[]>>();
            try
            {
                string json = null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "RewardGroups.json");
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("RewardGroups.json"))
                    using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                var o = JObject.Parse(json)["groups"] as JObject;
                foreach (var kv in o)
                    groups[kv.Key] = kv.Value.Select(a => a.Select(x => (string) x).ToArray()).ToList();
            }
            catch (Exception ex) { Log.WriteError("RewardGroups: " + ex.Message); }
            return groups;
        }

        // sorteia um grupo: (item, quantidade)
        static KeyValuePair<string, int>? Roll(string group, int depth = 0)
        {
            List<string[]> rows;
            if (depth > 4 || !Groups().TryGetValue(group, out rows) || rows.Count == 0) return null;
            double total = rows.Sum(r => Chance(r));
            double x = Rng.NextDouble() * Math.Max(total, 1);
            string[] pick = rows[rows.Count - 1];
            foreach (var r in rows) { x -= Chance(r); if (x <= 0) { pick = r; break; } }
            if (pick[0] == "RewardGroup") return Roll(pick[1], depth + 1);
            if (pick[0] != "NormalItem") return null;
            int n; if (!int.TryParse(pick[3], out n) || n < 1) n = 1;
            return new KeyValuePair<string, int>(pick[1], n);
        }

        static double Chance(string[] r) { double c; return r.Length > 4 && double.TryParse(r[4].Trim(), out c) ? c : 1; }

        [RicePacket(3500, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Play(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 24) return;
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            uint currency = BitConverter.ToUInt32(buf, 0), asked = BitConverter.ToUInt32(buf, 4), machine = BitConverter.ToUInt32(buf, 8);
            uint season = BitConverter.ToUInt32(buf, 20);
            bool cash = currency == 1;
            var cfg = RiceServer.Config;
            string group = machine == 1 ? string.Format("rw_gacha_parts_{0}_{1:00}", cash ? "cash" : "mito", season)
                                        : (cash ? "rw_cash_gacha_2015" : "rw_mito_gacha_2015");
            if (!Groups().ContainsKey(group)) group = machine == 1 ? (cash ? "rw_gacha_item_cash" : "rw_gacha_item_mito") : group;
            if (!Groups().ContainsKey(group)) { Chat.SendSystemMessage(c, "Gotcha: essa maquina ainda nao tem premios."); return; }

            long cost = asked > 0 ? (long) asked : (cash ? 300L : 50000L);
            if (cash)
            {
                var user = c.Player.User;
                if (user.Credits < cost) { Chat.SendSystemMessage(c, "Gotcha: CC insuficiente (" + cost + ")."); return; }
                user.Credits -= (uint) cost;
                SaveCredits(user);
            }
            else if (ch.Mito < cost || !ch.SpendMito((ulong) cost))
            {
                Chat.SendSystemMessage(c, "Gotcha: mito insuficiente (" + cost + ")."); return;
            }

            var won = Roll(group);
            if (won == null) { Chat.SendSystemMessage(c, "Gotcha: nada saiu (grupo " + group + ")."); return; }
            Item it;
            if (!ch.GrantItem(won.Value.Key, (uint) won.Value.Value, out it) || it == null)
            {
                Log.WriteError("Gotcha: nao consegui dar " + won.Value.Key);
                return;
            }
            if (!cash)
            {
                var m = new RicePacket(138);
                m.Writer.Write(1); m.Writer.Write(1); m.Writer.Write(-cost); m.Writer.Write(ch.Mito); m.Writer.Write((ushort) 0);
                c.Send(m);
            }
            else
            {
                var h = new RicePacket(1401);
                h.Writer.Write((uint) c.Player.User.Credits); h.Writer.Write(0L);
                c.Send(h);
            }
            ch.FlushModInfo(c);
            var ev = new RicePacket(137);
            ev.Writer.Write(23);
            ev.Writer.Write((int) it.GetInfo().TableIdx);
            ev.Writer.Write(won.Value.Value);
            ev.Writer.Write(0);
            c.Send(ev);
            Log.WriteLine("Gotcha ({0}, {1} {2}): {3} x{4}.", group, cost, cash ? "CC" : "mito", won.Value.Key, won.Value.Value);
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

        // Caixa de carro -> (tipo do carro, grau). A caixa da a chave pc_XXXXs; o grau vem do nome (_v1 .. _v9).
        public static bool BoxCar(GenericItemEntry box, out int carType, out int grade)
        {
            carType = 0; grade = 1;
            if (box == null || box.Category != "giftbox_car") return false;
            List<string[]> rows;
            if (!Groups().TryGetValue(box.Function ?? "", out rows)) return false;
            var key = rows.Select(r => r[1]).FirstOrDefault(k => k != null && k.StartsWith("pc_"));
            if (key == null) return false;
            var v = VehicleTable.Vehicles.FirstOrDefault(kv => kv.Value != null && kv.Value.GetKeyId() == key);
            if (v.Value == null) return false;
            carType = v.Key;
            int at = box.ID.LastIndexOf("_v", StringComparison.OrdinalIgnoreCase);
            int g;
            if (at >= 0 && int.TryParse(box.ID.Substring(at + 2), out g) && g >= 1 && g <= 9) grade = g;
            return true;
        }

        // abre a caixa de carro do inventario: o carro entra na garagem
        public static bool OpenCarBox(RiceClient c, Rice.Game.Character ch, Item box, int invIdx)
        {
            int carType, grade;
            if (!CarBox.Lookup(ch, box, out carType, out grade)) return false;
            CarBox.Forget(ch, (uint) invIdx);
            Vehicle car; Item key;
            if (!ch.GrantVehicle(carType, out car, out key, 0, grade))
            {
                Chat.SendSystemMessage(c, "Nao consegui tirar o carro da caixa.");
                return true;
            }
            Item used;
            ch.DropItem(invIdx, out used, 1, true);
            var ack = new RicePacket(418);
            ack.Writer.Write((uint) invIdx); ack.Writer.Write(1u); ack.Writer.Write(0x30u);
            ack.Writer.Write(0u); ack.Writer.Write(0u); ack.Writer.Write(0u);
            c.Send(ack);
            var rc = new RicePacket(98);
            rc.Writer.Write(car.GetInfo());
            rc.Writer.Write(1u);
            c.Send(rc);
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, "Voce ganhou o carro " + CarBox.CarName(carType) + " V" + grade + "!");
            Log.WriteLine("Caixa de carro: {0} -> carro {1} V{2}.", box.itemEntry.ID, carType, grade);
            return true;
        }
    }

    // Caixa de carro feita pelo servidor: o cliente nao deixa por a chave na troca, entao o comando !!caixa guarda o
    // carro da garagem numa caixa de carro (giftbox_car) e lembra qual carro e grau estao dentro (carboxes.json).
    // A caixa entra na troca com o bot ou abre de novo no inventario (volta o mesmo carro e grau).
    public static class CarBox
    {
        const string FileName = "carboxes.json";
        static readonly object Sync = new object();
        static Dictionary<string, int[]> store;

        static Dictionary<string, int[]> Store()
        {
            if (store != null) return store;
            try { store = CharFiles.Exists(FileName) ? JsonConvert.DeserializeObject<Dictionary<string, int[]>>(CharFiles.ReadAllText(FileName)) : null; }
            catch (Exception) { store = null; }
            return store ?? (store = new Dictionary<string, int[]>());
        }

        static void Save()
        {
            try { CharFiles.WriteAllText(FileName, JsonConvert.SerializeObject(store, Formatting.Indented)); }
            catch (Exception ex) { Log.WriteError(FileName + ": " + ex.Message); }
        }

        static string Key(Rice.Game.Character ch, uint invIdx) { return ch.CID + ":" + invIdx; }

        public static bool Lookup(Rice.Game.Character ch, Item box, out int carType, out int grade)
        {
            int[] r;
            lock (Sync)
                if (Store().TryGetValue(Key(ch, box.invIdx), out r) && r != null && r.Length >= 2)
                { carType = r[0]; grade = r[1]; return true; }
            return Gotcha.BoxCar(box.itemEntry, out carType, out grade);
        }

        public static void Forget(Rice.Game.Character ch, uint invIdx)
        {
            lock (Sync) if (Store().Remove(Key(ch, invIdx))) Save();
        }

        public static string CarName(int carType)
        {
            VehicleListEntry v;
            if (!VehicleTable.Vehicles.TryGetValue(carType, out v) || v == null) return "carro " + carType;
            var key = ItemTable.Items.FirstOrDefault(i => i.ID == v.GetKeyId());
            return key != null ? key.Name : "carro " + carType;
        }

        // a caixa do proprio carro (o grau mais perto); sem nenhuma, uma caixa de carro qualquer
        static string BoxFor(int carType, int grade)
        {
            VehicleListEntry v;
            string key = VehicleTable.Vehicles.TryGetValue(carType, out v) && v != null ? v.GetKeyId() : null;
            var boxes = ItemTable.Items.Where(i => i.Category == "giftbox_car").ToList();
            var own = new List<GenericItemEntry>();
            if (key != null)
                foreach (var bx in boxes)
                {
                    int ct, g;
                    if (Gotcha.BoxCar(bx, out ct, out g) && ct == carType) own.Add(bx);
                }
            var pick = own.OrderBy(bx => Math.Abs(GradeOf(bx.ID) - grade)).FirstOrDefault()
                       ?? boxes.FirstOrDefault(bx => GradeOf(bx.ID) == grade) ?? boxes.FirstOrDefault();
            return pick != null ? pick.ID : null;
        }

        static int GradeOf(string id)
        {
            int at = (id ?? "").LastIndexOf("_v", StringComparison.OrdinalIgnoreCase), g;
            return at >= 0 && int.TryParse(id.Substring(at + 2), out g) ? g : 1;
        }

        // !!caixa : lista os carros ; !!caixa N : guarda o carro N numa caixa
        public static bool HandleCommand(RiceClient c, string lower, string body)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(lower, @"^caixa\s*\d*$")) return false;
            var ch = c.Player.ActiveCharacter;
            var cars = ch.Garage.Where(v => v.CarID != ch.CurrentCarID && !v.IsTraining).OrderBy(v => v.CarID).ToList();
            string arg = lower.Substring(5).Trim();
            int n;
            if (!int.TryParse(arg, out n))
            {
                if (cars.Count == 0) { Chat.SendSystemMessage(c, "Nenhum carro para guardar (o que voce esta usando nao entra)."); return true; }
                Chat.SendSystemMessage(c, "Guardar carro numa caixa (para trocar com bot): !!caixa <numero>");
                for (int i = 0; i < cars.Count; i++)
                    Chat.SendSystemMessage(c, string.Format("  {0} - {1} V{2}", i + 1, CarName((int) cars[i].CarType), Math.Max(1, cars[i].Grade)));
                return true;
            }
            if (n < 1 || n > cars.Count) { Chat.SendSystemMessage(c, "Numero invalido. Digite !!caixa para ver a lista."); return true; }
            Pack(c, ch, cars[n - 1]);
            return true;
        }

        // guarda o carro numa caixa (comando ou duplo clique na chave)
        public static void Pack(RiceClient c, Rice.Game.Character ch, Vehicle car)
        {
            if (car.CarID == ch.CurrentCarID) { Chat.SendSystemMessage(c, "Esse e o carro que voce esta usando: troque de carro antes de guardar na caixa."); return; }
            if (car.IsTraining) { Chat.SendSystemMessage(c, "O carro de treino nao vai para caixa."); return; }
            int type = (int) car.CarType, grade = (int) Math.Max(1, car.Grade);
            string boxId = BoxFor(type, grade);
            if (boxId == null) { Chat.SendSystemMessage(c, "Nao achei caixa de carro na tabela."); return; }
            int carId = car.CarID;
            if (!ch.RemoveVehicle(carId)) { Chat.SendSystemMessage(c, "Nao consegui tirar o carro da garagem."); return; }
            Item box;
            if (!ch.GrantItem(boxId, 1, out box) || box == null)
            {
                Vehicle back; Item key;
                ch.GrantVehicle(type, out back, out key, (int) car.Color, grade);
                Chat.SendSystemMessage(c, "Nao consegui criar a caixa; o carro voltou para a garagem.");
                ch.FlushModInfo(c);
                return;
            }
            lock (Sync) { Store()[Key(ch, box.invIdx)] = new[] { type, grade }; Save(); }
            var ack = new RicePacket(88);          // tira o carro da garagem do cliente (como a venda, por 0)
            ack.Writer.Write((uint) carId);
            ack.Writer.Write(0);
            c.Send(ack);
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, "O " + CarName(type) + " V" + grade + " foi guardado numa caixa no inventario. Coloque a caixa na troca,"
                                      + " ou use a caixa para tirar o carro de novo.");
            Log.WriteLine("Caixa de carro: {0} guardou o carro {1} V{2} em {3} (inventario {4}).", ch.Name, type, grade, boxId, box.invIdx);
            return;
        }
    }
}
