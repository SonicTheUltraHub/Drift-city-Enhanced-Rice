using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Crew (team) offline. As crews ficam em crews.json (ao lado do Rice.exe), sem mexer no banco.
    // Layouts do cliente 2016 conforme a documentacao do emulador openDC (XiStrTeamInfo = 664 bytes).
    //   851 CheckTeamName -> 852 ; 842 CreateTeam -> 843 ; 840 MyTeamInfo -> 841 ; 848 LeaveTeam -> 849
    public static class Crew
    {
        public const int InfoSize = 664;
        static readonly string FilePath = Paths.World("crews.json");
        static readonly object Sync = new object();

        public class CrewData
        {
            public long Tid;
            public long Mark;
            public string Name = "";
            public string Desc = "";
            public string Url = "";
            public long Master;
            public string MasterName = "";
            public int Created;
            public List<long> Members = new List<long>();
            public bool IsBot;
            public int MinLevel;
            public int Level = -1;          // cor da placa: 0 verde, 1 azul, 2 rosa, 3 laranja (-1 = automatico)
        }

        // Nivel (grau) da crew, que o cliente usa para a cor da placa: C verde, B azul, A rosa, S laranja.
        public static int LevelOf(CrewData t)
        {
            if (t == null) return 0;
            if (t.Level >= 0) return Math.Min(3, t.Level);
            if (t.IsBot)
            {
                switch (t.Name)
                {
                    case "Rookies": case "MoonFlow": case "KoiFlow": return 1;
                    case "MitoKings": case "KoiGhost": case "NightRiders": return 2;
                    default: return 3;
                }
            }
            return Math.Max(0, Math.Min(3, RiceServer.Config.PlayerCrewLevel));
        }

        static List<CrewData> crews;

        public static List<CrewData> All()
        {
            lock (Sync)
            {
                if (crews == null)
                {
                    try { crews = File.Exists(FilePath) ? JsonConvert.DeserializeObject<List<CrewData>>(File.ReadAllText(FilePath)) : null; }
                    catch (Exception ex) { Log.WriteError("crews.json invalido: " + ex.Message); }
                    if (crews == null) crews = new List<CrewData>();
                }
                return crews;
            }
        }

        static void Save()
        {
            lock (Sync) File.WriteAllText(FilePath, JsonConvert.SerializeObject(crews, Formatting.Indented));
        }

        public static void SaveAll() { Save(); }

        public static CrewData ById(long tid) { return All().FirstOrDefault(c => c.Tid == tid); }

        public static CrewData ByName(string name)
        {
            return All().FirstOrDefault(c => string.Equals(c.Name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static void RemoveMember(CrewData t, long cid)
        {
            lock (Sync)
            {
                t.Members.Remove(cid);
                if (t.Master == cid && t.Members.Count > 0)
                {
                    t.Master = t.Members[0];
                    var nb = Bots.ByCid(t.Master);
                    if (nb != null) t.MasterName = nb.Name;
                }
                Save();
            }
        }

        public static void AddMember(CrewData t, long cid)
        {
            lock (Sync)
            {
                if (!t.Members.Contains(cid)) t.Members.Add(cid);
                Save();
            }
        }

        static ushort visualAge = 1;

        // crew do corredor; sem crew, cria uma de graca (para os bots derrotados entrarem)
        public static CrewData EnsureFor(Rice.Game.Character ch)
        {
            if (ch == null) return null;
            var t = Of((long) ch.CID);
            if (t != null) return t;
            string baseName = (ch.Name ?? "Crew");
            string name = (baseName.Length > 8 ? baseName.Substring(0, 8) : baseName) + "Crew";
            for (int i = 2; !NameFree(name) && i < 99; i++) name = (baseName.Length > 6 ? baseName.Substring(0, 6) : baseName) + "Crew" + i;
            lock (Sync)
            {
                var all = All();
                t = new CrewData
                {
                    Tid = all.Count == 0 ? 100000 : all.Max(x => x.Tid) + 1, Mark = 1, Name = name, Desc = "Crew de " + ch.Name,
                    Master = (long) ch.CID, MasterName = ch.Name, Created = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                t.Members.Add((long) ch.CID);
                all.Add(t);
                Save();
            }
            ch.TID = t.Tid;
            Log.WriteLine("Crew '{0}' criada para {1} (bots derrotados entram nela).", t.Name, ch.Name);
            return t;
        }

        public static CrewData Of(long cid)
        {
            return All().FirstOrDefault(c => c.Members.Contains(cid));
        }

        static bool NameFree(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length <= 12
                   && !All().Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
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
            if (b.Length < at + bytes) return "";
            return Encoding.Unicode.GetString(b, at, bytes).Split('\0')[0].Trim();
        }

        public static byte[] Info(CrewData t)
        {
            var o = new byte[InfoSize];
            if (t == null)
            {
                BitConverter.GetBytes(-1L).CopyTo(o, 0);
                BitConverter.GetBytes(-1L).CopyTo(o, 8);
                return o;
            }
            BitConverter.GetBytes(t.Tid).CopyTo(o, 0);
            BitConverter.GetBytes(t.Mark).CopyTo(o, 8);
            WStr(t.Name, 26).CopyTo(o, 0x10);
            WStr(t.Desc, 122).CopyTo(o, 0x2a);
            var url = Encoding.ASCII.GetBytes(t.Url ?? "");
            Array.Copy(url, 0, o, 0xa4, Math.Min(url.Length, 32));
            BitConverter.GetBytes(t.Created).CopyTo(o, 0xc5);
            o[0xe9] = (byte) 'A';
            BitConverter.GetBytes((uint) t.Members.Count).CopyTo(o, 0xf8);
            // pontos da crew: o cliente calcula o grau da propria crew (cor da placa) por pontos / exp total
            int lv = LevelOf(t);
            uint pts = lv >= 3 ? 500000u : lv == 2 ? 100000u : lv == 1 ? 20000u : 0u;
            BitConverter.GetBytes(pts).CopyTo(o, 0xf0);
            BitConverter.GetBytes(t.Master).CopyTo(o, 0x110);
            BitConverter.GetBytes(t.Master).CopyTo(o, 0x118);
            WStr(t.MasterName, 42).CopyTo(o, 0x120);
            WStr(t.MasterName, 42).CopyTo(o, 0x14a);
            return o;
        }

        static void Send(RiceClient c, ushort id, Action<BinaryWriter> f)
        {
            var p = new RicePacket(id);
            f(p.Writer);
            c.Send(p);
        }

        public static void SendMyInfo(RiceClient c, uint act)
        {
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            long cid = ch != null ? (long) ch.CID : 0;
            var t = Of(cid);
            int rank = t == null ? -1 : (t.Master == cid ? 1 : 3);
            Send(c, 841, w =>
            {
                w.Write(act); w.Write(cid); w.Write(rank);
                w.Write(Info(t));
                w.Write((ushort) 1);
                w.Write(new byte[8]);
            });
        }

        [RicePacket(851, RiceServer.ServerType.Game)]
        public static void CheckTeamName(RicePacket packet)
        {
            var p = packet.Buffer;
            string name = ReadWStr(p, 0, Math.Min(34, p.Length));
            bool ok = NameFree(name);
            Log.WriteLine("CheckTeamName '{0}': {1}", name, ok ? "livre" : "em uso/invalido");
            var echo = new byte[34];
            Array.Copy(p, echo, Math.Min(34, p.Length));
            Send(packet.Sender, 852, w => { w.Write(ok ? 1 : 0); w.Write(echo); });
        }

        [RicePacket(842, RiceServer.ServerType.Game)]
        public static void CreateTeam(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("CreateTeam ({0} bytes)", p.Length);
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null || p.Length < 0xc5) return;
            var cfg = RiceServer.Config;
            string name = ReadWStr(p, 0x10, 26);
            string desc = ReadWStr(p, 0x2a, 122);
            string url = Encoding.ASCII.GetString(p, 0xa4, Math.Min(33, p.Length - 0xa4)).Split('\0')[0].Trim();
            long mark = BitConverter.ToInt64(p, 8);

            string refuse = null;
            int needLv = cfg.CrewLadder ? 1 : cfg.CrewCreateLevel;   // escada de crews: crew desde o nivel 1
            if (ch.Level < needLv) refuse = "nivel minimo " + needLv;
            else if (Of((long) ch.CID) != null) refuse = "voce ja esta numa crew";
            else if (!NameFree(name)) refuse = "nome em uso ou invalido (ate 12 letras)";
            else if (!ch.SpendMito((ulong) cfg.CrewCreateFee)) refuse = "mito insuficiente (" + cfg.CrewCreateFee + ")";
            if (refuse != null)
            {
                Chat.SendSystemMessage(packet.Sender, "Nao foi possivel criar a crew: " + refuse + ".");
                return;
            }

            CrewData t;
            lock (Sync)
            {
                var all = All();
                t = new CrewData
                {
                    Tid = all.Count == 0 ? 100000 : all.Max(x => x.Tid) + 1,
                    Mark = mark >= 1 && mark <= 10 ? mark : 1, Name = name, Desc = desc, Url = url,
                    Master = (long) ch.CID, MasterName = ch.Name,
                    Created = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                t.Members.Add((long) ch.CID);
                all.Add(t);
                Save();
            }
            ch.TID = t.Tid;
            Log.WriteLine("Crew '{0}' ({1}) criada por {2}.", t.Name, t.Tid, ch.Name);
            // os outros corredores da sua conta (slots) entram juntos como membros
            var pl = packet.Sender.Player;
            if (pl != null && pl.Characters != null)
            {
                var alts = new List<string>();
                foreach (var o in pl.Characters.Where(x => x != null && x.CID != ch.CID))
                {
                    var old = Of((long) o.CID);
                    if (old != null && old.Tid != t.Tid) RemoveMember(old, (long) o.CID);
                    AddMember(t, (long) o.CID);
                    o.TID = t.Tid;
                    alts.Add(o.Name);
                }
                if (alts.Count > 0)
                {
                    Chat.SendSystemMessage(packet.Sender, "Seus outros corredores entraram na crew: " + string.Join(", ", alts) + ".");
                    Log.WriteLine("Crew '{0}': corredores da conta entraram ({1}).", t.Name, string.Join(", ", alts));
                }
            }
            Send(packet.Sender, 843, w => { w.Write(1); w.Write(Info(t)); w.Write((ushort) 1); w.Write(ch.Mito); });
            Chat.SendSystemMessage(packet.Sender, "Crew '" + t.Name + "' criada! (-" + cfg.CrewCreateFee + " mito)");
        }

        [RicePacket(848, RiceServer.ServerType.Game)]
        public static void LeaveTeam(RicePacket packet)
        {
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch == null) return;
            var t = Of((long) ch.CID);
            if (t == null) return;
            lock (Sync)
            {
                t.Members.Remove((long) ch.CID);
                if (t.Master == (long) ch.CID)
                {
                    if (t.Members.Count == 0) All().Remove(t);
                    else t.Master = t.Members[0];
                }
                Save();
            }
            ch.TID = 0;
            Send(packet.Sender, 849, w => { w.Write(1); w.Write((ushort) 1); });
            Chat.SendSystemMessage(packet.Sender, "Voce saiu da crew '" + t.Name + "'.");
        }

        // ---- membros, entrar numa crew (de bots), info de outro jogador ------------------------------------------

        static byte[] MemberEntry(long cid, CrewData t, RiceClient me)
        {
            var o = new byte[0x50];
            string name; int level, car, serial = 0; uint color = 0x01010102; int joined;
            bool online;
            var ch = me != null && me.Player != null ? me.Player.ActiveCharacter : null;
            if (ch != null && (long) ch.CID == cid)
            {
                name = ch.Name; level = ch.Level;
                var v = ch.Vehicle;
                car = v != null ? (int) v.CarType : 1;
                if (v != null && v.Color != 0) color = v.Color;
                serial = ch.Serial; online = true;
                joined = t.Created;
            }
            else
            {
                var b = Bots.ByCid(cid);
                if (b == null) return null;
                name = b.Name; level = b.Level; car = b.Car; color = b.Color; joined = b.Joined;
                online = RiceServer.Config.BotsAppearOnline && (RiceServer.Config.BotsOfflineChance <= 0 || cid % 3 != 0);
                serial = online ? (int) (0x7000 + (cid % 0x0fff)) : 0;
            }
            BitConverter.GetBytes(cid).CopyTo(o, 0);
            WStr(name, 34).CopyTo(o, 8);
            BitConverter.GetBytes(t.Master == cid ? 1 : 3).CopyTo(o, 0x2a);
            BitConverter.GetBytes(level).CopyTo(o, 0x2e);
            // XiCarAttr (16 bytes no 2016): u16 0, u16 tipo do carro, u32 cor, u32 0x82cccccc, 4 zeros
            BitConverter.GetBytes((ushort) car).CopyTo(o, 0x34);
            BitConverter.GetBytes(color).CopyTo(o, 0x36);
            BitConverter.GetBytes(0x82ccccccu).CopyTo(o, 0x3a);
            BitConverter.GetBytes((ushort) serial).CopyTo(o, 0x42);
            if (online) o[0x44] = 1;            // lugar: 1 = na cidade
            BitConverter.GetBytes(joined).CopyTo(o, 0x4c);
            return o;
        }

        public static void SendMembers(RiceClient c, CrewData t, int act, int channel)
        {
            var entries = t.Members.Select(m => MemberEntry(m, t, c)).Where(e => e != null).ToList();
            int pages = Math.Max(1, (entries.Count + 19) / 20);
            for (int k = 0; k < pages; k++)
            {
                var part = entries.Skip(k * 20).Take(20).ToList();
                int kk = k;
                Send(c, 856, w =>
                {
                    w.Write(act); w.Write(t.Tid); w.Write(channel); w.Write(t.Master);
                    w.Write((ushort) part.Count); w.Write((ushort) kk);
                    foreach (var e in part) w.Write(e);
                });
            }
            Log.WriteLine("Crew '{0}': lista de {1} membro(s) enviada.", t.Name, entries.Count);
        }

        [RicePacket(855, RiceServer.ServerType.Game)]
        public static void TeamMembers(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 16) return;
            int act = BitConverter.ToInt32(p, 0);
            long tid = BitConverter.ToInt64(p, 4);
            int channel = BitConverter.ToInt32(p, 12);
            var t = ById(tid);
            Log.WriteLine("TeamMembers act {0:x} crew {1} canal {2}: {3}", act, tid, channel, t != null ? t.Name : "nao existe");
            if (t != null) SendMembers(packet.Sender, t, act, channel);
        }

        // 858 CharTeamInfo (u32 act, i64 cid) -> 859 (act, cid, info, i32 rank): a crew de outro jogador / bot.
        [RicePacket(858, RiceServer.ServerType.Game)]
        public static void CharTeamInfo(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 12) return;
            uint act = BitConverter.ToUInt32(p, 0);
            long cid = BitConverter.ToInt64(p, 4);
            var t = Of(cid);
            Send(packet.Sender, 859, w =>
            {
                w.Write(act); w.Write(cid); w.Write(Info(t));
                w.Write(t == null ? -1 : (t.Master == cid ? 1 : 3));
            });
        }

        [RicePacket(870, RiceServer.ServerType.Game)]
        public static void JoinTeamApplyByName(RicePacket packet)
        {
            var p = packet.Buffer;
            string name = ReadWStr(p, 0, Math.Min(26, p.Length));
            Log.WriteLine("JoinTeamApplyByName '{0}' ({1} bytes)", name, p.Length);
            Apply(packet.Sender, ByName(name), name);
        }

        [RicePacket(871, RiceServer.ServerType.Game)]
        public static void JoinTeamApply(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 8) return;
            long tid = BitConverter.ToInt64(p, 0);
            Log.WriteLine("JoinTeamApply crew {0} ({1} bytes): {2}", tid, p.Length, BitConverter.ToString(p));
            Apply(packet.Sender, ById(tid), tid.ToString());
        }

        // o bot do pedido (Join Crew clicando nele): cid (i64) ou nome (UTF-16) em algum lugar do pacote
        static Bots.BotProfile BotIn(byte[] p)
        {
            for (int o = 0; o + 8 <= p.Length; o += 2)
            {
                long v = BitConverter.ToInt64(p, o);
                if (v <= 0 || ById(v) != null) continue;
                var b = Bots.ByCid(v);
                if (b != null && !b.IsAlt) return b;
            }
            for (int st = 0; st < 2; st++)
            {
                var parts = Encoding.Unicode.GetString(p, st, (p.Length - st) & ~1).Split('\0');
                foreach (var s in parts)
                {
                    string n = s.Trim();
                    if (n.Length < 2) continue;
                    var b = Bots.ByName(n);
                    if (b != null && !b.IsAlt) return b;
                }
            }
            return null;
        }

        // (desligado: o Join Crew nao trazia o bot no pacote) Voce e o lider da sua crew e aperta "Join Crew" num bot: vira um convite para a SUA crew. Bot sem crew aceita na
        // hora (sem precisar de amizade); bot de outra crew recusa. true = era convite (nao segue como pedido de entrada).
        static bool InviteBot(RiceClient c, byte[] p)
        {
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null) return false;
            var mine = Of((long) ch.CID);
            if (mine == null || mine.Master != (long) ch.CID) return false;
            var b = BotIn(p);
            if (b == null) return false;
            var cur = Of(b.Cid);
            if (cur != null && cur.Tid == mine.Tid) { Chat.SendSystemMessage(c, b.Name + " ja esta na sua crew."); return true; }
            if (cur != null) { Bots.SayLater(c, 800, "channel", b.Name, "Valeu o convite, " + ch.Name + ", mas eu ja sou da " + cur.Name + "."); return true; }
            if (b.Special || Bots.IsAce(b) || b.MostWanted) { Bots.SayLater(c, 800, "channel", b.Name, "Valeu, " + ch.Name + ", mas eu corro sozinho."); return true; }
            if (RiceServer.Config.CrewLadder && b.Level > ch.Level)
            {
                Bots.SayLater(c, 800, "channel", b.Name, "Valeu o convite, " + ch.Name + ", mas eu sou nivel " + b.Level + ". Sobe mais um pouco que eu penso.");
                return true;
            }
            string noSlot;
            if (!CrewLadder.CanRecruit(ch, out noSlot)) { Chat.SendSystemMessage(c, noSlot); return true; }
            JoinBot(c, ch, mine, b, "convite");
            return true;
        }

        // bot entra na crew do jogador (convite pelo Join Crew ou o bot pediu para entrar e voce aceitou)
        public static void JoinBot(RiceClient c, Rice.Game.Character ch, CrewData mine, Bots.BotProfile b, string why)
        {
            CrewLadder.NoteRecruit(ch, b.Cid);
            AddMember(mine, b.Cid);
            b.CrewTid = mine.Tid; b.CrewName = mine.Name;
            Bots.Save();
            Chat.SendSystemMessage(c, b.Name + " (nivel " + b.Level + ") entrou na sua crew (" + mine.Name + ").");
            Bots.SayLater(c, 1200, "team", b.Name, "Valeu, " + ch.Name + "! To na " + mine.Name + " agora.");
            Log.WriteLine("Crew: {0} entrou na {1} ({2} de {3}).", b.Name, mine.Name, why, ch.Name);
        }

        public static void Apply(RiceClient c, CrewData t, string asked)
        {
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null) return;
            string refuse = null;
            if (t == null) refuse = "A crew '" + asked + "' nao existe. Digite !crews no chat para ver as crews de bots.";
            else if (Of((long) ch.CID) != null) refuse = "Voce ja esta numa crew. Saia dela antes.";
            else if (!t.IsBot) refuse = "Essa crew nao tem lider online para aprovar o pedido.";
            else if (ch.Level < t.MinLevel) refuse = "A crew " + t.Name + " so aceita nivel " + t.MinLevel + " ou mais.";
            else if (t.Members.Count >= 100) refuse = "A crew " + t.Name + " esta cheia.";
            if (refuse != null)
            {
                Log.WriteLine("Pedido de entrada recusado: {0}", refuse);
                Bots.Popup(c, refuse);
                return;
            }
            AddMember(t, (long) ch.CID);
            ch.TID = t.Tid;
            Log.WriteLine("{0} entrou na crew de bots '{1}'.", ch.Name, t.Name);
            Send(c, 847, w => { w.Write(1); w.Write(Info(t)); w.Write(visualAge++); });
            Chat.SendSystemMessage(c, "O lider " + t.MasterName + " aprovou! Voce agora faz parte da crew '" + t.Name + "'.");
            Bots.WelcomeToCrew(c, t, ch.Name);
        }

        // ---- fechar (apagar) a crew --------------------------------------------------------------------------
        // No jogo original o lider pede o fechamento e a crew some em 7 dias; offline ela e apagada na hora.
        // 844 CloseTeam (i64 crew) -> 845 (i32 1, info com estado 'C'), aviso 877 tipo 3 (apagada), 841 sem crew.

        static void Notify(RiceClient c, uint kind, string crewName, string name)
        {
            Send(c, 877, w =>
            {
                w.Write(kind); w.Write(WStr(crewName, 26)); w.Write(WStr(name, 42));
                w.Write((uint) DateTimeOffset.UtcNow.ToUnixTimeSeconds()); w.Write(0);
            });
        }

        static void Delete(CrewData t)
        {
            lock (Sync)
            {
                All().Remove(t);
                Save();
            }
            foreach (var b in t.Members.Select(Bots.ByCid).Where(b => b != null))
            {
                b.CrewTid = 0; b.CrewName = "";
            }
            Bots.Save();
        }

        [RicePacket(844, RiceServer.ServerType.Game)]
        public static void CloseTeam(RicePacket packet)
        {
            var c = packet.Sender;
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null) return;
            var t = Of((long) ch.CID);
            Log.WriteLine("CloseTeam: {0}", t != null ? t.Name : "sem crew");
            if (t == null) return;
            if (t.Master != (long) ch.CID)
            {
                Bots.Popup(c, "So o lider pode fechar a crew. Para sair, use Sair (ou !sair crew no chat).");
                return;
            }
            var info = Info(t);
            info[0xe9] = (byte) 'C';
            Delete(t);
            ch.TID = 0;
            Send(c, 845, w => { w.Write(1); w.Write(info); });
            Notify(c, 3, t.Name, ch.Name);
            SendMyInfo(c, 0);
            Chat.SendSystemMessage(c, "Crew '" + t.Name + "' apagada.");
            Log.WriteLine("Crew '{0}' apagada pelo lider {1}.", t.Name, ch.Name);
        }

        // 853 CancelCloseTeam: offline a crew ja foi apagada; so responde.
        [RicePacket(853, RiceServer.ServerType.Game)]
        public static void CancelCloseTeam(RicePacket packet)
        {
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            var t = ch != null ? Of((long) ch.CID) : null;
            Send(packet.Sender, 854, w => { w.Write(t != null ? 1 : 0); w.Write(Info(t)); });
        }

        // Comando de chat (!sair crew): membro sai, lider apaga.
        public static string LeaveOrClose(RiceClient c)
        {
            var ch = c.Player.ActiveCharacter;
            var t = Of((long) ch.CID);
            if (t == null) return "Voce nao esta em nenhuma crew.";
            if (t.Master == (long) ch.CID)
            {
                Delete(t);
                ch.TID = 0;
                Notify(c, 3, t.Name, ch.Name);
                SendMyInfo(c, 0);
                return "Crew '" + t.Name + "' apagada. Troque de area/canal se o nome ainda aparecer.";
            }
            lock (Sync)
            {
                t.Members.Remove((long) ch.CID);
                Save();
            }
            ch.TID = 0;
            Send(c, 849, w => { w.Write(1); w.Write(visualAge++); });
            SendMyInfo(c, 0);
            return "Voce saiu da crew '" + t.Name + "'.";
        }

        // Janela de Channel Battle (batalha de crews por canal): offline nao ha canais em disputa; lista vazia
        // para a janela abrir. 860 CBattleInfo (u32 act) -> 861 (act, proxima batalha, pagina 0, n 0).
        [RicePacket(860, RiceServer.ServerType.Game)]
        public static void CBattleInfo(RicePacket packet)
        {
            var p = packet.Buffer;
            uint act = p.Length >= 4 ? BitConverter.ToUInt32(p, 0) : 0;
            Send(packet.Sender, 861, w => { w.Write(act); w.Write(0u); w.Write(0); w.Write(0); });
        }

        // 862 GetCBattleTime (u32 act) -> 863 (act, hora da proxima batalha = 0)
        [RicePacket(862, RiceServer.ServerType.Game)]
        public static void GetCBattleTime(RicePacket packet)
        {
            var p = packet.Buffer;
            uint act = p.Length >= 4 ? BitConverter.ToUInt32(p, 0) : 0;
            Send(packet.Sender, 863, w => { w.Write(act); w.Write(0); });
        }

        // ---- emblema (foto) da crew -----------------------------------------------------------------------------
        // Os 10 emblemas do jogo (Data_US/gameui/teammark/01..10, BMP 32x32) vao embutidos no Rice.exe.
        // 64 GetImage (i32 act, i64 crew, i64 emblema; act 0x3f5 = emblema de crew) -> 65 (act, resultado, emblema,
        //    crew, chave, attr, paginas-1, tamanho da ultima, tamanho total, dados); paginas de 4000 em 68.
        // 832 GetTeamMark (i64 crew) -> 833 (i32 1, crew, i64 emblema, i32 tamanho, BMP).
        const int CrewMarkAct = 0x3f5, ImagePage = 4000;
        static readonly Dictionary<long, byte[]> markCache = new Dictionary<long, byte[]>();

        public static byte[] MarkData(long mark)
        {
            if (mark < 1 || mark > 10) return null;
            lock (markCache)
            {
                byte[] d;
                if (markCache.TryGetValue(mark, out d)) return d;
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var st = asm.GetManifestResourceStream("teammark." + mark.ToString("00")))
                    {
                        if (st != null)
                        {
                            var ms = new MemoryStream();
                            st.CopyTo(ms);
                            d = ms.ToArray();
                        }
                    }
                    if (d == null && File.Exists(Path.Combine("res", "teammark", mark.ToString("00"))))
                        d = File.ReadAllBytes(Path.Combine("res", "teammark", mark.ToString("00")));
                }
                catch (Exception ex) { Log.WriteError("Emblema " + mark + ": " + ex.Message); }
                markCache[mark] = d;
                return d;
            }
        }

        [RicePacket(64, RiceServer.ServerType.Lobby | RiceServer.ServerType.Game | RiceServer.ServerType.Area | RiceServer.ServerType.Ranking)]
        public static void GetImage(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 20) return;
            int act = BitConverter.ToInt32(p, 0);
            long obj = BitConverter.ToInt64(p, 4), image = BitConverter.ToInt64(p, 12);
            var data = act == CrewMarkAct ? MarkData(image) : null;
            Log.WriteLine("GetImage act {0:x} objeto {1} imagem {2}: {3}", act, obj, image, data != null ? data.Length + " bytes" : "nao tenho");
            if (data == null)
            {
                Send(packet.Sender, 65, w =>
                {
                    w.Write(act); w.Write(act == CrewMarkAct ? 0 : -1); w.Write(image); w.Write(obj);
                    w.Write(0u); w.Write(0u); w.Write(0); w.Write(0); w.Write(0);
                });
                return;
            }
            int pages = Math.Max(1, (data.Length + ImagePage - 1) / ImagePage);
            for (int k = 0; k < pages - 1; k++)
            {
                int kk = k;
                Send(packet.Sender, 68, w =>
                {
                    w.Write(act); w.Write(kk); w.Write(ImagePage);
                    w.Write(data, kk * ImagePage, ImagePage);
                });
            }
            int lastLen = data.Length - (pages - 1) * ImagePage;
            Send(packet.Sender, 65, w =>
            {
                w.Write(act); w.Write(1); w.Write(image); w.Write(obj);
                w.Write(0u); w.Write(0u); w.Write(pages - 1); w.Write(lastLen); w.Write(data.Length);
                w.Write(data, (pages - 1) * ImagePage, lastLen);
            });
        }

        [RicePacket(832, RiceServer.ServerType.Game | RiceServer.ServerType.Lobby)]
        public static void GetTeamMark(RicePacket packet)
        {
            var p = packet.Buffer;
            long tid = p.Length >= 8 ? BitConverter.ToInt64(p, 0) : 0;
            var t = ById(tid);
            var data = t != null ? MarkData(t.Mark) : null;
            Log.WriteLine("GetTeamMark crew {0}: emblema {1}", tid, t != null ? t.Mark : 0);
            Send(packet.Sender, 833, w =>
            {
                w.Write(data != null ? 1 : 0); w.Write(tid); w.Write(t != null ? t.Mark : 0L);
                w.Write(data != null ? data.Length : 0);
                if (data != null) w.Write(data);
            });
        }
    }
}
