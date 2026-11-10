using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rice.Game;
using Rice.Server.Core;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Troca com os bots. Protocolo lido no cliente 2016 (DriftCity.exe):
    //   C2S 500 TradeOpen (nome 0x2a) ; 504 ItemOn (u32 troca, u32 InvenIdx, u32 qtd) ; 506 ItemOff (u32 troca, u32 InvenIdx)
    //       508 Lock (u32 troca, u16 estado) ; 510 Close (u32 troca) ; 512 Complete (u32 troca) ; 514 Money (u32 troca, i64)
    //   S2C 501 TradeOpenAck (nome A 0x2a, nome B 0x2a, i64 cid A, i64 cid B, u32 troca, u16 resultado 1 / 9999 recusou)
    //       505 ItemOnAck (u32 troca, item 88 bytes = ItemInfo sem os 8 primeiros, nome de quem pos 0x2a)
    //       507 ItemOffAck (u32 troca, u32 InvenIdx, nome) ; 509 LockAck (u32 troca, u32 1 trancou / 2 destrancou, nome)
    //       511 CloseAck (u32 troca) ; 513 CompleteAck (u32 troca, u32 4 confirmou / 5 trocou, nome) ; 515 MoneyAck (u32, i64, nome)
    // O bot: recusa as vezes (menos party, amigos e crew). Peca colocada: ele "equipa" na hora e poe a peca antiga dele
    // no lado dele (volta para voce). Chave ou caixa de carro: ele passa a usar esse carro e te da o carro antigo.
    // Voce confirma, ele confirma junto.
    public static class Trade
    {
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static uint nextId = 1;

        class Offer { public uint Count; public uint InvIdx; public string Kind; public string Cat; public string ItemId; public int CarType; public int Grade; public uint BotIdx; public string OldItem; public int OldCar, OldGrade; public int Uid, OldUid; public string Text, Slot; }
        class Session
        {
            public uint Id; public Bots.BotProfile Bot;
            public readonly List<Offer> Offers = new List<Offer>();
            public long Money; public bool Locked, Done;
            public uint NextBotIdx = 9001;
        }
        static readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();

        // ---- presente do bot da sua crew: ele vem ate voce e manda a troca so com o que achou para te dar ----------
        class Gift { public long Bot; public ulong For; public List<string> Items; public DateTime Until; }
        static readonly List<Gift> gifts = new List<Gift>();

        // o que o bot achou: Mittron Enhancer, peca (ate o seu nivel), Seed... quanto maior o nivel dele, melhor
        public static List<string> RollGift(Bots.BotProfile b, Rice.Game.Character ch)
        {
            var l = new List<string>();
            int lv = Math.Max(1, b.Level);
            string tier = lv >= 60 ? "H" : lv >= 30 ? "S" : "N";
            int n = 1 + Math.Min(2, lv / 30) + (Rng.NextDouble() < 0.3 ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                double r = Rng.NextDouble();
                if (r < 0.4) l.Add(tier == "H" ? "i_n_00045" : tier == "S" ? "i_n_00044" : "i_n_00043");
                else if (r < 0.65) l.Add("i_n_Seed_" + tier);
                else
                {
                    var e = Auction.RewardItem(Math.Max(1, Math.Min(lv, ch.Level))) as ItemTableEntry;
                    if (e != null && BotSocial.PartCats.Contains(e.Category) && Auction.ItemLevel(e.ID) <= ch.Level) l.Add(e.ID);
                    else l.Add(tier == "H" ? "i_n_00045" : tier == "S" ? "i_n_00044" : "i_n_00043");
                }
            }
            return l.Where(id => ItemTable.Items.Any(x => x.ID == id)).ToList();
        }

        public static void SetGift(Bots.BotProfile b, Rice.Game.Character ch, List<string> items, double seconds)
        {
            lock (Sync)
            {
                gifts.RemoveAll(g => g.Bot == b.Cid || DateTime.UtcNow > g.Until);
                gifts.Add(new Gift { Bot = b.Cid, For = ch.CID, Items = items, Until = DateTime.UtcNow.AddSeconds(seconds) });
            }
        }

        // o bot manda o pedido de troca (a janela abre como se voce tivesse pedido); se o jogo nao abrir, quando voce
        // abrir a troca com ele os presentes ja estao la
        public static void BotInvite(RiceClient c, Rice.Game.Character ch, Bots.BotProfile b)
        {
            if (c == null || ch == null || b == null) return;
            Session prev;
            Close(ch, out prev);
            uint id;
            var s = new Session { Bot = b };
            lock (Sync) { id = nextId++; s.Id = id; sessions[ch.CID] = s; }
            var pk = new RicePacket(501);
            pk.Writer.Write(WStr(ch.Name, 0x2a));
            pk.Writer.Write(WStr(b.Name, 0x2a));
            pk.Writer.Write((long) ch.CID);
            pk.Writer.Write(b.Cid);
            pk.Writer.Write(id);
            pk.Writer.Write((ushort) 1);
            c.Send(pk);
            Bots.MeetPlayer(b, 1800);
            PlaceGifts(c, ch, s);
            Log.WriteLine("Troca: {0} mandou a troca para {1} (presente).", b.Name, ch.Name);
        }

        static void PlaceGifts(RiceClient c, Rice.Game.Character ch, Session s)
        {
            Gift g;
            lock (Sync) g = gifts.FirstOrDefault(x => x.Bot == s.Bot.Cid && x.For == ch.CID && DateTime.UtcNow <= x.Until);
            if (g == null || g.Items.Count == 0) return;
            var names = new List<string>();
            foreach (var itemId in g.Items)
            {
                uint bi;
                lock (Sync) { bi = s.NextBotIdx++; s.Offers.Add(new Offer { Kind = "gift", ItemId = itemId, BotIdx = bi, InvIdx = 0x7fff0000u + bi }); }
                var bp = new RicePacket(505);
                bp.Writer.Write(s.Id); bp.Writer.Write(Item88(Fake(itemId, bi))); bp.Writer.Write(WStr(s.Bot.Name, 0x2a));
                c.Send(bp);
                var e = ItemTable.Items.FirstOrDefault(x => x.ID == itemId);
                names.Add(e != null ? e.Name : itemId);
            }
            Bots.Say(c, "channel", s.Bot.Name, "Coloquei na troca o que achei pra voce: " + string.Join(", ", names) + ". E so confirmar!");
        }

        static byte[] WStr(string s, int bytes)
        {
            var b = new byte[bytes];
            var e = Encoding.Unicode.GetBytes(s ?? "");
            Array.Copy(e, b, Math.Min(e.Length, bytes - 2));
            return b;
        }

        static string ReadWStr(byte[] b, int at, int bytes)
        {
            if (b.Length < at + 2) return "";
            return Encoding.Unicode.GetString(b, at, Math.Min(bytes, b.Length - at)).Split('\0')[0].Trim();
        }

        static byte[] Item88(ItemInfo info)
        {
            using (var ms = new MemoryStream())
            using (var w = new PacketWriter(ms))
            {
                w.Write(info);
                w.Flush();
                var all = ms.ToArray();
                var r = new byte[88];
                Array.Copy(all, 8, r, 0, Math.Min(88, all.Length - 8));
                return r;
            }
        }

        static ItemInfo Fake(string itemId, uint invIdx)
        {
            int t = ItemTable.Items.FindIndex(i => i.ID == itemId);
            return new ItemInfo { StackNum = 1, ItemHealth = 100f, TableIdx = (uint) Math.Max(t, 0), InvenIdx = invIdx };
        }

        // saiu da area / desconectou no meio da troca: fecha e o bot volta para o lugar dele
        public static void Abort(Rice.Game.Character ch)
        {
            Session s;
            if (ch != null) Close(ch, out s);
        }

        static bool Close(Rice.Game.Character ch, out Session s)
        {
            lock (Sync) { if (sessions.TryGetValue(ch.CID, out s)) sessions.Remove(ch.CID); else return false; }
            if (s != null && s.Bot != null) Bots.Unlink(s.Bot.Cid);     // troca fechou: o bot volta para o lugar dele
            return true;
        }

        static void SendClose(RiceClient c, uint id)
        {
            var pk = new RicePacket(511);
            pk.Writer.Write(id);
            c.Send(pk);
        }

        // ---------------------------------------------------------------------------------------------------------
        [RicePacket(500, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Open(RicePacket packet)
        {
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            string name = ReadWStr(packet.Buffer, 0, 0x2a);
            var b = Bots.ByName(name);
            Session prev;
            Close(ch, out prev);             // troca antiga que ficou aberta: o bot dela volta para o lugar
            Log.WriteLine("Troca: {0} pediu troca com {1}.", ch.Name, name);
            if (b == null) { Chat.SendSystemMessage(c, name + " nao esta online para trocar."); return; }
            if (Bots.InTutorial(ch)) { Chat.SendSystemMessage(c, "Termine o tutorial e pegue o seu primeiro carro antes de trocar."); return; }
            if (b.IsAlt && b.InTutorial) { Chat.SendSystemMessage(c, b.Name + " ainda nao terminou o tutorial (esta com o carro de treino). Nao da para trocar com ele."); return; }
            if (b.IsAlt) { Chat.SendSystemMessage(c, "Para trocar com o seu outro corredor, entre com ele (correio/garagem)."); return; }

            bool close = BotSocial.PartyBots(ch).Contains(b) || BotSocial.IsFriend((long) ch.CID, b.Cid);
            var myCrew = Crew.Of((long) ch.CID); var botCrew = Crew.Of(b.Cid);
            if (myCrew != null && botCrew != null && myCrew.Tid == botCrew.Tid) close = true;
            double refuse = b.Personality == "calado" ? 0.55 : b.Personality == "competitivo" ? 0.45 : b.Personality == "comerciante" ? 0.15 : 0.3;
            bool ok = close || !RiceServer.Config.TradeBotsRefuse || Rng.NextDouble() >= refuse;

            uint id;
            lock (Sync)
            {
                id = nextId++;
                if (ok) sessions[ch.CID] = new Session { Id = id, Bot = b };
            }
            var pk = new RicePacket(501);
            pk.Writer.Write(WStr(ch.Name, 0x2a));
            pk.Writer.Write(WStr(b.Name, 0x2a));
            pk.Writer.Write((long) ch.CID);
            pk.Writer.Write(b.Cid);
            pk.Writer.Write(id);
            pk.Writer.Write((ushort) (ok ? 1 : 9999));
            c.Send(pk);
            if (!ok) { Bots.Say(c, "channel", b.Name, RefuseLine(b)); return; }
            Bots.MeetPlayer(b, 1800);        // o bot fica de frente para voce ate a troca acabar (feita ou cancelada)
            Session cur; lock (Sync) sessions.TryGetValue(ch.CID, out cur);
            if (cur != null) PlaceGifts(c, ch, cur);      // bot da sua crew que veio te dar coisas
            Bots.Say(c, "channel", b.Name, b.NeoBot ? "Bora trocar! Aceito arma, armadura, Amplifier e Fuse da Neo City (e pecas, caixa de carro ou de peca visual)."
                                                    : "Bora trocar! Coloca uma peca (velocidade, aceleracao, durabilidade, boost), equipamento da Neo City, uma caixa de carro ou de peca visual.");
            Chat.SendSystemMessage(c, "Trocar carro: clique duas vezes na chave (ou !!caixa) e ela vira caixa de carro. Peca visual: !!visual guarda numa caixa. Coloque a caixa na troca.");
        }

        static string RefuseLine(Bots.BotProfile b)
        {
            string[] l = { "Agora nao, to ocupado.", "Hoje nao, valeu.", "Nao troco com quem nao conheco. Me adiciona primeiro!", "Depois a gente troca." };
            return l[Rng.Next(l.Length)];
        }

        static Rice.Game.Item Inv(Rice.Game.Character ch, uint idx) { return ch.Inventory.FirstOrDefault(i => i.invIdx == idx); }

        [RicePacket(504, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ItemOn(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            uint id = BitConverter.ToUInt32(buf, 0), idx = BitConverter.ToUInt32(buf, 4);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Session s;
            lock (Sync) if (!sessions.TryGetValue(ch.CID, out s) || s.Id != id || s.Locked) return;
            var it = Inv(ch, idx);
            if (it == null || it.itemEntry == null) return;
            var b = s.Bot;
            var e = it.itemEntry;
            var o = new Offer { InvIdx = idx, ItemId = e.ID };
            string msg;
            string ecat = e.Category ?? "";
            if (ecat.StartsWith("op_") || ecat.StartsWith("dock_"))
            {
                // option ou dock: um de cada tipo no carro do bot; ele confere o nivel e compara com o que tem
                if (it.curCarID != 0) { Chat.SendSystemMessage(c, "Tire do carro antes de trocar."); return; }
                var ne = e as ItemTableEntry;
                int need = Auction.ItemLevel(e.ID);
                if (need > b.Level) { Bots.Say(c, "channel", b.Name, "Essa pede nivel " + need + ", eu sou " + b.Level + ". Ainda nao consigo usar."); return; }
                o.Kind = "part"; o.Cat = ecat; o.Slot = ecat;
                string old; b.Parts.TryGetValue(ecat, out old);
                o.OldItem = old;
                var oe = old != null ? ItemTable.Items.FirstOrDefault(x => x.ID == old) as ItemTableEntry : null;
                double np = (ne != null ? ne.BasePoints : 0) + Math.Max(0, it.upgradePoint), op = oe != null ? oe.BasePoints : 0;
                msg = string.Format("{0}: {1} -> {2} (+{3})", CatName(ecat), oe != null ? oe.Name + " (+" + (int) op + ")" : "vazio", e.Name, (int) np);
                Bots.Say(c, "channel", b.Name, oe == null ? "Nao tenho " + CatName(ecat).ToLower() + " no carro, ja coloco essa!"
                                              : np > op ? "Essa e melhor que a minha (+" + (int) op + "), boa!" : "Hmm, a minha e melhor (+" + (int) op + ")... mas se quiser trocar, troco.");
            }
            else if (ecat.StartsWith("neo_") && (ecat == "neo_weapon" || ecat == "neo_armor" || ecat == "neo_coil" || ecat == "neo_fuse"))
            {
                // equipamento da Neo City: arma (2 lugares), armadura, Amplifier e Fuse
                if (it.curCarID != 0) { Chat.SendSystemMessage(c, "Tire do carro antes de trocar."); return; }
                var ne = e as ItemTableEntry;
                int need; if (ne == null || !int.TryParse(ne.MinLevel, out need)) need = 0;
                if (need > b.Level + 9) { Bots.Say(c, "channel", b.Name, "Esse pede nivel " + need + ", eu sou " + b.Level + ". Ainda nao consigo usar."); return; }
                o.Kind = "part"; o.Cat = ecat;
                if (ecat == "neo_weapon")
                {
                    string s1, s2; b.Parts.TryGetValue("neo_weapon", out s1); b.Parts.TryGetValue("neo_weapon#2", out s2);
                    Offer first; lock (Sync) first = s.Offers.FirstOrDefault(x => x.Kind == "part" && x.Cat == ecat);
                    // a arma do mesmo tipo (Cannon, Railgun, Wave, Mine) troca a dele desse tipo; senao a vaga livre / a pior
                    string fam = e.ID.Length > 9 ? e.ID.Substring(0, 9) : e.ID;
                    if (first != null) o.Slot = first.Slot == "neo_weapon" ? "neo_weapon#2" : "neo_weapon";
                    else if (s1 != null && s1.StartsWith(fam)) o.Slot = "neo_weapon";
                    else if (s2 != null && s2.StartsWith(fam)) o.Slot = "neo_weapon#2";
                    else o.Slot = s1 == null ? "neo_weapon" : s2 == null ? "neo_weapon#2" : (NeoLevel(s1) <= NeoLevel(s2) ? "neo_weapon" : "neo_weapon#2");
                }
                else o.Slot = ecat;
                string old; b.Parts.TryGetValue(o.Slot, out old);
                o.OldItem = old;
                var oe = old != null ? ItemTable.Items.FirstOrDefault(x => x.ID == old) as ItemTableEntry : null;
                int nl = need, ol = old != null ? NeoLevel(old) : 0;
                msg = string.Format("{0}: {1} -> {2}", CatName(ecat), oe != null ? oe.Name : "vazio", e.Name);
                Bots.Say(c, "channel", b.Name, oe == null ? "Nao tenho " + CatName(ecat).ToLower() + ", ja equipo esse!"
                                              : nl > ol ? "Esse e melhor que o meu (" + oe.Name + "), boa!" : "Hmm, o meu e melhor (" + oe.Name + ")... mas se quiser trocar, troco.");
            }
            else if (ecat == "healthupnow")
            {
                // kit de recuperacao: o bot guarda e usa na Neo City quando alguem da party estiver mal
                o.Kind = "kit"; o.Cat = ecat; o.Count = Math.Max(1u, it.stackNum);
                int has = b.NeoKits != null ? b.NeoKits.Values.Sum() : 0;
                msg = string.Format("{0} x{1} (ele tem {2})", e.Name, o.Count, has);
                Bots.Say(c, "channel", b.Name, "Valeu! Uso esse kit quando a party estiver com pouca vida na Neo City.");
            }
            else if (BotSocial.PartCats.Contains(e.Category))
            {
                if (it.curCarID != 0) { Chat.SendSystemMessage(c, "Tire a peca do carro antes de trocar."); return; }
                o.Kind = "part"; o.Cat = e.Category;
                // duas pecas por tipo (slot 1 = categoria, slot 2 = categoria#2): vaga livre primeiro; as duas cheias ->
                // a 1a peca da troca troca a 1a dele e a 2a peca troca a 2a
                string k1 = e.Category, k2 = e.Category + "#2";
                string s1, s2; b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
                Offer first; lock (Sync) first = s.Offers.FirstOrDefault(x => x.Kind == "part" && x.Cat == e.Category);
                if (first == null) o.Slot = s1 == null ? k1 : s2 == null ? k2 : k1;
                else o.Slot = first.Slot == k1 ? k2 : k1;
                string old; b.Parts.TryGetValue(o.Slot, out old);
                o.OldItem = old;
                var ne = e as ItemTableEntry;
                var oe = old != null ? ItemTable.Items.FirstOrDefault(x => x.ID == old) as ItemTableEntry : null;
                msg = string.Format("{0} (slot {1}): {2} ({3}) -> {4} ({5})", CatName(e.Category), o.Slot == k1 ? 1 : 2, oe != null ? oe.Name : "vazio",
                                    oe != null ? "+" + (int) oe.BasePoints : "+0", e.Name, ne != null ? "+" + (int) ne.BasePoints : "?");
                // o bot opina: qual slot a peca melhora mais
                var e1 = s1 != null ? ItemTable.Items.FirstOrDefault(x => x.ID == s1) as ItemTableEntry : null;
                var e2 = s2 != null ? ItemTable.Items.FirstOrDefault(x => x.ID == s2) as ItemTableEntry : null;
                double np = ne != null ? ne.BasePoints : 0, p1 = e1 != null ? e1.BasePoints : 0, p2 = e2 != null ? e2.BasePoints : 0;
                string tip;
                if (s1 == null || s2 == null) tip = "Tenho vaga, ja equipo essa!";
                else if (np <= Math.Min(p1, p2)) tip = "Hmm, as minhas duas sao melhores (+" + (int) p1 + " e +" + (int) p2 + ")...";
                else if (p1 <= p2) tip = np > p1 ? "Essa e melhor que a minha 1a (+" + (int) p1 + "), boa!" : "Essa so melhora a minha 2a.";
                else tip = "Essa fica melhor no lugar da minha 2a (+" + (int) p2 + ").";
                Bots.Say(c, "channel", b.Name, tip);
            }
            else if (e.Category == "car")
            {
                var car = ch.Garage.FirstOrDefault(v => v.CarID == it.curCarID);
                if (car == null) return;
                if (car.CarID == ch.CurrentCarID || ch.Garage.Count <= 1) { Chat.SendSystemMessage(c, "Nao da para trocar o carro que voce esta usando."); return; }
                o.Kind = "car"; o.CarType = (int) car.CarType; o.Grade = (int) Math.Max(1, car.Grade);
                o.OldCar = b.Car; o.OldGrade = BotSocial.GradeOf(b);
                msg = CarLine(o.OldCar, o.OldGrade, o.CarType, o.Grade);
            }
            else if (VisualShop.BoxOf(ch, idx) != null)
            {
                var vb = VisualShop.BoxOf(ch, idx);
                o.Kind = "visual"; o.Uid = vb.Uid; o.Text = vb.Text; o.Slot = VisualShop.SlotOfUid(vb.Uid);
                int old; o.OldUid = b.Visuals.TryGetValue(o.Slot, out old) ? old : 0;
                msg = "Visual: " + (o.OldUid > 0 ? VisualShop.NameOf(o.OldUid) : "nada") + " -> " + VisualShop.NameOf(o.Uid);
            }
            else if (e.Category == "giftbox_car")
            {
                int ct, g;
                if (!CarBox.Lookup(ch, it, out ct, out g)) { Chat.SendSystemMessage(c, "Essa caixa nao tem carro."); return; }
                o.Kind = "box"; o.CarType = ct; o.Grade = g;
                o.OldCar = b.Car; o.OldGrade = BotSocial.GradeOf(b);
                msg = CarLine(o.OldCar, o.OldGrade, o.CarType, o.Grade);
            }
            else { Bots.Say(c, "channel", b.Name, "Isso eu nao quero. So pecas, option, dock, equipamento da Neo City, chave de carro ou caixa de carro."); return; }

            lock (Sync)
            {
                if (s.Offers.Any(x => x.InvIdx == idx)) return;
                bool isCar = o.Kind == "car" || o.Kind == "box";
                if (isCar && Bots.Unsupported(o.CarType)) { Bots.Say(c, "channel", b.Name, "Esse carro nao roda no meu jogo, nao quero."); return; }
                if (isCar && s.Offers.Any(x => x.Kind == "car" || x.Kind == "box")) { Chat.SendSystemMessage(c, "Um carro por troca."); return; }
                if (o.Kind == "visual" && s.Offers.Any(x => x.Kind == "visual" && x.Slot == o.Slot)) { Chat.SendSystemMessage(c, "Uma peca visual de cada tipo por troca."); return; }
                if (o.Kind == "part" && s.Offers.Count(x => x.Kind == "part" && x.Cat == o.Cat) >= (o.Cat.StartsWith("op_") || o.Cat.StartsWith("dock_") || (o.Cat.StartsWith("neo_") && o.Cat != "neo_weapon") ? 1 : 2)) { Chat.SendSystemMessage(c, "No maximo duas pecas (ou uma option/dock) de cada tipo por troca."); return; }
                o.BotIdx = s.NextBotIdx++;
                s.Offers.Add(o);
            }
            var pk = new RicePacket(505);
            pk.Writer.Write(id); pk.Writer.Write(Item88(it.GetInfo())); pk.Writer.Write(WStr(ch.Name, 0x2a));
            c.Send(pk);
            // o bot poe o que volta para voce
            string back = o.Kind == "kit" ? null : o.Kind == "part" ? o.OldItem : o.Kind == "visual" ? (o.OldUid > 0 ? e.ID : null) : KeyOf(o.OldCar);
            if (back != null)
            {
                var bp = new RicePacket(505);
                bp.Writer.Write(id); bp.Writer.Write(Item88(Fake(back, o.BotIdx))); bp.Writer.Write(WStr(b.Name, 0x2a));
                c.Send(bp);
            }
            Chat.SendSystemMessage(c, "[Troca com " + b.Name + "] " + msg);
        }

        static int NeoLevel(string id)
        {
            var x = ItemTable.Items.FirstOrDefault(i => i.ID == id) as ItemTableEntry;
            int m; return x != null && int.TryParse(x.MinLevel, out m) ? m : 0;
        }

        static string CatName(string cat)
        {
            switch (cat)
            {
                case "speed": return "Velocidade"; case "accel": return "Aceleracao"; case "crash": return "Durabilidade"; case "boost": return "Boost";
                case "op_S": return "Option de velocidade"; case "op_A": return "Option de aceleracao"; case "op_C": return "Option de durabilidade";
                case "op_B": return "Option de boost"; case "op_F": return "Option especial";
                case "dock_S": return "Dock de velocidade"; case "dock_A": return "Dock de aceleracao"; case "dock_C": return "Dock de durabilidade";
                case "dock_B": return "Dock de boost";
                case "neo_weapon": return "Arma da Neo City"; case "neo_armor": return "Armadura da Neo City";
                case "neo_coil": return "Amplifier"; case "neo_fuse": return "Fuse";
                default: return "Peca";
            }
        }

        static string KeyOf(int carType)
        {
            VehicleListEntry v;
            return VehicleTable.Vehicles.TryGetValue(carType, out v) && v != null ? v.GetKeyId() : null;
        }

        static string CarName(int carType)
        {
            VehicleListEntry v;
            if (!VehicleTable.Vehicles.TryGetValue(carType, out v) || v == null) return "carro " + carType;
            var key = ItemTable.Items.FirstOrDefault(i => i.ID == v.GetKeyId());
            return key != null ? key.Name : "carro " + carType;
        }

        static string Stats(int carType, int grade)
        {
            try
            {
                var u = VehicleTable.GetVehicleUpgrade(carType, grade);
                return string.Format("vel {0} / ace {1} / dur {2} / boost {3}", u.Speed, u.Acceleration, u.Durability, u.Boost);
            }
            catch (Exception) { return "?"; }
        }

        static string CarLine(int oldCar, int oldGrade, int newCar, int newGrade)
        {
            return string.Format("Carro: {0} V{1} ({2}) -> {3} V{4} ({5})", CarName(oldCar), oldGrade, Stats(oldCar, oldGrade),
                                 CarName(newCar), newGrade, Stats(newCar, newGrade));
        }

        [RicePacket(506, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ItemOff(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            uint id = BitConverter.ToUInt32(buf, 0), idx = BitConverter.ToUInt32(buf, 4);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Session s; Offer o;
            lock (Sync)
            {
                if (!sessions.TryGetValue(ch.CID, out s) || s.Id != id || s.Locked) return;
                o = s.Offers.FirstOrDefault(x => x.InvIdx == idx);
                if (o == null) return;
                s.Offers.Remove(o);
            }
            var pk = new RicePacket(507);
            pk.Writer.Write(id); pk.Writer.Write(idx); pk.Writer.Write(WStr(ch.Name, 0x2a));
            c.Send(pk);
            var bp = new RicePacket(507);
            bp.Writer.Write(id); bp.Writer.Write(o.BotIdx); bp.Writer.Write(WStr(s.Bot.Name, 0x2a));
            c.Send(bp);
        }

        [RicePacket(514, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Money(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            uint id = BitConverter.ToUInt32(buf, 0); long money = BitConverter.ToInt64(buf, 4);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Session s;
            lock (Sync) { if (!sessions.TryGetValue(ch.CID, out s) || s.Id != id || s.Locked) return; s.Money = Math.Max(0, Math.Min(money, ch.Mito)); }
            var pk = new RicePacket(515);
            pk.Writer.Write(id); pk.Writer.Write(s.Money); pk.Writer.Write(WStr(ch.Name, 0x2a));
            c.Send(pk);
        }

        [RicePacket(508, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Lock(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 6) return;
            uint id = BitConverter.ToUInt32(buf, 0); uint st = BitConverter.ToUInt16(buf, 4);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Session s;
            lock (Sync) { if (!sessions.TryGetValue(ch.CID, out s) || s.Id != id) return; s.Locked = st != 2; }
            var pk = new RicePacket(509);
            pk.Writer.Write(id); pk.Writer.Write(st); pk.Writer.Write(WStr(ch.Name, 0x2a));
            c.Send(pk);
            // o bot tranca junto
            var bp = new RicePacket(509);
            bp.Writer.Write(id); bp.Writer.Write(st); bp.Writer.Write(WStr(s.Bot.Name, 0x2a));
            c.Send(bp);
        }

        [RicePacket(510, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void CloseReq(RicePacket packet)
        {
            var buf = packet.Buffer;
            uint id = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0;
            Session s;
            Close(packet.Sender.Player.ActiveCharacter, out s);
            SendClose(packet.Sender, s != null ? s.Id : id);
        }

        [RicePacket(512, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Complete(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            uint id = BitConverter.ToUInt32(buf, 0);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Session s;
            lock (Sync) { if (!sessions.TryGetValue(ch.CID, out s) || s.Id != id || s.Done) return; s.Done = true; }
            foreach (var who in new[] { ch.Name, s.Bot.Name })
            {
                var pk = new RicePacket(513);
                pk.Writer.Write(id); pk.Writer.Write(4u); pk.Writer.Write(WStr(who, 0x2a));
                c.Send(pk);
            }
            Session gone;
            Close(ch, out gone);
            var givenParts = s.Offers.Where(o => o.Kind == "part").Select(o => o.ItemId).ToList();
            bool givenCar = s.Offers.Any(o => o.Kind == "car" || o.Kind == "box");
            long givenMoney = s.Money;
            var lines = Exchange(c, ch, s);
            var end = new RicePacket(513);
            end.Writer.Write(id); end.Writer.Write(5u); end.Writer.Write(WStr(ch.Name, 0x2a));
            c.Send(end);
            ch.FlushModInfo(c);
            foreach (var l in lines) Chat.SendSystemMessage(c, "[Troca com " + s.Bot.Name + "] " + l);
            if (lines.Count > 0) Bots.Say(c, "channel", s.Bot.Name, "Valeu pela troca! Ja equipei.");
            try { BotChatter.TradeDone(c, ch, s.Bot, givenParts, givenCar, lines.Count > 0 ? givenMoney : 0); }
            catch (Exception ex) { Log.WriteError("Troca (meta do bot): " + ex.Message); }
        }

        static List<string> Exchange(RiceClient c, Rice.Game.Character ch, Session s)
        {
            var lines = new List<string>();
            var b = s.Bot;
            foreach (var o in s.Offers.Where(x => x.Kind == "gift"))
            {
                Item got;
                if (ch.GrantItem(o.ItemId, 1, out got))
                {
                    var e = ItemTable.Items.FirstOrDefault(x => x.ID == o.ItemId);
                    lines.Add(b.Name + " te deu " + (e != null ? e.Name : o.ItemId) + ".");
                }
            }
            if (s.Offers.Any(x => x.Kind == "gift")) lock (Sync) gifts.RemoveAll(g => g.Bot == b.Cid);
            foreach (var o in s.Offers.Where(x => x.Kind != "gift"))
            {
                var it = Inv(ch, o.InvIdx);
                if (it == null) continue;
                if (o.Kind == "visual")
                {
                    Item dropped;
                    if (!ch.DropItem((int) o.InvIdx, out dropped, 1, true)) continue;
                    VisualShop.ForgetBox(ch, o.InvIdx);
                    b.Visuals[o.Slot] = o.Uid;
                    if (o.OldUid > 0) VisualShop.GiveVisual(c, ch, o.OldUid, "");
                    lines.Add("Visual: " + b.Name + " equipou " + VisualShop.NameOf(o.Uid) + (o.OldUid > 0 ? " e te deu " + VisualShop.NameOf(o.OldUid) + "." : "."));
                    continue;
                }
                if (o.Kind == "kit")
                {
                    Item dropped;
                    if (!ch.DropItem((int) o.InvIdx, out dropped, o.Count, true)) continue;
                    if (b.NeoKits == null) b.NeoKits = new Dictionary<string, int>();
                    int cur; b.NeoKits.TryGetValue(o.ItemId, out cur);
                    b.NeoKits[o.ItemId] = cur + (int) o.Count;
                    lines.Add(b.Name + " guardou " + o.Count + "x " + it.itemEntry.Name + " (agora tem " + b.NeoKits.Values.Sum() + ").");
                    continue;
                }
                if (o.Kind == "part")
                {
                    Item dropped;
                    if (!ch.DropItem((int) o.InvIdx, out dropped, 1, true)) continue;
                    b.Parts[o.Slot ?? o.Cat] = o.ItemId;
                    if (o.OldItem != null) { Item got; ch.GrantItem(o.OldItem, 1, out got); }
                    lines.Add(CatName(o.Cat) + ": " + b.Name + " equipou " + it.itemEntry.Name + (o.OldItem != null ? " e te deu a antiga." : "."));
                }
                else
                {
                    if (o.Kind == "car")
                    {
                        if (!ch.RemoveVehicle((int) it.curCarID)) continue;
                    }
                    else
                    {
                        Item dropped;
                        if (!ch.DropItem((int) o.InvIdx, out dropped, 1, true)) continue;
                        CarBox.Forget(ch, o.InvIdx);
                    }
                    Vehicle car; Item key;
                    if (o.OldCar > 0 && ch.GrantVehicle(o.OldCar, out car, out key, 0, o.OldGrade))
                    {
                        var rc = new RicePacket(98);
                        rc.Writer.Write(car.GetInfo());
                        rc.Writer.Write(1u);
                        c.Send(rc);
                    }
                    b.Car = o.CarType; b.CarGrade = o.Grade;
                    lines.Add(b.Name + " agora usa " + CarName(o.CarType) + " V" + o.Grade + "; voce ganhou o " + CarName(o.OldCar) + " V" + o.OldGrade + ".");
                }
            }
            if (s.Money > 0 && ch.SpendMito((ulong) s.Money))
            {
                var m = new RicePacket(138);
                m.Writer.Write(1); m.Writer.Write(1); m.Writer.Write(-s.Money); m.Writer.Write(ch.Mito); m.Writer.Write((ushort) 0);
                c.Send(m);
                lines.Add("Voce deu " + s.Money.ToString("N0") + " mito.");
            }
            Bots.Save();
            Log.WriteLine("Troca {0} <-> {1}: {2}", ch.Name, b.Name, string.Join(" | ", lines));
            return lines;
        }
    }
}
