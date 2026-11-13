using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rice.Server.Core;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Amigos, party e perfil com os bots (modo offline). Layouts do cliente 2016 pela documentacao do openDC.
    //   230 FriendList -> 231 ; 232 FriendAddByName -> 234 (+225 quando o bot aceita) ; 235 FriendDel -> 237
    //   228 PartyPreCheck -> 229 ; 240 PartyInvite -> 243 PartyJoinResult (bot aceitou) ou 241 ; 245 Banish
    //   660 GameCharInfo (nome) -> 661 (perfil de um bot)
    // Amigos ficam em friends.json (por personagem). A party e so na memoria (como no jogo original).
    public static class BotSocial
    {
        const string FriendsPath = "friends.json";
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static Dictionary<long, List<long>> friends;

        public const int PartyMax = 4;          // o jogador + 3 bots

        public class PartyData
        {
            public int Id;
            public List<long> Bots = new List<long>();
        }

        static readonly Dictionary<long, PartyData> parties = new Dictionary<long, PartyData>();
        static int nextParty = 1;

        // ---- amigos -------------------------------------------------------------------------------------------

        static Dictionary<long, List<long>> Friends()
        {
            lock (Sync)
            {
                if (friends == null)
                {
                    try { friends = CharFiles.Exists(FriendsPath) ? JsonConvert.DeserializeObject<Dictionary<long, List<long>>>(CharFiles.ReadAllText(FriendsPath)) : null; }
                    catch (Exception ex) { Log.WriteError("friends.json invalido: " + ex.Message); }
                    if (friends == null) friends = new Dictionary<long, List<long>>();
                }
                return friends;
            }
        }

        static void SaveFriends()
        {
            lock (Sync)
            {
                try { CharFiles.WriteAllText(FriendsPath, JsonConvert.SerializeObject(friends, Formatting.Indented)); }
                catch (Exception ex) { Log.WriteError("Nao consegui gravar friends.json: " + ex.Message); }
            }
        }

        public static List<long> FriendsOf(long cid)
        {
            lock (Sync)
            {
                List<long> l;
                if (!Friends().TryGetValue(cid, out l)) { l = new List<long>(); friends[cid] = l; }
                return l;
            }
        }

        public static bool IsFriend(long cid, long bot) { lock (Sync) return FriendsOf(cid).Contains(bot); }

        // Corredor novo: 3 bots iniciantes viram amigos dele (e evoluem junto: StarterFriendsGrow)
        public static void AddStarterFriends(long cid, int count = 3)
        {
            var pool = Bots.All().Where(b => b.Beginner).ToList();
            if (pool.Count == 0) return;
            var free = pool.Where(b => b.FriendOf == 0).OrderBy(b => Rnd.Next()).ToList();
            var pick = free.Take(count).ToList();
            if (pick.Count < count) pick.AddRange(pool.Where(b => !pick.Contains(b)).OrderBy(b => Rnd.Next()).Take(count - pick.Count));
            lock (Sync)
            {
                var l = FriendsOf(cid);
                foreach (var b in pick)
                {
                    if (b.FriendOf == 0) b.FriendOf = cid;
                    if (!l.Contains(b.Cid)) l.Add(b.Cid);
                }
                SaveFriends();
            }
            Bots.Save();
            Log.WriteLine("Corredor novo {0}: amigos iniciais {1}.", cid, string.Join(", ", pick.Select(b => b.Name)));
        }

        // Os amigos iniciais acompanham o seu nivel (um pouco abaixo ou acima) e trocam de carro ao mudar de faixa
        public static void StarterFriendsGrow(Rice.Game.Character ch)
        {
            if (ch == null) return;
            bool changed = false;
            foreach (var b in Bots.All().Where(x => x.Beginner && x.FriendOf == (long) ch.CID && GrowthOf(x) >= 1))
            {
                int target = Math.Max(1, ch.Level + (int) (b.Cid % 4) - 2);
                if (target <= b.Level) continue;
                b.Level = target;
                var v = Rice.Server.Structures.Resources.VehicleTable.Vehicles;
                Rice.Server.Structures.Resources.VehicleListEntry e;
                int nextTier = (v.TryGetValue(b.Car, out e) ? e.Level : 1) + 1;
                Bots.TierCarFor(b);                         // carro do V do nivel (igual a loja)
                Bots.EliteFor(b);
                changed = true;
            }
            if (changed) Bots.Save();
        }

        static readonly Random Rnd = new Random();

        // Atributos do carro do bot (no grau dele): speed, accel, durab., boost
        // Golpe do bot como o do jogador: durabilidade total (carro + pecas + nivel) x 61-100% (o jogo usa a mesma faixa)
        static readonly Random HitRng = new Random();
        public static float HitDamage(Bots.BotProfile b)
        {
            if (b == null) return 0f;
            double crash = CarStats(b)[2] + Math.Max(1, b.Level);
            double r;
            lock (HitRng) r = 0.61 + HitRng.NextDouble() * 0.39;
            return (float) (Math.Max(1, crash) * r * Math.Max(0, RiceServer.Config.BotHitScale));
        }

        public static int[] CarStats(Bots.BotProfile b)
        {
            try
            {
                var u = Rice.Server.Structures.Resources.VehicleTable.GetVehicleUpgrade(b.Car, GradeOf(b));
                var r = new[] { u.Speed, u.Acceleration, u.Durability, u.Boost };
                // pecas que o bot ganhou na troca somam no atributo
                if (b.Parts != null)
                    foreach (var kv in b.Parts)
                    {
                        string key = kv.Key.Split('#')[0];
                        int k = Array.IndexOf(PartCats, key);
                        // option e dock (troca): S velocidade, A aceleracao, C durabilidade, B boost
                        if (k < 0 && (key.StartsWith("op_") || key.StartsWith("dock_"))) k = "SACB".IndexOf(key[key.Length - 1]);
                        var e = ItemTable.Items.FirstOrDefault(x => x.ID == kv.Value) as ItemTableEntry;
                        if (k >= 0 && e != null) r[k] += (int) e.BasePoints;
                    }
                return r;
            }
            catch (Exception) { return new[] { 60, 60, 60, 60 }; }
        }

        static byte[] WStr(string s, int bytes)
        {
            var r = new byte[bytes];
            var e = Encoding.Unicode.GetBytes(s ?? "");
            Array.Copy(e, r, Math.Min(e.Length, bytes - 2));
            return r;
        }

        static string ReadWStr(byte[] b, int at, int bytes)
        {
            if (b.Length < at + 2) return "";
            return Encoding.Unicode.GetString(b, at, Math.Min(bytes, b.Length - at)).Split('\0')[0].Trim();
        }

        // Quem esta online: sorteado de novo toda vez que voce entra num corredor (BotsOfflineChance fica offline; 0 = todos online).
        // Os da sua party e os especiais ficam sempre online.
        static HashSet<long> offline;
        static readonly Random OnRng = new Random();
        public static void RerollOnline(Rice.Game.Character ch)
        {
            try
            {
                var keep = new HashSet<long>();
                if (ch != null) foreach (var p in PartyBots(ch)) keep.Add(p.Cid);
                var n = new HashSet<long>();
                foreach (var b in Bots.All())
                    if (!b.Special && !Bots.IsAce(b) && !b.NeoBot && !keep.Contains(b.Cid) && OnRng.NextDouble() < RiceServer.Config.BotsOfflineChance) n.Add(b.Cid);
                offline = n;
                Log.WriteLine("Bots: {0} online agora (sorteio desta entrada).", Bots.All().Count - n.Count);
            }
            catch (Exception ex) { Log.WriteError("Bots online: " + ex.Message); }
        }
        public static bool Online(Bots.BotProfile b)
        {
            if (b.IsAlt) return true;
            if (!RiceServer.Config.BotsAppearOnline) return false;
            if (b.Special || Bots.IsAce(b)) return true;
            if (b.MostWanted) return CrewLadder.AnyMostWanted();      // Most Wanted so aparece depois da LegendsDC
            var off = offline;
            return off != null ? !off.Contains(b.Cid) : (RiceServer.Config.BotsOfflineChance <= 0 || b.Cid % 3 != 0);
        }

        public static uint SerialOf(Bots.BotProfile b) { return (uint) ((b.IsAlt ? 0x6000 : 0x7000) + (b.Cid % 0x0fff)); }

        public static Bots.BotProfile BySerial(uint serial)
        {
            if (serial >= 0x6000 && serial < 0x7000)
                return Bots.ByCid(Bots.AltCids().FirstOrDefault(c => 0x6000 + (c % 0x0fff) == serial));
            if (serial < 0x7000 || serial > 0x7fff) return null;
            return Bots.All().FirstOrDefault(b => SerialOf(b) == serial);
        }

        public static readonly string[] PartCats = { "speed", "accel", "crash", "boost" };

        public static int GradeOf(Bots.BotProfile b)
        {
            // nunca abaixo do V do carro (um carro V7 aparece no minimo V7)
            VehicleListEntry v;
            int lv = VehicleTable.Vehicles.TryGetValue(b.Car, out v) && v != null ? Math.Max(1, v.Level) : 1;
            if (b.CarGrade >= 1 && b.CarGrade <= 9) return Math.Max(lv, b.CarGrade);
            return lv;
        }

        // XiStrFriend (0x70)
        static byte[] FriendRecord(Bots.BotProfile b, char state)
        {
            var o = new byte[0x70];
            var t = Crew.Of(b.Cid);
            bool on = Online(b);
            WStr(b.Name, 42).CopyTo(o, 0);
            WStr(t != null ? t.Name : "", 26).CopyTo(o, 0x2a);
            BitConverter.GetBytes(b.Cid).CopyTo(o, 0x44);
            BitConverter.GetBytes(t != null ? t.Tid : -1L).CopyTo(o, 0x4c);
            BitConverter.GetBytes(t != null ? t.Mark : -1L).CopyTo(o, 0x54);
            BitConverter.GetBytes((int) state).CopyTo(o, 0x5c);
            if (on) o[0x60] = 1;                                // na cidade
            BitConverter.GetBytes((ushort) b.Level).CopyTo(o, 0x68);
            BitConverter.GetBytes((ushort) GradeOf(b)).CopyTo(o, 0x6a);
            BitConverter.GetBytes(on ? SerialOf(b) : 0u).CopyTo(o, 0x6c);
            return o;
        }

        static void Send(RiceClient c, ushort id, Action<PacketWriter> f)
        {
            var p = new RicePacket(id);
            f(p.Writer);
            c.Send(p);
        }

        static Rice.Game.Character Me(RicePacket packet)
        {
            return packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
        }

        [RicePacket(230, RiceServer.ServerType.Game)]
        public static void FriendList(RicePacket packet)
        {
            var ch = Me(packet);
            if (ch == null) return;
            var list = Bots.AltsOf(ch).Concat(FriendsOf((long) ch.CID).Select(Bots.ByCid).Where(b => b != null)).ToList();
            Send(packet.Sender, 231, w =>
            {
                w.Write(list.Count);
                w.Write(0x40000u);
                foreach (var b in list) w.Write(FriendRecord(b, 'F'));
            });
        }

        public static string AddFriend(RiceClient c, string name)
        {
            var ch = c.Player.ActiveCharacter;
            var b = Bots.ByName(name);
            if (b == null) return "'" + name + "' nao existe (so da para adicionar bots no modo offline).";
            if (b.IsAlt) return b.Name + " e outro corredor seu: ja aparece na sua lista de amigos.";
            lock (Sync)
            {
                var l = FriendsOf((long) ch.CID);
                if (l.Contains(b.Cid)) return b.Name + " ja e seu amigo.";
                l.Add(b.Cid);
                SaveFriends();
            }
            Send(c, 234, w => w.Write(FriendRecord(b, 'R')));
            Task.Delay(1500 + Rng.Next(2000)).ContinueWith(_ =>
            {
                try
                {
                    Send(c, 225, w => w.Write(FriendRecord(b, 'F')));
                    Chat.SendSystemMessage(c, b.Name + " aceitou seu pedido de amizade.");
                    // bot sem crew que vira seu amigo entra na sua crew (se voce tiver uma e tiver vaga)
                    var myCrew = RiceServer.Config.CrewLadder ? Crew.Of((long) ch.CID) : null;   // so com o mod Escada de crews
                    string noSlot = null;
                    if (myCrew != null && Crew.Of(b.Cid) == null && !b.Special && !Bots.IsAce(b) && !b.MostWanted && b.Level > ch.Level)
                        Bots.SayLater(c, 1500, "channel", b.Name, "Amizade sim, mas na sua crew so quando voce passar do meu nivel (" + b.Level + ").");
                    else if (myCrew != null && Crew.Of(b.Cid) == null && !b.Special && !Bots.IsAce(b) && !b.MostWanted && !CrewLadder.CanRecruit(ch, out noSlot))
                        Chat.SendSystemMessage(c, b.Name + " virou seu amigo, mas nao entrou na crew: " + noSlot);
                    else if (myCrew != null && Crew.Of(b.Cid) == null && !b.Special && !Bots.IsAce(b) && !b.MostWanted)
                    {
                        CrewLadder.NoteRecruit(ch, b.Cid);
                        Crew.AddMember(myCrew, b.Cid);
                        b.CrewTid = myCrew.Tid; b.CrewName = myCrew.Name;
                        Bots.Save();
                        Chat.SendSystemMessage(c, b.Name + " (nivel " + b.Level + ") estava sem crew e entrou na sua crew (" + myCrew.Name + ").");
                        Bots.SayLater(c, 1500, "team", b.Name, "Valeu pelo convite, " + ch.Name + "! To na " + myCrew.Name + " agora.");
                        Log.WriteLine("Crew: {0} (amigo de {1}) entrou na {2}.", b.Name, ch.Name, myCrew.Name);
                    }
                    if (b.Personality != "calado")
                        Bots.Say(c, "channel", b.Name, b.Personality == "competitivo"
                            ? "Amigo aceito! Mas na pista eu nao alivio, hein " + ch.Name + "."
                            : "Valeu pela amizade, " + ch.Name + "! Me chama pra party quando quiser.");
                }
                catch (Exception ex) { Log.WriteError("AddFriend: " + ex.Message); }
            });
            Log.WriteLine("Amizade: {0} -> bot {1}.", ch.Name, b.Name);
            return null;
        }

        [RicePacket(232, RiceServer.ServerType.Game)]
        public static void FriendAddByName(RicePacket packet)
        {
            if (Me(packet) == null) return;
            string name = ReadWStr(packet.Buffer, 0, 42);
            Log.WriteLine("FriendAddByName '{0}'", name);
            string err = AddFriend(packet.Sender, name);
            if (err != null) Bots.Popup(packet.Sender, err);
        }

        [RicePacket(235, RiceServer.ServerType.Game)]
        public static void FriendDel(RicePacket packet)
        {
            var ch = Me(packet);
            if (ch == null) return;
            string name = ReadWStr(packet.Buffer, 0, 42);
            var b = Bots.ByName(name);
            Log.WriteLine("FriendDel '{0}'", name);
            if (b == null) return;
            lock (Sync)
            {
                FriendsOf((long) ch.CID).Remove(b.Cid);
                SaveFriends();
            }
            Send(packet.Sender, 237, w =>
            {
                w.Write(b.Cid); w.Write((short) 0); w.Write(WStr(ch.Name, 42)); w.Write(WStr(b.Name, 42));
            });
        }

        // ---- party --------------------------------------------------------------------------------------------

        public static PartyData PartyOf(Rice.Game.Character ch)
        {
            if (ch == null) return null;
            lock (parties)
            {
                PartyData p;
                return parties.TryGetValue((long) ch.CID, out p) ? p : null;
            }
        }

        // Quem evolui (BotsFrozen): 2 = na party de um jogador (rapido), 1 = na crew de um jogador (devagar, so nivel),
        // 0 = parado (nao sobe nivel, nao compra carro/peca, nao pega licenca). Sem BotsFrozen: todos evoluem (2).
        public static int GrowthOf(Bots.BotProfile b)
        {
            if (b == null) return 0;
            if (!RiceServer.Config.BotsFrozen) return 2;
            foreach (var pl in RiceServer.GetPlayers())
            {
                var ch = pl != null ? pl.ActiveCharacter : null;
                if (ch != null && PartyBots(ch).Contains(b)) return 2;
            }
            var bc = Crew.Of(b.Cid);
            if (bc != null)
                foreach (var pl in RiceServer.GetPlayers())
                {
                    var ch = pl != null ? pl.ActiveCharacter : null;
                    var mc = ch != null ? Crew.Of((long) ch.CID) : null;
                    if (mc != null && mc.Tid == bc.Tid) return 1;
                }
            return 0;
        }

        public static List<Bots.BotProfile> PartyBots(Rice.Game.Character ch)
        {
            var p = PartyOf(ch);
            return p == null ? new List<Bots.BotProfile>() : p.Bots.Select(Bots.ByCid).Where(b => b != null).ToList();
        }

        // XiStrPartyMemberInfo (58): i64 cid, nome 42, u16 avatar, u16 nivel, u32 grau do carro
        static void WriteMember(PacketWriter w, long cid, string name, int avatar, int level, int grade)
        {
            w.Write(cid); w.Write(WStr(name, 42)); w.Write((ushort) avatar); w.Write((ushort) level); w.Write((uint) grade);
        }

        public static void SendPartyState(RiceClient c)
        {
            var ch = c != null && c.Player != null ? c.Player.ActiveCharacter : null;
            var p = PartyOf(ch);
            if (p == null) return;
            var bots = PartyBots(ch);
            var v = ch.Vehicle;
            int myGrade = v != null ? (int) v.Grade : 1;
            Send(c, 243, w =>
            {
                w.Write(1u); w.Write(p.Id); w.Write(WStr(ch.Name + "'s party", 60));
                w.Write((uint) (bots.Count + 1));
                w.Write((uint) ch.Serial); WriteMember(w, (long) ch.CID, ch.Name, ch.Avatar, ch.Level, myGrade);
                foreach (var b in bots)
                {
                    w.Write(SerialOf(b)); WriteMember(w, b.Cid, b.Name, b.Avatar, b.Level, GradeOf(b));
                }
            });
        }

        // Retorna null se o bot entrou; senao, o motivo.
        public static string Invite(RiceClient c, Bots.BotProfile b)
        {
            var ch = c.Player.ActiveCharacter;
            if (b == null) return "Esse jogador nao existe.";
            // da sua crew: sempre aceita (mesmo "offline")
            var myCrew = Crew.Of((long) ch.CID); var bc = Crew.Of(b.Cid);
            bool crewMate = myCrew != null && bc != null && myCrew.Tid == bc.Tid;
            if (!Online(b) && !crewMate) return b.Name + " esta offline agora.";
            if (Bots.InTutorial(ch)) return "Termine o tutorial e pegue o seu primeiro carro antes de fazer party.";
            if (b.IsAlt && b.InTutorial) return b.Name + " ainda nao terminou o tutorial (esta com o carro de treino). Entre com ele e termine primeiro.";
            PartyData p;
            lock (parties)
            {
                if (!parties.TryGetValue((long) ch.CID, out p)) p = null;
                if (p != null && p.Bots.Contains(b.Cid)) return b.Name + " ja esta na sua party.";
                if (p != null && p.Bots.Count + 1 >= PartyMax) return "A party esta cheia (maximo " + PartyMax + ").";
            }
            bool friend = b.IsAlt || IsFriend((long) ch.CID, b.Cid) || crewMate;
            double chance = friend ? 1.0 : b.Personality == "calado" ? 0.3 : b.Personality == "competitivo" ? 0.5 : 0.75;
            if (Rng.NextDouble() > chance)
                return b.Name + " recusou o convite." + (friend ? "" : " (Amigos sempre aceitam: adicione-o como amigo.)");
            lock (parties)
            {
                if (!parties.TryGetValue((long) ch.CID, out p))
                {
                    p = new PartyData { Id = nextParty++ };
                    parties[(long) ch.CID] = p;
                }
                p.Bots.Add(b.Cid);
            }
            Log.WriteLine("Party: {0} entrou na party de {1}.", b.Name, ch.Name);
            if (b.IsAlt && !b.CustomLook)
            {
                // seu corredor: o visual de agora (pintura/vidro/pecas compradas depois do login)
                try
                {
                    var altCh = AltCharacter(c.Player, b);
                    var altCar = altCh != null ? altCh.Vehicle : null;
                    if (altCar != null)
                    {
                        b.Look = VisualShop.LookOf((long) altCh.CID, altCar.CarID, altCar.Color);
                        if (b.Look != null && b.Look.Paint != 0) b.Color = b.Look.Paint;
                    }
                }
                catch (Exception ex) { Log.WriteError("Party: visual de " + b.Name + ": " + ex.Message); }
            }
            SendPartyState(c);
            Bots.SayLater(c, 1200, "party", b.Name, b.Personality == "competitivo" ? "Partiu! Cria a sala que eu corro com voce."
                                                   : "Opa, to junto! Cria uma sala no Battle Zone que eu entro na corrida.");
            return null;
        }

        // ---- convite de party feito por um bot (BotChatter: chamou para HUV/Rush/Undercity e voce aceitou) ------
        // 240 PartyInvite para o jogador: u32 serial de quem convida, u32 ticket, XiStrPartyMemberInfo (58), nome da
        // party (wchar 30), texto (wchar 100). O jogador responde 242 PartyJoin (aceitou) ou 241 PartyReject.
        class BotInvite { public long Leader; public List<long> Group = new List<long>(); public DateTime Until; public uint Ticket; }
        static readonly Dictionary<long, BotInvite> botInvites = new Dictionary<long, BotInvite>();
        static uint nextTicket = 0x5000;

        public static string InvitePlayer(RiceClient c, Bots.BotProfile leader, List<Bots.BotProfile> group, string text)
        {
            var ch = c != null && c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null || leader == null) return "sem jogador";
            if (Bots.InTutorial(ch)) return "Termine o tutorial e pegue o seu primeiro carro antes de fazer party.";
            var mine = PartyBots(ch);
            if (mine.Contains(leader)) return leader.Name + " ja esta na sua party.";
            if (mine.Count + 1 >= PartyMax) return "A sua party esta cheia (maximo " + PartyMax + ").";
            var inv = new BotInvite { Leader = leader.Cid, Until = DateTime.UtcNow.AddSeconds(90), Ticket = nextTicket++ };
            if (group != null) foreach (var g in group) if (g != null && g != leader && !g.IsAlt) inv.Group.Add(g.Cid);
            lock (botInvites) botInvites[(long) ch.CID] = inv;
            Send(c, 240, w =>
            {
                w.Write(SerialOf(leader)); w.Write(inv.Ticket);
                WriteMember(w, leader.Cid, leader.Name, leader.Avatar, leader.Level, GradeOf(leader));
                w.Write(WStr(leader.Name + "'s party", 60));
                w.Write(WStr(text ?? "", 200));
            });
            Log.WriteLine("Party: {0} convidou {1} para a party.", leader.Name, ch.Name);
            return null;
        }

        // 242 PartyJoin: o jogador aceitou o convite de um bot. Retorna true se era convite de bot.
        public static bool OnJoin(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return false;
            var leader = BySerial(BitConverter.ToUInt32(buf, 0));
            if (leader == null) return false;
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            BotInvite inv;
            lock (botInvites)
            {
                if (!botInvites.TryGetValue((long) ch.CID, out inv) || inv.Leader != leader.Cid || DateTime.UtcNow > inv.Until) inv = null;
                botInvites.Remove((long) ch.CID);
            }
            if (inv == null) { Chat.SendSystemMessage(c, "Esse convite de party ja expirou."); return true; }
            var add = new List<Bots.BotProfile> { leader };
            foreach (var id in inv.Group) { var g = Bots.ByCid(id); if (g != null && Online(g)) add.Add(g); }
            var names = new List<string>();
            lock (parties)
            {
                PartyData p;
                if (!parties.TryGetValue((long) ch.CID, out p)) { p = new PartyData { Id = nextParty++ }; parties[(long) ch.CID] = p; }
                foreach (var b in add)
                {
                    if (p.Bots.Contains(b.Cid) || p.Bots.Count + 1 >= PartyMax) continue;
                    p.Bots.Add(b.Cid); names.Add(b.Name);
                }
            }
            Bots.Ungroup(leader.Cid);
            Log.WriteLine("Party: {0} aceitou o convite de {1} ({2}).", ch.Name, leader.Name, string.Join(", ", names));
            SendPartyState(c);
            Bots.SayLater(c, 1200, "party", leader.Name, "Boa, " + ch.Name + "! Bora, vamos juntos.");
            return true;
        }

        public static bool OnReject(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return false;
            var leader = BySerial(BitConverter.ToUInt32(buf, 0));
            if (leader == null) return false;
            var ch = packet.Sender.Player.ActiveCharacter;
            lock (botInvites) botInvites.Remove((long) ch.CID);
            Bots.SayLater(packet.Sender, 1500, "channel", leader.Name, "Beleza, fica para a proxima.");
            return true;
        }

        public static void Remove(RiceClient c, Bots.BotProfile b, bool banished)
        {
            var ch = c.Player.ActiveCharacter;
            bool empty = false;
            lock (parties)
            {
                PartyData p;
                if (!parties.TryGetValue((long) ch.CID, out p) || !p.Bots.Remove(b.Cid)) return;
                if (p.Bots.Count == 0) { parties.Remove((long) ch.CID); empty = true; }
            }
            Send(c, (ushort) (banished ? 245 : 244), w => w.Write(SerialOf(b)));
            if (empty) Send(c, 244, w => w.Write((uint) ch.Serial));
            else SendPartyState(c);
            Log.WriteLine("Party: {0} saiu da party de {1}.", b.Name, ch.Name);
        }

        public static void Disband(RiceClient c)
        {
            var ch = c.Player.ActiveCharacter;
            lock (parties) parties.Remove((long) ch.CID);
            Send(c, 244, w => w.Write((uint) ch.Serial));
        }

        // 228: o cliente pergunta quem e o jogador (serial ou nome). Retorna true se era um bot.
        public static bool PreCheck(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return false;
            uint serial = BitConverter.ToUInt32(buf, 0);
            string name = ReadWStr(buf, 4, 42);
            var b = !string.IsNullOrEmpty(name) ? Bots.ByName(name) : BySerial(serial);
            if (b == null) return false;
            if (!Online(b))
            {
                Bots.Popup(packet.Sender, b.Name + " esta offline agora.");
                return true;
            }
            Send(packet.Sender, 229, w =>
            {
                w.Write(SerialOf(b)); w.Write(b.Cid); w.Write(WStr(b.Name, 42)); w.Write(-1); w.Write(new byte[60]);
            });
            return true;
        }

        // 240: convite. Retorna true se era para um bot.
        public static bool OnInvite(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return false;
            var b = BySerial(BitConverter.ToUInt32(buf, 0));
            if (b == null) return false;
            string err = Invite(packet.Sender, b);
            if (err != null)
            {
                Log.WriteLine("Party: convite para {0} recusado ({1}).", b.Name, err);
                uint ticket = buf.Length >= 8 ? BitConverter.ToUInt32(buf, 4) : 0;
                Send(packet.Sender, 241, w =>
                {
                    w.Write(SerialOf(b)); w.Write(ticket); WriteMember(w, b.Cid, b.Name, b.Avatar, b.Level, GradeOf(b));
                    w.Write(new byte[200]);
                });
                Chat.SendSystemMessage(packet.Sender, err);
            }
            return true;
        }

        public static bool OnBanish(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return false;
            var b = BySerial(BitConverter.ToUInt32(buf, 0));
            if (b == null) return false;
            Remove(packet.Sender, b, true);
            return true;
        }

        // O cliente manda 244 PartyLeave ao trocar de area; a party de bots continua (use !!party sair para desfazer).
        public static void OnAreaJoin(Rice.Game.Player player)
        {
            var c = player != null ? player.GameClient : null;
            if (c == null || PartyOf(player.ActiveCharacter) == null) return;
            Task.Delay(1500).ContinueWith(_ =>
            {
                try { SendPartyState(c); }
                catch (Exception ex) { Log.WriteError("Party reenviar: " + ex.Message); }
            });
        }

        // Corredores da conta (alts) na party ganham junto: exp e mito x AltRewardShare.
        // personagem de um outro corredor seu (mesma conta: o que ja esta carregado; outra conta: o que esta online agora
        // ou carregado do banco). null = nao e corredor seu.
        public static Rice.Game.Character AltCharacter(Rice.Game.Player player, Bots.BotProfile b)
        {
            if (b == null || !b.IsAlt) return null;
            // jogando com ele agora: o objeto ativo (e o que esta valendo)
            var on = RiceServer.GetPlayers().Where(p => p != null && p.ActiveCharacter != null).Select(p => p.ActiveCharacter).FirstOrDefault(x => (long) x.CID == b.Cid);
            if (on != null) return on;
            // da sua lista de corredores: a copia carregada no login pode estar velha (exp/mito/nivel ganhos depois por
            // outra copia). Ganhar em cima dela gravava o valor velho + o premio e parecia que nao ganhava. Atualiza do banco.
            var own = player != null && player.Characters != null ? player.Characters.FirstOrDefault(x => x != null && (long) x.CID == b.Cid) : null;
            if (own != null)
            {
                try
                {
                    using (var rc = Rice.Database.GetContext())
                    {
                        var dc = rc.Characters.Find((long) b.Cid);
                        if (dc != null) { own.Experience = dc.Experience; own.Level = dc.Level; own.Mito = dc.Mito; }
                    }
                }
                catch (Exception ex) { Log.WriteError("Corredor " + b.Name + ": " + ex.Message); }
                return own;
            }
            try { return Rice.Game.Character.Retrieve(b.Name); }
            catch (Exception ex) { Log.WriteError("Corredor " + b.Name + ": " + ex.Message); return null; }
        }

        public static void ShareRewards(Rice.Game.Character ch, long exp, long mito, HashSet<long> skipAlts = null)
        {
            if (ch == null || (exp <= 0 && mito <= 0)) return;
            var player = RiceServer.GetPlayers().FirstOrDefault(p => p.ActiveCharacter == ch);
            if (player == null) return;
            var cfg0 = RiceServer.Config;
            // bots da party (que nao sao seus corredores): ganham exp e mito com voce, sobem de nivel e de licenca.
            // Cada um conta no chat da party: exp que falta para o proximo nivel e o progresso das licencas.
            int said = 0;
            foreach (var b in PartyBots(ch).Where(x => !x.IsAlt && !x.Special).ToList())
            {
                long e = (long) Math.Round(exp * Math.Max(0, cfg0.PartyBotExpShare));
                long m = (long) Math.Round(mito * Math.Max(0, cfg0.PartyBotExpShare));
                if (e <= 0 && m <= 0) continue;
                if (b.Mito < 0) b.Mito = 0;
                b.Mito += m;
                int up = Bots.AddExp(b, e);
                string lic = Licenses.Activity(b, cfg0.BotLicenseActivity);
                string line = "+" + e + " exp" + (up > 0 ? " | NIVEL " + b.Level + "!" : "") + " | nivel " + b.Level + ": " + b.Exp + "/" + Bots.ExpNeed(b)
                              + " exp p/ o " + (b.Level + 1) + (lic.Length > 0 ? " | Licencas: " + lic : "");
                // um de cada vez (mensagens juntas no mesmo instante o jogo mostrava so uma): todos da party aparecem
                if (player.GameClient != null && cfg0.PartyBotExpTable) Bots.SayLater(player.GameClient, 1500 + 900 * said++, "party", b.Name, line);
            }
            Bots.Save();
            var party = PartyBots(ch).Where(b => b.IsAlt && (skipAlts == null || !skipAlts.Contains(b.Cid))).ToList();
            if (party.Count == 0) return;
            double share = RiceServer.Config.AltRewardShare;
            var parts = new List<string>();
            foreach (var b in party)
            {
                var alt = AltCharacter(player, b);
                if (alt == null) continue;
                long e = (long) (exp * share), m = (long) (mito * share);
                int before = alt.Level;
                if (e > 0) alt.GrantExperience(e);
                if (m > 0) alt.GrantMito((ulong) m);
                b.Level = alt.Level;
                Log.WriteLine("Party: {0} (seu corredor) ganhou +{1} exp e +{2} mito (nivel {3}, mito {4}).", alt.Name, e, m, alt.Level, alt.Mito);
                parts.Add(alt.Name + (e > 0 ? " +" + e + " exp" : "") + (m > 0 ? " +" + m + " mito" : "")
                          + (alt.Level > before ? " (nivel " + alt.Level + "!)" : ""));
            }
            if (parts.Count > 0 && player.GameClient != null)
                Chat.SendSystemMessage(player.GameClient, "Sua party ganhou junto: " + string.Join(", ", parts) + ".");
        }

        // Bonus de party nas recompensas (HUV): +PartyBotBonus por bot na party.
        public static double RewardBonus(Rice.Game.Character ch)
        {
            var p = PartyOf(ch);
            if (p == null) return 1.0;
            double extra = p.Bots.Select(Bots.ByCid).Where(x => x != null).Sum(x => Licenses.BotRewardBonus(x));   // licencas
            return 1.0 + RiceServer.Config.PartyBotBonus * p.Bots.Count + extra;
        }

        // ---- perfil (F2 / clicar no nome) ---------------------------------------------------------------------
        // 660 GameCharInfo (nome) -> 661: CharInfo (0x141) + CarInfo (0x32) + Stats (0x70) + TeamInfo (664)
        //    + u32 serial + u32 cidade, total 0x497 bytes.
        [RicePacket(660, RiceServer.ServerType.Game)]
        public static void GameCharInfo(RicePacket packet)
        {
            string name = ReadWStr(packet.Buffer, 0, 42);
            var b = Bots.ByName(name);
            // corredor de verdade (voce no F2 "My Information", ou um corredor seu de outra conta): os dados reais dele
            if (b == null || b.IsAlt)
            {
                Rice.Game.Character real = null;
                try
                {
                    real = RiceServer.GetPlayers().Where(p => p != null && p.ActiveCharacter != null).Select(p => p.ActiveCharacter)
                                     .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (real == null)
                        using (var rc = Rice.Database.GetContext())
                            if (rc.Characters.Any(x => x.Name == name)) real = Rice.Game.Character.Retrieve(name);
                }
                catch (Exception ex) { Log.WriteError("Perfil de " + name + ": " + ex.Message); }
                if (real != null) { SendRealInfo(packet.Sender, real, b); return; }
                if (b == null) { Log.WriteLine("Perfil: {0} nao encontrado.", name); return; }
            }
            Log.WriteLine("Perfil do bot {0}.", b.Name);
            var t = Crew.Of(b.Cid);
            var ms = new MemoryStream();
            var pw = new PacketWriter(ms);

            var info = new CharInfo
            {
                CID = (ulong) b.Cid, Name = b.Name, LastMessageFrom = "", Avatar = (ushort) b.Avatar, Level = (ushort) b.Level,
                ExpInfo = new ExpInfo(), MitoMoney = 0, TeamId = t != null ? t.Tid : -1, TeamMarkId = t != null ? t.Mark : -1,
                TeamName = t != null ? t.Name : "", TeamRank = t == null ? -1 : (t.Master == b.Cid ? 1 : 3), PType = (byte) 'A',
                PvpCnt = b.Sold + 20, PvpWinCnt = b.Sold / 2 + 5, City = 1, CurrentCarID = 1,
                TeamJoinDate = DateTime.Now, TeamCloseDate = DateTime.Now, TeamLeaveDate = DateTime.Now, Flags = -1,
                Position = new Vector4(0, 0, 0)
            };
            pw.Write(info);
            Pad(ms, pw, 0x141);

            int grade = GradeOf(b);
            float cap = 100f;
            List<VehicleUpgradeEntry> ups;
            VehicleUpgradeEntry up = null;
            if (VehicleTable.VehicleUpgrades.TryGetValue(b.Car, out ups) && ups.Count >= grade) up = ups[grade - 1];
            if (up != null) cap = Rice.Game.Vehicle.ScaledCapacity(up);
            pw.Write(new CarInfo
            {
                CarUnit = new CarUnit { CarID = 1, CarType = (uint) b.Car, BaseColor = 0, Grade = (uint) grade, SlotType = 0, Mitron = cap, Kmh = b.Level * 1500f },
                Color = b.Color, Color2 = 0, MitronCapacity = cap, MitronEfficiency = up != null ? up.MitronEfficiency : 1f
            });
            Pad(ms, pw, 0x141 + 0x32);

            int bonus = b.Level / 2;
            pw.Write(new StatUpdate
            {
                BaseSpeed = up != null ? up.Speed : 0, BaseDurability = up != null ? up.Durability : 0,
                BaseAcceleration = up != null ? up.Acceleration : 0, BaseBoost = up != null ? up.Boost : 0,
                EquipSpeed = bonus, EquipDurability = bonus, EquipAcceleration = bonus, EquipBoost = bonus
            });
            Pad(ms, pw, 0x141 + 0x32 + 0x70);

            pw.Write(Crew.Info(t));
            pw.Write(Online(b) ? SerialOf(b) : 0u);
            pw.Write(1u);
            Pad(ms, pw, 0x497);

            var data = ms.ToArray();
            Send(packet.Sender, 661, w => w.Write(data, 0, 0x497));
        }

        // perfil (661) de um corredor de verdade: nivel, carro, atributos, crew e o recorde de corridas salvo
        static void SendRealInfo(RiceClient c, Rice.Game.Character ch, Bots.BotProfile asBot)
        {
            Log.WriteLine("Perfil do corredor {0}.", ch.Name);
            var ms = new MemoryStream();
            var pw = new PacketWriter(ms);
            pw.Write(ch.GetInfo());
            Pad(ms, pw, 0x141);
            var car = ch.Vehicle ?? (ch.Garage != null ? ch.Garage.FirstOrDefault() : null);
            if (car != null) pw.Write(car.GetInfo());
            Pad(ms, pw, 0x141 + 0x32);
            try { pw.Write(ch.GetStatUpdate()); } catch (Exception) { }
            Pad(ms, pw, 0x141 + 0x32 + 0x70);
            pw.Write(Crew.Info(Crew.Of((long) ch.CID)));
            uint serial = asBot != null ? (Online(asBot) ? SerialOf(asBot) : 0u) : ch.Serial;
            pw.Write(serial);
            pw.Write((uint) Math.Max(1, ch.City));
            Pad(ms, pw, 0x497);
            var data = ms.ToArray();
            Send(c, 661, w => w.Write(data, 0, 0x497));
        }

        static void Pad(MemoryStream ms, PacketWriter pw, int size)
        {
            pw.Flush();
            if (ms.Length < size) pw.Write(new byte[size - ms.Length]);
            else if (ms.Length > size) { ms.SetLength(size); ms.Position = size; }
            pw.Flush();
        }

        // ---- comandos ------------------------------------------------------------------------------------------

        public static bool HandleCommand(RiceClient c, string lower, string body)
        {
            // !!<nome do bot da sua party>: ele mostra a garagem; !!<nome> <numero ou carro>: ele troca de carro
            var me0 = c.Player != null ? c.Player.ActiveCharacter : null;
            if (me0 != null)
            {
                var pb = PartyBots(me0).Where(x => !x.IsAlt && body.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase)
                                                   && (body.Length == x.Name.Length || body[x.Name.Length] == ' '))
                                       .OrderByDescending(x => x.Name.Length).FirstOrDefault();
                if (pb != null)
                {
                    string arg = body.Substring(pb.Name.Length).Trim();
                    var gar = Bots.GarageOf(pb);
                    if (arg.Length == 0)
                    {
                        var list = gar.Select((g, i) => (i + 1) + ") " + Bots.CarName(g.Car) + " V" + g.Grade + (g.Car == pb.Car ? " (usando)" : "")).ToList();
                        Bots.SayLater(c, 600, "party", pb.Name, "Minha garagem: " + string.Join(", ", list) + ". Pra eu trocar: !!" + pb.Name + " <numero>");
                        return true;
                    }
                    int n;
                    Bots.BotCar pick = int.TryParse(arg, out n) && n >= 1 && n <= gar.Count ? gar[n - 1]
                                     : gar.FirstOrDefault(g => Bots.CarName(g.Car).IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (pick == null) { Bots.SayLater(c, 600, "party", pb.Name, "Nao tenho esse carro. Digite !!" + pb.Name + " para ver a garagem."); return true; }
                    if (pick.Car == pb.Car) { Bots.SayLater(c, 600, "party", pb.Name, "Ja estou com o " + Bots.CarName(pick.Car) + "!"); return true; }
                    Bots.UseCar(pb, pick);
                    Bots.SayLater(c, 900, "party", pb.Name, BotChatter.PickLine("Beleza, peguei o " + Bots.CarName(pick.Car) + "!", "Fechou, agora vou de " + Bots.CarName(pick.Car) + "."));
                    Log.WriteLine("Bots: {0} trocou para o carro {1} (pedido de {2}).", pb.Name, pick.Car, me0.Name);
                    return true;
                }
            }
            if (lower.StartsWith("amigo "))
            {
                string err = AddFriend(c, body.Substring(6).Trim());
                if (err != null) Chat.SendSystemMessage(c, err);
                return true;
            }
            if (lower == "amigos")
            {
                var l = FriendsOf((long) c.Player.ActiveCharacter.CID).Select(Bots.ByCid).Where(x => x != null).ToList();
                Chat.SendSystemMessage(c, l.Count == 0 ? "Voce ainda nao tem amigos. Use !!amigo <nome>."
                    : "Amigos: " + string.Join(", ", l.Select(x => x.Name + (Online(x) ? " (online)" : ""))));
                return true;
            }
            if (lower.StartsWith("party "))
            {
                string arg = body.Substring(6).Trim();
                if (arg.Equals("sair", StringComparison.OrdinalIgnoreCase))
                {
                    Disband(c);
                    Chat.SendSystemMessage(c, "Party desfeita.");
                    return true;
                }
                string err = Invite(c, Bots.ByName(arg));
                if (err != null) Chat.SendSystemMessage(c, err);
                return true;
            }
            if (lower == "party")
            {
                var bots = PartyBots(c.Player.ActiveCharacter);
                Chat.SendSystemMessage(c, bots.Count == 0 ? "Sem party. Use !!party <nome do bot> (amigos sempre aceitam)."
                    : "Party: " + string.Join(", ", bots.Select(x => x.Name + " (" + x.Level + ")")) + ". !!party sair para desfazer.");
                return true;
            }
            return false;
        }
    }
}
