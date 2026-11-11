using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Battle Zone offline: salas, corridas e corredores IA (bots do proprio jogo: "[AI] Elio" etc.).
    // Layouts do cliente 2016 conforme a documentacao do emulador openDC (github.com/Xerender13/openDC),
    // implementados aqui do zero. Fluxo:
    //   460 RoomCreate -> 622 RoomMaster, 446 RoomState, 461 RoomCreateAck
    //   621 RoomModify (assento = 0xffff com o botao IA -> entra um corredor IA: 468 RoomJoin)
    //   486 PvpStartReq -> 487 PvpLoad (+ 625 AiCarAssign) ; 489 PvpLoadComplete -> 490 PvpGo
    //   493 PvpGate -> 494 ; 491 PvpEnd -> 491 + 495 PvpFinish ; fim -> 622 + 492 PvpResult
    public static class Battle
    {
        const int Seats = 32;
        const uint Open = 0, Locked = 0xffffffff;
        const int Wait = 0, Loading = 1, Racing = 2;
        const int Finished = 1, Retired = 2;
        const int Practice = 100;
        const int UserInfoSize = 16, CarAttrSize = 16, PlayerInfoSize = 0xd8;
        const ushort Age = 123;

        class Member
        {
            public RiceClient Client;           // null = IA
            public Rice.Game.Character Char;
            public uint BaseSerial;             // humano: serial; IA: key << 16
            public byte[] UserInfo, CarAttr, PlayerInfo;
            public bool Ready, Loaded;
            public int EndType, EndTime, Rank;
            public float Record;
            public bool Ended;
            public bool LateEnd;                // IA ainda correndo quando o primeiro chegou (o jogo da "desistiu" para ela)
            // IA
            public int Key, Driver, AiIdx, CarType, Level;
            public uint Color = 0x08000002u, Tint = 0x82ccccccu;   // IA: cor do carro (do bot) e do vidro
            public bool Assigned;
            public string Name;
            // bot que aparece na sala como jogador (nome, nivel, carro) e vira IA so na corrida
            public bool AsPlayer; public uint LobbySerial;
            public DateTime PatienceUntil = DateTime.MaxValue;   // bot que entrou sozinho: sai da sala se demorar para largar

            public bool IsAi { get { return Client == null; } }
            public uint Serial { get { return IsAi && AsPlayer ? LobbySerial : IsAi && Assigned ? (uint) (Key << 16 | (Driver & 0xffff)) : BaseSerial; } }

            public byte[] Unit()
            {
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write(Serial);
                w.Write(Age);
                w.Write(Sized(UserInfo, UserInfoSize));
                w.Write(IsAi ? true : Ready);
                w.Write(Sized(CarAttr, CarAttrSize));
                w.Write(WStr(Name, 32));
                return ms.ToArray();
            }
        }

        class Room
        {
            public int Id;
            public uint Channel, Type, MapFlag;
            public ushort Map, Capacity;
            public string Name;
            public int State = Wait;
            public uint Master;
            public uint[] SeatList = new uint[Seats];
            public List<Member> Members = new List<Member>();
            public List<uint> Racers = new List<uint>();
            public HashSet<uint> Waiting = new HashSet<uint>();
            public int RankCounter, StartTime, AiKeys;
            public int? FirstEnd;
            public HashSet<int> CardsTaken = new HashSet<int>();      // cartas "?" ja abertas nesta corrida
            public HashSet<uint> CardPicked = new HashSet<uint>();    // quem ja pegou carta
            public Timer Timer;
            public string ChallengeHost;      // sala de desafio de um bot (BotChatter)
            public long DuelBot;              // mano a mano da escada de crews (CrewLadder): o bot que voce enfrenta
            public long DuelMate;             // corrida em dupla: o bot da sua crew que corre do seu lado
            public bool ChallengeBonus;       // premio extra por aceitar o desafio (uma vez)
            public DateTime NextBotJoin = DateTime.UtcNow.AddSeconds(8);
            public DateTime ResultAt = DateTime.MinValue;                                     // fim da ultima corrida
            public bool BackInRoom = true;                                                    // voce ja voltou para a tela da sala                      // bots entrando sozinhos na sua sala
            public HashSet<string> Kicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // tirados por voce: nao voltam
            public List<long> Spectators = new List<long>();          // bots assistindo (sala cheia): entram quando abrir vaga
            public int SpecMax = -1;
            public HashSet<long> JustWatch = new HashSet<long>();     // espectadores que so vieram assistir (nao pegam vaga)
            public DateTime NextWatcher = DateTime.UtcNow.AddSeconds(5 + Rng.Next(20));
            public bool SpecPending;                                  // um espectador ja vai entrar (2 a 10 s depois da vaga abrir)                                  // quantos espectadores essa sala aceita (sorteado: 1 ate BattleSpectatorsMax)
            public bool AfterRace;                                    // corrida acabou: alguns bots saem quando voce volta
            public Dictionary<uint, int> Gates = new Dictionary<uint, int>();   // portoes de cada carro na corrida (narracao)
            public List<uint> Order = new List<uint>();               // ordem pelos portoes (1o primeiro)
            public DateTime NextTalk = DateTime.MinValue;             // proxima fala dos espectadores
            public int Area { get { return 900 + Id; } }
            public IEnumerable<Member> Humans { get { return Members.Where(m => !m.IsAi); } }
            public IEnumerable<Member> Ais { get { return Members.Where(m => m.IsAi); } }
        }

        static readonly object Sync = new object();
        static readonly Dictionary<int, Room> Rooms = new Dictionary<int, Room>();
        static readonly Dictionary<RiceClient, int> RoomOf = new Dictionary<RiceClient, int>();
        static int nextId = 1;
        static readonly Random Rng = new Random();

        // ------------------------------------------------------------------ utilidades

        static byte[] Sized(byte[] b, int size)
        {
            var r = new byte[size];
            if (b != null) Array.Copy(b, r, Math.Min(b.Length, size));
            return r;
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
            return Encoding.Unicode.GetString(b, at, bytes).Split('\0')[0];
        }

        static byte[] Slice(byte[] b, int at, int len)
        {
            var r = new byte[len];
            if (at < b.Length) Array.Copy(b, at, r, 0, Math.Min(len, b.Length - at));
            return r;
        }

        static uint SerialOf(RiceClient c)
        {
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            return ch != null ? ch.Serial : 0u;
        }

        static void Send(RiceClient c, ushort id, byte[] payload)
        {
            var p = new RicePacket(id);
            p.Writer.Write(payload);
            c.Send(p);
        }

        static void Cast(Room room, ushort id, byte[] payload)
        {
            foreach (var m in room.Humans.ToList())
            {
                try { Send(m.Client, id, payload); }
                catch (Exception ex) { Log.WriteError("Battle cast: " + ex.Message); }
            }
        }

        static byte[] Build(Action<BinaryWriter> f)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            f(w);
            return ms.ToArray();
        }

        static byte[] SeatBytes(Room r)
        {
            return Build(w => { foreach (var s in r.SeatList) w.Write(s); });
        }

        static byte[] MasterFrame(Room r)
        {
            return Build(w => { w.Write((uint) r.Id); w.Write(r.Master); });
        }

        static byte[] StateFrame(Room r)
        {
            return Build(w =>
            {
                w.Write(1); w.Write((uint) r.Id); w.Write((uint) r.Id);
                w.Write(r.Map); w.Write((ushort) (r.MapFlag & 0xffff)); w.Write(r.Type);
                w.Write((ushort) r.State); w.Write((short) (r.State != Wait ? r.Area : -1));
                // @0x1e do pacote (DriftCity.exe 0x5431e9): quantos estao assistindo ("Spectators: N")
                w.Write(r.Capacity); w.Write((ushort) 0); w.Write((ushort) Math.Min(r.Spectators.Count, 999));
                w.Write(WStr(r.Name, 80));
                w.Write(SeatBytes(r));
                w.Write((uint) r.Members.Count);
                foreach (var m in r.Members) w.Write(m.Unit());
            });
        }

        static byte[] ModifyFrame(Room r)
        {
            return Build(w => { w.Write((uint) r.Id); w.Write(r.Map); w.Write(r.MapFlag); w.Write(r.Capacity); w.Write(SeatBytes(r)); });
        }

        static byte[] ReadyFrame(Member m)
        {
            return Build(w => { w.Write(m.Serial); w.Write(m.IsAi || m.Ready); });
        }

        static byte[] JoinFrame(Room r, Member m)
        {
            return Build(w =>
            {
                w.Write(m.Serial); w.Write(Age);
                w.Write(Sized(m.UserInfo, UserInfoSize)); w.Write(Sized(m.CarAttr, CarAttrSize));
                w.Write((uint) r.Id); w.Write((uint) r.Id);
                w.Write(Sized(m.PlayerInfo, PlayerInfoSize));
                w.Write(SeatBytes(r)); w.Write(r.Type);
            });
        }

        static byte[] LeaveFrame(Room r, uint serial)
        {
            return Build(w => { w.Write(serial); w.Write(SeatBytes(r)); w.Write(r.Type); });
        }

        static Room RoomOfClient(RiceClient c)
        {
            int id;
            Room r;
            return RoomOf.TryGetValue(c, out id) && Rooms.TryGetValue(id, out r) ? r : null;
        }

        static Member MemberOf(Room r, RiceClient c)
        {
            return r.Members.FirstOrDefault(m => m.Client == c);
        }

        // ------------------------------------------------------------------ desafio dos bots
        // Um bot chama para corrida no chat (BotChatter); outros aceitam. A sala dele aparece na lista da Battle Zone
        // com 1 lugar livre. Voce entra (468 RoomJoin / 443 / 445), os bots entram como IA, e 5 s depois o dono fala
        // que pode comecar e a corrida comeca sozinha (o seu jogo pilota as IAs, entao voce fica de mestre da sala).
        public class Challenge
        {
            public int Id; public long Host; public string HostName = ""; public List<long> Racers = new List<long>();
            public DateTime Until; public int Created;
            public string Title; public bool Duel; public long Mate;
            public int Arena;      // 0 = Beginner; 1..4 = Pro EXP x4 / x6 / x8 / x16
        }

        // ---- regras das arenas da Battle Zone (como o jogo mostra ao criar a sala) ----------------------------------
        // Beginner (canal 11): nivel 1-30, carro V1-V4. Pro (canal 12), pelo EXP Reward da sala (bits do tipo da sala,
        // DriftCity.exe 0x5cdaa0): x4 (0x20000) nivel 11+ V1-V4, x6 (0x40000) 31+ V1-V6, x8 (0x80000) 51+ V1-V8,
        // x16 (0x100000) 71+ V1-V9. Mission (canal 13) fica de fora dos desafios dos bots.
        public const uint BeginnerChannel = 11, ProChannel = 12;
        static readonly int[] ArenaMinLv = { 1, 11, 31, 51, 71 }, ArenaMaxLv = { 30, 999, 999, 999, 999 }, ArenaMaxV = { 4, 4, 6, 8, 9 };
        static readonly uint[] ArenaBits = { 0, 0x20000, 0x40000, 0x80000, 0x100000 };
        static readonly string[] ArenaNames = { "Beginner", "Pro x4", "Pro x6", "Pro x8", "Pro x16" };
        public static bool FitsArena(int arena, int level, int grade)
        {
            if (arena < 0 || arena > 4) return true;
            return level >= ArenaMinLv[arena] && level <= ArenaMaxLv[arena] && grade >= 1 && grade <= ArenaMaxV[arena];
        }
        public static bool BotFits(int arena, Bots.BotProfile b) { return b != null && FitsArena(arena, b.Level, BotSocial.GradeOf(b)); }
        // arena de uma sala do jogador (canal + tipo); -1 = sem regra (Mission, evento...)
        static int ArenaOfRoom(uint channel, uint type)
        {
            if (channel == BeginnerChannel) return 0;
            if (channel != ProChannel) return -1;
            for (int a = 4; a >= 1; a--) if ((type & ArenaBits[a]) != 0) return a;
            return 1;
        }
        static uint ChannelOf(int arena) { return arena == 0 ? BeginnerChannel : ProChannel; }
        static uint TypeOf(int arena) { return 0x80u | (arena >= 0 && arena <= 4 ? ArenaBits[arena] : 0); }
        // em que arena o bot abre a sala: uma das que ele cabe (nivel e V do carro); -1 = nenhuma
        public static int ArenaFor(Bots.BotProfile b)
        {
            if (b == null) return -1;
            var ok = Enumerable.Range(0, 5).Where(a => BotFits(a, b)).ToList();
            return ok.Count == 0 ? -1 : ok[Rng.Next(ok.Count)];
        }
        // mano a mano da escada: sempre abre (Pro pelo V do carro do bot, Beginner se ele couber)
        static int DuelArena(Bots.BotProfile b)
        {
            if (BotFits(0, b)) return 0;
            int g = BotSocial.GradeOf(b);
            for (int a = 1; a <= 4; a++) if (g <= ArenaMaxV[a]) return a;
            return 4;
        }
        static string ArenaRule(int a)
        {
            return ArenaNames[a] + ": nivel " + (a == 0 ? "1-30" : ArenaMinLv[a] + "+") + " e carro V1-V" + ArenaMaxV[a];
        }
        static readonly List<Challenge> challenges = new List<Challenge>();
        static int nextChallenge = 5000;

        // ---- bot correndo na Battle Zone: some da cidade (o da party para de te seguir) ate a corrida acabar,
        // mesmo se voce sair da sala; depois volta e avisa no chat da party.
        static readonly Dictionary<long, DateTime> racingUntil = new Dictionary<long, DateTime>();
        static readonly Dictionary<long, string> racingNote = new Dictionary<long, string>();

        public static void MarkRacing(Bots.BotProfile b, double seconds)
        {
            if (b == null || b.IsAlt) return;
            lock (racingUntil) { racingUntil[b.Cid] = DateTime.UtcNow.AddSeconds(Math.Max(1, seconds)); racingNote.Remove(b.Cid); }
        }

        static void EndRacing(Bots.BotProfile b, string note)
        {
            if (b == null) return;
            lock (racingUntil)
            {
                if (!racingUntil.ContainsKey(b.Cid)) return;
                racingUntil[b.Cid] = DateTime.UtcNow;
                if (note != null) racingNote[b.Cid] = note; else racingNote.Remove(b.Cid);
            }
        }

        // recado de volta (a corrida acabou com voce na sala; ele volta quando voce sair)
        static void RaceNote(Bots.BotProfile b, string note)
        {
            if (b == null) return;
            lock (racingUntil) if (racingUntil.ContainsKey(b.Cid)) racingNote[b.Cid] = note;
        }

        // bot da party terminou uma corrida sem voce: conta no chat da party o que ganhou e voce (lider) leva
        // PartyBotRaceLeaderShare (2/3) do que ele ganhou. Depois ele volta dirigindo ate voce.
        static void PartyRaceResult(Bots.BotProfile b, int place, int count, long exp, long mito)
        {
            if (b == null) { return; }
            RiceClient oc = null; Rice.Game.Character och = null;
            foreach (var pl in RiceServer.GetPlayers())
                if (pl != null && pl.ActiveCharacter != null && pl.GameClient != null && BotSocial.PartyBots(pl.ActiveCharacter).Contains(b))
                { oc = pl.GameClient; och = pl.ActiveCharacter; break; }
            if (och == null) { EndRacing(b, null); return; }
            double share = Math.Max(0, Math.Min(1, RiceServer.Config.PartyBotRaceLeaderShare));
            long le = (long) Math.Round(exp * share), lm = (long) Math.Round(mito * share);
            string text = "Corrida na Battle Zone: cheguei em " + place + "o de " + count + "!"
                        + (exp > 0 || mito > 0 ? " Ganhei +" + exp + " exp e +" + mito.ToString("N0") + " mito." : " Nao ganhei nada dessa vez.");
            Bots.SayLater(oc, 500, "party", b.Name, text);
            if (le > 0 || lm > 0)
            {
                if (le > 0) och.GrantExperience(le);
                if (lm > 0)
                {
                    och.GrantMito((ulong) lm);
                    var mm = new RicePacket(138);          // mostra o mito ganho (como a recompensa das licencas)
                    mm.Writer.Write(1); mm.Writer.Write(1); mm.Writer.Write(lm); mm.Writer.Write(och.Mito); mm.Writer.Write((ushort) 0);
                    try { oc.Send(mm); } catch (Exception) { }
                }
                try { och.FlushModInfo(oc); och.FlushNotices(oc); } catch (Exception) { }
                var oc2 = oc;
                System.Threading.Tasks.Task.Delay(1200).ContinueWith(_ =>
                {
                    try { Chat.SendSystemMessage(oc2, "Voce (lider da party) ganhou " + (int) Math.Round(share * 100) + "% da corrida do " + b.Name + ": +" + le + " exp e +" + lm.ToString("N0") + " mito."); }
                    catch (Exception) { }
                });
                Log.WriteLine("Corrida do bot {0} (party de {1}): {2}o de {3}, lider +{4} exp +{5} mito.", b.Name, och.Name, place, count, le, lm);
            }
            EndRacing(b, "To voltando da Battle Zone, ja chego ai!");
        }

        // premio de uma corrida que o bot terminou sem voce (os 3 primeiros ganham 3x, 2x, 1x)
        static void SoloRaceReward(Bots.BotProfile b, int place, int count)
        {
            if (b == null || b.IsAlt) return;
            var cfg = RiceServer.Config;
            int pts = place >= 1 && place <= 3 ? 4 - place : 0;
            int grow = BotSocial.GrowthOf(b);
            long exp = 0, mito = 0;
            if (pts > 0 && grow > 0)
            {
                exp = (long) Math.Round((10 + b.Level) * pts * Math.Max(0, cfg.BotActivityScale) * (grow == 1 ? cfg.BotCrewExpScale : 1.0));
                mito = (long) Math.Round((150 + 40L * b.Level) * pts * Math.Max(0, cfg.BotActivityScale));
                if (b.Mito < 0) b.Mito = 0;
                b.Mito += mito;
                CrewLadder.LeaderShare(b.Cid, mito, "corrida");
                Bots.AddExp(b, exp);
                Licenses.Activity(b, cfg.BotLicenseActivity);
                Bots.Save();
            }
            PartyRaceResult(b, place, count, exp, mito);
        }

        static double RaceSeconds()
        {
            var cfg = RiceServer.Config;
            return cfg.BotRaceMinSeconds + Rng.NextDouble() * Math.Max(0, cfg.BotRaceMaxSeconds - cfg.BotRaceMinSeconds);
        }

        // true = o bot esta numa corrida da Battle Zone; quando acaba, avisa o dono da party (uma vez) e volta a seguir
        public static bool IsRacing(long cid)
        {
            string note;
            lock (racingUntil)
            {
                DateTime t;
                if (!racingUntil.TryGetValue(cid, out t)) return false;
                if (DateTime.UtcNow < t) return true;
                racingUntil.Remove(cid);
                racingNote.TryGetValue(cid, out note); racingNote.Remove(cid);
            }
            string msg = note ?? "Voltei da corrida na Battle Zone! To indo te encontrar.";
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var b = Bots.ByCid(cid);
                    if (b == null) return;
                    foreach (var pl in RiceServer.GetPlayers())
                        if (pl != null && pl.ActiveCharacter != null && pl.GameClient != null && BotSocial.PartyBots(pl.ActiveCharacter).Contains(b))
                            Bots.SayLater(pl.GameClient, 300, "party", b.Name, msg);
                }
                catch (Exception ex) { Log.WriteError("Bot voltou da corrida: " + ex.Message); }
            });
            return false;
        }

        public static Challenge OpenChallenge(Bots.BotProfile host, List<Bots.BotProfile> racers, double seconds, int arena = -1)
        {
            if (host == null) return null;
            if (arena < 0 || !BotFits(arena, host)) arena = ArenaFor(host);
            if (arena < 0) { Log.WriteLine("Desafio: {0} (nivel {1}, V{2}) nao cabe em nenhuma arena.", host.Name, host.Level, BotSocial.GradeOf(host)); return null; }
            var c = new Challenge { Id = nextChallenge++, Host = host.Cid, HostName = host.Name, Until = DateTime.UtcNow.AddSeconds(seconds),
                                    Created = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Arena = arena };
            c.Racers.Add(host.Cid);
            foreach (var r in racers) if (r != null && r != host && c.Racers.Count < 7 && BotFits(arena, r)) c.Racers.Add(r.Cid);
            racers = racers.Where(r => r != null && c.Racers.Contains(r.Cid)).ToList();
            lock (Sync) { Expire(); challenges.Add(c); }
            // quem vai correr dirige ate a Battle Zone e espera la ate a sala comecar
            CityBots.Errand(host, "battle zone", null, seconds + 30);
            foreach (var r in racers) if (r != null && r != host) CityBots.Errand(r, "battle zone", null, seconds + 30);
            // quem aceitou ja nao segue ninguem: espera a sala abrir e corre (BotRaceMaxSeconds no maximo)
            MarkRacing(host, seconds + RiceServer.Config.BotRaceMaxSeconds + 15);
            foreach (var r in racers) if (r != null && r != host) MarkRacing(r, seconds + RiceServer.Config.BotRaceMaxSeconds + 15);
            Log.WriteLine("Desafio {0}: sala de {1} com {2} bot(s) ({3}).", c.Id, host.Name, c.Racers.Count, ArenaRule(arena));
            return c;
        }

        // ninguem entrou na sala em BotRaceOpenSeconds (1 min): os bots correm sozinhos. Somem da porta da Battle Zone por
        // 2 a 5 min (a corrida) e depois voltam; cada um ganha exp/mito pela colocacao e o vencedor comemora.
        static readonly List<Challenge> toRun = new List<Challenge>();
        static void Expire()
        {
            foreach (var x in challenges.Where(x => DateTime.UtcNow > x.Until).ToList()) { challenges.Remove(x); toRun.Add(x); }
        }

        public static void ChallengeTick()
        {
            try { BotsJoinRooms(); } catch (Exception ex) { Log.WriteError("Bots entrando na sala: " + ex.Message); }
            List<Challenge> run;
            lock (Sync) { Expire(); run = toRun.ToList(); toRun.Clear(); }
            var cfg = RiceServer.Config;
            foreach (var c in run)
            {
                var racers = c.Racers.Select(Bots.ByCid).Where(b => b != null).ToList();
                if (racers.Count < 2) { foreach (var b in racers) EndRacing(b, null); continue; }
                double secs = cfg.BotRaceMinSeconds + Rng.NextDouble() * Math.Max(0, cfg.BotRaceMaxSeconds - cfg.BotRaceMinSeconds);
                foreach (var b in racers) { CityBots.Vanish(b, secs + 2); MarkRacing(b, secs + 2); }
                Log.WriteLine("Desafio {0}: ninguem entrou, os {1} bots correm sozinhos ({2:0} s).", c.Id, racers.Count, secs);
                var rc = racers;
                System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(secs)).ContinueWith(_ =>
                {
                    try
                    {
                        // colocacao: habilidade x (velocidade + aceleracao do carro) com sorte
                        // (mais sorte: o melhor carro tambem perde as vezes)
                        var order = rc.OrderByDescending(b => Math.Max(0.5, b.Skill) * (BotSocial.CarStats(b)[0] + BotSocial.CarStats(b)[1]) * (0.6 + Rng.NextDouble() * 0.8)).ToList();
                        for (int i = 0; i < order.Count; i++)
                        {
                            var b = order[i];
                            // so os 3 primeiros ganham (3x, 2x, 1x); quem perde nao ganha nada
                            int pts = i < 3 ? 3 - i : 0;
                            if (pts == 0)
                            {
                                PartyRaceResult(b, i + 1, order.Count, 0, 0);
                                if (i == order.Count - 1 && Rng.NextDouble() < 0.3) BotChatter.Tell(b, "Perdi a corrida... fica pra proxima.");
                                continue;
                            }
                            int grow = BotSocial.GrowthOf(b);
                            if (grow == 0) { PartyRaceResult(b, i + 1, order.Count, 0, 0); continue; }   // bot parado: corre, mas nao evolui
                            long exp = (long) Math.Round((10 + b.Level) * pts * Math.Max(0, cfg.BotActivityScale) * (grow == 1 ? cfg.BotCrewExpScale : 1.0));
                            long mito = (long) Math.Round((150 + 40L * b.Level) * pts * Math.Max(0, cfg.BotActivityScale));
                            if (b.Mito < 0) b.Mito = 0;
                            b.Mito += mito;
                            CrewLadder.LeaderShare(b.Cid, mito, "corrida");
                            if (Bots.AddExp(b, exp) > 0) BotChatter.Tell(b, "Subi pro nivel " + b.Level + "!");
                            Licenses.Activity(b, cfg.BotLicenseActivity);
                            PartyRaceResult(b, i + 1, order.Count, exp, mito);
                        }
                        Bots.Save();
                        BotChatter.Confirm(order[0], "Ganhei a corrida! " + string.Join(", ", order.Skip(1).Take(3).Select(x => x.Name)) + " vieram atras.");
                        Log.WriteLine("Desafio {0}: {1} venceu.", c.Id, order[0].Name);
                    }
                    catch (Exception ex) { Log.WriteError("Desafio sozinho: " + ex.Message); }
                });
            }
        }

        // Bots (online, fora de corrida, que cabem na arena da sala e tem linha na tabela de IA) entram sozinhos na sala
        // que voce criou, um de cada vez a cada BattleBotJoinSeconds; voce tira clicando no assento deles.
        static void BotsJoinRooms()
        {
            var cfg = RiceServer.Config;
            if (!cfg.BattleBotsJoinRoom || !cfg.BattleAiEnabled) return;
            var says = new List<Tuple<RiceClient, string, string>>();
            lock (Sync)
            {
                // depois do resultado, quando voce volta para a tela da sala (516), os bots voltam a aparecer como
                // jogador; se o jogo nao avisar, depois de 90 s
                if (cfg.BattleBotsAsPlayers)
                    foreach (var room in Rooms.Values.ToList())
                        if (room.State == Wait && (room.BackInRoom || (DateTime.UtcNow - room.ResultAt).TotalSeconds > 90))
                            RedressAll(room);
                foreach (var room in Rooms.Values.ToList())
                {
                    if (room.State != Wait || !room.Humans.Any()) continue;
                    var host0 = room.Humans.FirstOrDefault(m => m.Serial == room.Master) ?? room.Humans.First();
                    // salas de desafio (as que voce entra) e mano a mano: paciencia, saida depois da corrida e espectadores
                    // tambem; so nao entra bot novo no mano a mano
                    if (room.BackInRoom) PatienceAndSeats(room, host0, says);
                    if (DateTime.UtcNow >= room.NextWatcher)
                    {
                        room.NextWatcher = DateTime.UtcNow.AddSeconds(8 + Rng.Next(25));
                        AddSpectator(room, host0, says, true);   // alguns so vem assistir (Spectators: N no topo da sala)
                    }
                    if (room.DuelBot != 0 || room.DuelMate != 0) continue;
                    if (DateTime.UtcNow < room.NextBotJoin) continue;
                    double gap = Math.Max(2, cfg.BattleBotJoinSeconds);
                    room.NextBotJoin = DateTime.UtcNow.AddSeconds(2 + Rng.NextDouble() * (gap - 2));   // ate BattleBotJoinSeconds
                    var host = room.Humans.FirstOrDefault(m => m.Serial == room.Master) ?? room.Humans.First();
                    int batch = 1 + Rng.Next(Math.Max(1, cfg.BattleBotJoinMax));                        // 1 a 3 de uma vez
                    for (int k = 0; k < batch; k++)
                    {
                    int seat = -1;
                    for (int i = 0; i < room.Capacity && i < Seats; i++) if (room.SeatList[i] == Open) { seat = i; break; }
                    int arena = ArenaOfRoom(room.Channel, room.Type);
                    var inRoom = new HashSet<string>(room.Members.Select(m => m.Name ?? ""), StringComparer.OrdinalIgnoreCase);
                    var party = new HashSet<long>(BotSocial.PartyBots(host.Char).Select(x => x.Cid));
                    // sala cheia: alguns ficam assistindo (espectadores) e entram quando abrir uma vaga
                    if (seat < 0) { AddSpectator(room, host, says); break; }
                    // vaga aberta: quem estava assistindo entra primeiro
                    if (room.Spectators.Any(x => !room.JustWatch.Contains(x))) { ScheduleSpectator(room); break; }
                    var pool = Bots.All().Concat(RiceServer.Config.AltsRace ? Bots.AltsOf(host.Char) : new List<Bots.BotProfile>())
                                         .Where(b => b.AiIdx > 0 && !inRoom.Contains(b.Name) && !room.Kicked.Contains(b.Name) && !(b.IsAlt && b.InTutorial)
                                                     && !party.Contains(b.Cid) && !IsRacing(b.Cid) && BotSocial.Online(b) && BotFits(arena, b)).ToList();
                    if (pool.Count == 0) break;
                    var bb = pool[Rng.Next(pool.Count)];
                    if (!SeatBot(room, host, bb, seat)) break;
                    says.Add(Tuple.Create(host.Client, bb.Name, Pick("Opa, posso correr tambem?", "Entrei! Bora?", "Cheguei pra correr!", "Essa sala ta boa, to dentro.")));
                    }
                }
            }
            foreach (var t in says) Bots.SayLater(t.Item1, 800, "room", t.Item2, t.Item3);
        }
        static string Pick(params string[] s) { return s[Rng.Next(s.Length)]; }

        // ---- paciencia, saida depois da corrida e espectadores ----------------------------------------------------------
        static DateTime PatienceFrom(DateTime now)
        {
            double s = RiceServer.Config.BattleBotPatienceSeconds;
            return s <= 0 ? DateTime.MaxValue : now.AddSeconds(s * (0.6 + Rng.NextDouble()));
        }

        // poe um bot (que entrou sozinho) no assento; false = nao tem linha de IA / nao cabe
        static bool SeatBot(Room room, Member host, Bots.BotProfile bb, int seat)
        {
            var ai = NewAi(room, bb.Level, bb.Name, bb.Car, bb.AiIdx);
            if (ai == null) return false;
            ai.Driver = (int) (host.Serial & 0xffff);
            ai.PatienceUntil = PatienceFrom(DateTime.UtcNow);
            room.Members.Add(ai);
            room.SeatList[seat] = ai.Serial;
            MarkRacing(bb, 1800);
            Cast(room, 468, JoinFrame(room, ai));
            Cast(room, 485, ReadyFrame(ai));
            Cast(room, 621, ModifyFrame(room));
            Log.WriteLine("Sala {0}: {1} entrou sozinho no assento {2}.", room.Id, bb.Name, seat);
            return true;
        }

        static void AddSpectator(Room room, Member host, List<Tuple<RiceClient, string, string>> says, bool watchOnly = false)
        {
            var cfg = RiceServer.Config;
            if (room.SpecMax < 0) room.SpecMax = cfg.BattleSpectatorsMax <= 0 ? 0 : 1 + Rng.Next(cfg.BattleSpectatorsMax);
            if (!cfg.BattleSpectators || room.Spectators.Count >= room.SpecMax || Rng.NextDouble() > cfg.BattleSpectatorChance) return;
            int arena = ArenaOfRoom(room.Channel, room.Type);
            var inRoom = new HashSet<string>(room.Members.Select(m => m.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            var party = new HashSet<long>(BotSocial.PartyBots(host.Char).Select(x => x.Cid));
            var pool = Bots.All().Where(b => !b.IsAlt && b.AiIdx > 0 && !inRoom.Contains(b.Name) && !room.Kicked.Contains(b.Name) && !room.Spectators.Contains(b.Cid)
                                             && !party.Contains(b.Cid) && !IsRacing(b.Cid) && BotSocial.Online(b) && BotFits(arena, b)).ToList();
            if (pool.Count == 0) return;
            var sp = pool[Rng.Next(pool.Count)];
            room.Spectators.Add(sp.Cid);
            if (watchOnly) room.JustWatch.Add(sp.Cid);
            MarkRacing(sp, 1800);
            Log.WriteLine("Sala {0}: {1} esta assistindo{2}.", room.Id, sp.Name, watchOnly ? "" : " (sala cheia)");
            SendSpecCount(room);
            if (watchOnly)
            {
                if (Rng.NextDouble() < 0.5)
                    says.Add(Tuple.Create(host.Client, sp.Name, Pick("Vim so assistir, boa sorte a todos!", "Arquibancada garantida. Quero ver essa corrida!",
                        "To de olho, hein! Bora ver quem e o mais rapido.", "Pipoca na mao, so assistindo hoje.")));
                return;
            }
            says.Add(Tuple.Create(host.Client, sp.Name, Pick("Sala cheia... vou ficar assistindo. Abriu vaga, eu entro!",
                "To de espectador por enquanto. Alguem sai ai!", "Vou assistir essa, mas quero a proxima vaga!",
                "Lotou! Fico aqui na arquibancada esperando.")));
        }

        // vaga aberta: o primeiro espectador entra de 2 a 10 s depois (o tempo de sair da arquibancada)
        static void ScheduleSpectator(Room room)
        {
            if (room.SpecPending || !room.Spectators.Any(x => !room.JustWatch.Contains(x))) return;
            room.SpecPending = true;
            int ms = 2000 + Rng.Next(8001);
            int rid = room.Id;
            System.Threading.Tasks.Task.Delay(ms).ContinueWith(_ =>
            {
                var says = new List<Tuple<RiceClient, string, string>>();
                try
                {
                    lock (Sync)
                    {
                        Room r;
                        if (!Rooms.TryGetValue(rid, out r) || r != room) return;
                        r.SpecPending = false;
                        if (r.State != Wait || !r.Humans.Any()) return;
                        var host = r.Humans.FirstOrDefault(m => m.Serial == r.Master) ?? r.Humans.First();
                        int seat = -1;
                        for (int i = 0; i < r.Capacity && i < Seats; i++) if (r.SeatList[i] == Open) { seat = i; break; }
                        if (seat >= 0) SpectatorTakesSeat(r, host, seat, says);
                        if (seat >= 0) ScheduleSpectator(r);   // mais vagas: o proximo vem depois
                    }
                }
                catch (Exception ex) { Log.WriteError("Espectador entrando: " + ex.Message); room.SpecPending = false; }
                foreach (var t in says) Bots.SayLater(t.Item1, 300, "room", t.Item2, t.Item3);
            });
        }

        static bool SpectatorTakesSeat(Room room, Member host, int seat, List<Tuple<RiceClient, string, string>> says)
        {
            while (room.Spectators.Any(x => !room.JustWatch.Contains(x)))
            {
                long cid = room.Spectators.First(x => !room.JustWatch.Contains(x));
                room.Spectators.Remove(cid);
                var sp = Bots.ByCid(cid);
                if (sp == null) continue;
                if (!SeatBot(room, host, sp, seat)) { EndRacing(sp, null); continue; }
                SendSpecCount(room);
                says.Add(Tuple.Create(host.Client, sp.Name, Pick("Abriu vaga! Agora e a minha vez.", "Finalmente! Saindo da arquibancada.",
                    "Opa, vaga livre: to dentro!", "Cansei de assistir, agora eu corro!")));
                return true;
            }
            return false;
        }

        // bot sai da sala (paciencia ou fim da corrida); quem estava assistindo entra no lugar
        // 823 ObserverCount (DriftCity.exe 0x51d190): u16 = quantos estao assistindo ("Spectators: N" no topo da sala)
        static void SendSpecCount(Room room)
        {
            Cast(room, 823, BitConverter.GetBytes((ushort) Math.Min(room.Spectators.Count, ushort.MaxValue)));
            // o contador da sala vem do estado da sala (446): manda de novo com o numero novo
            if (room.State == Wait)
            {
                Cast(room, 622, MasterFrame(room));
                Cast(room, 446, StateFrame(room));
                foreach (var m in room.Members) Cast(room, 485, ReadyFrame(m));
            }
        }

        static void BotLeaves(Room room, Member host, Member ai, string line, List<Tuple<RiceClient, string, string>> says)
        {
            uint s = ai.Serial;
            int seat = Array.IndexOf(room.SeatList, s);
            room.Members.Remove(ai);
            ReplaceSeat(room, s, Open);
            Cast(room, 469, LeaveFrame(room, s));
            Cast(room, 621, ModifyFrame(room));
            var kb = Bots.ByName(ai.Name);
            if (kb != null) { EndRacing(kb, null); room.Kicked.Add(kb.Name); }   // nao volta nessa sala
            Log.WriteLine("Sala {0}: {1} saiu ({2}).", room.Id, ai.Name, line);
            says.Add(Tuple.Create(host.Client, ai.Name, line));
            room.NextBotJoin = DateTime.UtcNow.AddSeconds(3 + Rng.NextDouble() * Math.Max(2, RiceServer.Config.BattleBotJoinSeconds));
            if (seat >= 0 && seat < room.Capacity) ScheduleSpectator(room);
        }

        static void PatienceAndSeats(Room room, Member host, List<Tuple<RiceClient, string, string>> says)
        {
            var cfg = RiceServer.Config;
            var party = new HashSet<string>(BotSocial.PartyBots(host.Char).Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            // o dono do desafio (sala que voce entrou) nao sai da propria sala
            var mine = room.Ais.Where(a => a.Name != null && !party.Contains(a.Name) && Bots.ByName(a.Name) != null
                                           && !string.Equals(a.Name, room.ChallengeHost, StringComparison.OrdinalIgnoreCase)).ToList();
            var now = DateTime.UtcNow;
            if (room.AfterRace)
            {
                room.AfterRace = false;
                // fim da corrida: alguns vao embora (cada um com BattleBotLeaveAfterRaceChance)
                foreach (var ai in mine.Where(a => Rng.NextDouble() < cfg.BattleBotLeaveAfterRaceChance).ToList())
                    BotLeaves(room, host, ai, Pick("Boa corrida, galera! Vou nessa.", "Valeu pela corrida, vou dar uma volta na cidade.",
                        "Chega por hoje, ate a proxima!", "GG! Vou ali e ja volto... talvez."), says);
                foreach (var ai in room.Ais) if (ai.PatienceUntil != DateTime.MaxValue) ai.PatienceUntil = PatienceFrom(now);
                return;
            }
            // demorou para largar: um de cada vez perde a paciencia
            var bored = mine.Where(a => a.PatienceUntil <= now).OrderBy(a => a.PatienceUntil).FirstOrDefault();
            if (bored != null)
                BotLeaves(room, host, bored, Pick("Demorou demais pra largar... fui!", "Cansei de esperar, vou correr em outra sala.",
                    "Vai largar ou nao? To saindo.", "Zzz... acordo na proxima. Fui!"), says);
        }

        // ---- narracao dos espectadores (portoes de cada carro) ----------------------------------------------------------
        static string NameOf(Room room, uint key)
        {
            var m = room.Members.FirstOrDefault(x => (x.IsAi ? 0x80000000u | (uint) x.Key : x.Serial) == key);
            return m != null ? m.Name : null;
        }

        static void Narrate(Room room, uint key)
        {
            var cfg = RiceServer.Config;
            int g; room.Gates.TryGetValue(key, out g); room.Gates[key] = ++g;
            int before = room.Order.IndexOf(key);
            if (before < 0) { room.Order.Add(key); before = room.Order.Count - 1; }
            // sobe na ordem enquanto tiver mais portoes que o da frente
            int pos = before;
            while (pos > 0) { int f; room.Gates.TryGetValue(room.Order[pos - 1], out f); if (f >= g) break; pos--; }
            if (pos != before) { room.Order.RemoveAt(before); room.Order.Insert(pos, key); }
            if (!cfg.BattleSpectators || room.Spectators.Count == 0 || DateTime.UtcNow < room.NextTalk) return;
            var host = room.Humans.FirstOrDefault();
            var sp = Bots.ByCid(room.Spectators[Rng.Next(room.Spectators.Count)]);
            if (host == null || sp == null) return;
            string me = NameOf(room, key), text = null;
            if (pos < before && me != null)
            {
                string passed = NameOf(room, room.Order[pos + 1]);
                text = pos == 0 ? Pick("{0} assumiu a ponta! Que ultrapassagem linda!", "Olha o {0}! Passou o {1} e agora lidera!", "{0} pra cima do {1}... e passou! Primeiro lugar!")
                                : Pick("Que ultrapassagem linda do {0} em cima do {1}!", "{0} passou o {1}! Que manobra!", "Uau, o {0} deixou o {1} pra tras!", "{0} mergulhou por dentro e passou o {1}!");
                text = string.Format(text, me, passed ?? "rival");
            }
            else if (room.Order.Count >= 3 && Rng.NextDouble() < 0.35)
            {
                int lead; room.Gates.TryGetValue(room.Order[0], out lead);
                uint lastKey = room.Order[room.Order.Count - 1];
                int last; room.Gates.TryGetValue(lastKey, out last);
                string ln = NameOf(room, lastKey), first = NameOf(room, room.Order[0]);
                if (lead - last >= 4 && ln != null) text = string.Format(Pick("{0} ficou muito pra tras... acorda, piloto!", "Alguem avisa o {0} que a corrida ja comecou!", "{0} ta passeando, muito longe do pelotao!"), ln);
                else if (first != null) text = string.Format(Pick("{0} segue liderando!", "Corrida apertada! {0} na frente por enquanto.", "Que disputa! {0} ainda lidera."), first);
            }
            if (text == null) return;
            room.NextTalk = DateTime.UtcNow.AddSeconds(Math.Max(3, cfg.BattleNarratorSeconds) * (0.8 + Rng.NextDouble() * 0.6));
            Bots.SayLater(host.Client, 200, "room", sp.Name, text);
        }

        // mano a mano (escada de crews): sala com o nome do bot e da crew e so 2 lugares (ele e voce)
        public static Challenge OpenDuel(Bots.BotProfile host, string title, double seconds, Bots.BotProfile mate = null)
        {
            if (host == null) return null;
            var c = new Challenge { Id = nextChallenge++, Host = host.Cid, HostName = host.Name, Until = DateTime.UtcNow.AddSeconds(seconds),
                                    Created = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Title = title, Duel = true, Mate = mate != null ? mate.Cid : 0,
                                    Arena = DuelArena(host) };
            c.Racers.Add(host.Cid);
            if (mate != null && mate != host) c.Racers.Add(mate.Cid);
            lock (Sync) { Expire(); challenges.RemoveAll(x => x.Duel && x.Host == host.Cid); challenges.Add(c); }
            Log.WriteLine("Mano a mano {0}: sala '{1}'.", c.Id, title);
            return c;
        }

        // sala de mano a mano desse bot aberta ou com corrida em andamento
        public static bool DuelActive(long botCid)
        {
            lock (Sync) { Expire(); return challenges.Any(x => x.Duel && x.Host == botCid) || Rooms.Values.Any(r => r.DuelBot == botCid); }
        }

        public static bool ChallengeOpen(int id) { lock (Sync) return challenges.Any(x => x.Id == id && DateTime.UtcNow <= x.Until); }

        // entradas da lista de salas (XiPvpRoomInfo 0x72) com os desafios abertos
        public static List<byte[]> ChallengeEntries(uint channel)
        {
            var r = new List<byte[]>();
            lock (Sync)
            {
                Expire();
                LoadAi();
                foreach (var c in challenges)
                {
                    if (ChannelOf(c.Arena) != channel) continue;          // so na aba da arena dele (Beginner ou Pro)
                    uint type = TypeOf(c.Arena);
                    int cap = c.Racers.Count + 1;
                    ushort map = (ushort) (aiTracks.Count > 0 ? aiTracks.First() : 0);
                    r.Add(Build(w =>
                    {
                        w.Write((uint) c.Id); w.Write((ushort) 0); w.Write((ushort) 0); w.Write((uint) c.Created);
                        w.Write(map); w.Write((ushort) Wait); w.Write((short) -1); w.Write(type);
                        w.Write((ushort) c.Racers.Count); w.Write((ushort) cap); w.Write((ushort) 0); w.Write((ushort) 0);
                        w.Write(WStr(c.Title ?? ("Desafio de " + c.HostName), 60)); w.Write(-1L); w.Write(0L); w.Write(new byte[8]);
                    }));
                }
            }
            return r;
        }

        [RicePacket(468, RiceServer.ServerType.Game)]
        public static void RoomJoin(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("RoomJoin ({0} bytes)", p.Length);
            if (p.Length < 0x2e + PlayerInfoSize) return;
            JoinChallenge(packet.Sender, BitConverter.ToInt32(p, 0x26), Slice(p, 6, UserInfoSize), Slice(p, 0x16, CarAttrSize), Slice(p, 0x2e, PlayerInfoSize));
        }

        [RicePacket(443, RiceServer.ServerType.Game)]
        public static void RoomDirectJoin(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("RoomDirectJoin ({0} bytes)", p.Length);
            if (p.Length < 0x100) return;
            int id;
            uint chn = OfflineLobby.CurrentChannel(packet.Sender);
            lock (Sync) { var c = challenges.FirstOrDefault(x => DateTime.UtcNow <= x.Until && ChannelOf(x.Arena) == chn); id = c != null ? c.Id : -1; }
            if (id < 0) { Chat.SendSystemMessage(packet.Sender, "Nenhuma sala aberta agora. Crie uma sala ou espere um bot chamar para corrida."); return; }
            JoinChallenge(packet.Sender, id, Slice(p, 8, UserInfoSize), Slice(p, 0xf0, CarAttrSize), Slice(p, 0x18, PlayerInfoSize));
        }

        [RicePacket(445, RiceServer.ServerType.Game)]
        public static void RoomIdDirectJoin(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("RoomIdDirectJoin ({0} bytes)", p.Length);
            if (p.Length < 0x108) return;
            JoinChallenge(packet.Sender, BitConverter.ToInt32(p, 0x104), Slice(p, 8, UserInfoSize), Slice(p, 0xf0, CarAttrSize), Slice(p, 0x18, PlayerInfoSize));
        }

        static void JoinChallenge(RiceClient c, int id, byte[] userInfo, byte[] carAttr, byte[] playerInfo)
        {
            // teste do V do carro na sala (para a IA mostrar o V certo): o que o cliente manda do seu carro
            try { Log.WriteLine("Sala: seu UserInfo {0} | CarAttr {1}", BitConverter.ToString(userInfo ?? new byte[0]), BitConverter.ToString(carAttr ?? new byte[0])); } catch (Exception) { }
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null) return;
            Room room;
            Challenge chal;
            lock (Sync)
            {
                chal = challenges.FirstOrDefault(x => x.Id == id && DateTime.UtcNow <= x.Until);
                if (chal == null) { Chat.SendSystemMessage(c, "Essa sala ja fechou."); return; }
                int myGrade = ch.Vehicle != null ? (int) ch.Vehicle.Grade : 1;
                if (!chal.Duel && !FitsArena(chal.Arena, ch.Level, myGrade))
                {
                    Chat.SendSystemMessage(c, "Voce nao pode entrar nessa sala (" + ArenaRule(chal.Arena) + "). Voce: nivel " + ch.Level + ", carro V" + myGrade + ".");
                    return;
                }
                challenges.Remove(chal);
                LeaveRoom(c);
                LoadAi();
                var bots = chal.Racers.Select(Bots.ByCid).Where(b => b != null).ToList();
                room = new Room
                {
                    Id = chal.Id, Type = TypeOf(chal.Arena), Map = (ushort) (aiTracks.Count > 0 ? aiTracks.OrderBy(x => Rng.Next()).First() : 0),
                    Capacity = (ushort) Math.Min(Seats, bots.Count + 1), MapFlag = 0, Name = chal.Title ?? ("Desafio de " + chal.HostName),
                    Channel = ChannelOf(chal.Arena), ChallengeHost = chal.HostName, ChallengeBonus = !chal.Duel,
                    DuelBot = chal.Duel ? chal.Host : 0, DuelMate = chal.Duel ? chal.Mate : 0
                };
                for (int i = 0; i < Seats; i++) room.SeatList[i] = i < room.Capacity ? Open : Locked;
                var me = new Member
                {
                    Client = c, Char = ch, BaseSerial = SerialOf(c), UserInfo = userInfo, CarAttr = carAttr, PlayerInfo = playerInfo,
                    Name = ch.Name, Level = ch.Level, Ready = true
                };
                room.Members.Add(me);
                room.Master = me.Serial;
                Rooms[room.Id] = room;
                RoomOf[c] = room.Id;
                int seat = 0;
                foreach (var b in bots)
                {
                    var ai = NewAi(room, b.Level, b.Name, b.Car, b.AiIdx);
                    if (ai == null) break;
                    ai.Driver = (int) (me.Serial & 0xffff);
                    if (!chal.Duel) ai.PatienceUntil = PatienceFrom(DateTime.UtcNow);   // tambem perde a paciencia se demorar
                    room.Members.Add(ai);
                    CityBots.Errand(b, "battle zone", null, Math.Max(60, RiceServer.Config.BattleBotWaitSeconds));   // vai para a Battle Zone e espera la
                    MarkRacing(b, 1800);                       // na sala: ate a corrida acabar (ou a sala fechar)
                    room.SeatList[seat++] = ai.Serial;
                }
                room.SeatList[seat] = me.Serial;
                Log.WriteLine("Desafio {0}: {1} entrou na sala de {2} ({3} IA).", room.Id, ch.Name, chal.HostName, room.Ais.Count());
                Send(c, 622, MasterFrame(room));
                Send(c, 446, StateFrame(room));
                foreach (var a in room.Ais) Send(c, 485, ReadyFrame(a));
            }
            int wait = (int) (Math.Max(1, RiceServer.Config.BotRaceStartSeconds) * 1000);
            Bots.SayLater(c, 1200, "room", chal.HostName, "Chegou, " + ch.Name + "! Em " + (wait / 1000) + " s a gente larga.");
            System.Threading.Tasks.Task.Delay(wait).ContinueWith(_ =>
            {
                try
                {
                    string err = null;
                    lock (Sync)
                    {
                        if (!Rooms.ContainsKey(room.Id) || room.State != Wait || !room.Humans.Any()) return;
                        if (!RiceServer.Config.BotRaceAutoStart) err = "Aperte Iniciar quando quiser.";
                        else err = StartLoad(room, room.Map, (int) room.MapFlag, 0);
                    }
                    Bots.Say(c, "room", chal.HostName, err == null ? "Todo mundo pronto? Valendo!" : err);
                }
                catch (Exception ex) { Log.WriteError("Desafio largada: " + ex.Message); }
            });
        }

        // ------------------------------------------------------------------ dados das IAs

        class AiDriver { public int Idx, Level; public string Name; public List<int> Cars; }
        static List<AiDriver> drivers;
        static HashSet<int> aiTracks, playerCars;

        // seus corredores (alts) nao tem linha propria na tabela de IA: usam uma linha que nao e de nenhum bot (na sala
        // aparecem com o nome deles; na corrida o jogo mostra o nome da linha)
        public static int SpareAiRow(long cid)
        {
            LoadAi();
            if (drivers == null) return 0;
            var excluded = new HashSet<int>(RiceServer.Config.BattleAiExcludedCars ?? new int[0]);
            var spare = drivers.Where(d => Bots.ByName(d.Name) == null && d.Cars.Any(c => playerCars.Contains(c) && !excluded.Contains(c)))
                               .Select(d => d.Idx).OrderBy(i => i).ToList();
            return spare.Count == 0 ? 0 : spare[(int) (Math.Abs(cid) % spare.Count)];
        }

        // linha da tabela de IA com o nome do bot (cada bot tem a sua; ver Cliente/ferramentas/nomes_ia.py); 0 = nenhuma
        public static int AiRowFor(string name)
        {
            LoadAi();
            if (string.IsNullOrEmpty(name) || drivers == null) return 0;
            var excluded = new HashSet<int>(RiceServer.Config.BattleAiExcludedCars ?? new int[0]);
            var d = drivers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)
                                             && x.Cars.Any(c => playerCars.Contains(c) && !excluded.Contains(c)));
            return d != null ? d.Idx : 0;
        }

        // ---- seus corredores na corrida com o nome e o nivel deles ---------------------------------------------------
        // Na corrida o jogo mostra o nome e o nivel da LINHA da tabela de IA do cliente (Data_US\Init_us.agt), nao o
        // que o servidor manda. Cada corredor (de todas as contas) ganha uma linha so dele (alt_ai_rows.json) e o
        // servidor escreve o nome e o nivel dele nessa linha do Init_us.agt (AltsAiTableSync): no Start.bat antes de abrir
        // o jogo (Rice.exe ia) e quando o jogo fecha. Corredor novo: aparece certo na proxima vez que abrir o jogo.
        static Dictionary<long, int> altRows;
        static readonly HashSet<string> SpecialRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Taxi Driver", "Patrol Car", "Road Prowler", "Deliverer", "Fogel Bakery", "Instructor Duffy", "Elio" };
        static readonly Dictionary<int, string> rowOrigName = new Dictionary<int, string>();

        static Dictionary<long, int> AltRows()
        {
            if (altRows != null) return altRows;
            altRows = new Dictionary<long, int>();
            try
            {
                string p = Paths.SaveFile("alt_ai_rows.json");
                if (File.Exists(p))
                    altRows = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<long, int>>(File.ReadAllText(p)) ?? new Dictionary<long, int>();
            }
            catch (Exception ex) { Log.WriteError("alt_ai_rows.json: " + ex.Message); }
            return altRows;
        }

        static void SaveAltRows()
        {
            try { File.WriteAllText(Paths.SaveFile("alt_ai_rows.json"), Newtonsoft.Json.JsonConvert.SerializeObject(altRows)); }
            catch (Exception ex) { Log.WriteError("alt_ai_rows.json: " + ex.Message); }
        }

        static bool IsAltRow(int idx)
        {
            lock (rowOrigName) return AltRows().ContainsValue(idx);
        }

        // linha so do corredor (a mesma sempre); a 1a vez pega a livre de nivel mais perto
        public static int AltRowFor(long cid, string name, int level)
        {
            LoadAi();
            if (drivers == null || drivers.Count == 0) return 0;
            var botNames = new HashSet<string>(Bots.All().Where(b => !b.IsAlt && b.Name != null).Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            lock (rowOrigName)
            {
                var map = AltRows();
                int row;
                if (!map.TryGetValue(cid, out row) || !drivers.Any(d => d.Idx == row))
                {
                    var excluded = new HashSet<int>(RiceServer.Config.BattleAiExcludedCars ?? new int[0]);
                    var used = new HashSet<int>(map.Values);
                    var free = drivers.Where(d => !used.Contains(d.Idx) && !SpecialRows.Contains(OrigName(d))
                                                  && !botNames.Contains(OrigName(d))
                                                  && d.Cars.Any(c => playerCars.Contains(c) && !excluded.Contains(c))).ToList();
                    if (free.Count == 0) return 0;
                    row = free.OrderBy(d => Math.Abs(d.Level - level)).ThenBy(d => d.Idx).First().Idx;
                    map[cid] = row;
                    SaveAltRows();
                    Log.WriteLine("Corredor {0}: linha {1} da tabela de IA (o nome aparece na corrida depois de abrir o jogo de novo).", name, row);
                }
                var dr = drivers.First(d => d.Idx == row);
                OrigName(dr);
                if (!string.IsNullOrEmpty(name)) dr.Name = name;
                if (level > 0) dr.Level = Math.Max(1, Math.Min(99, level));
                return row;
            }
        }

        static string OrigName(AiDriver d)
        {
            string n;
            if (!rowOrigName.TryGetValue(d.Idx, out n)) rowOrigName[d.Idx] = n = d.Name;
            return n;
        }

        static readonly byte[] AgtKey = Convert.FromBase64String("ND0w6Gc2NjI+MzQ7ERUWFhQTHRgRAwYMBAMGCC5VJiMqIy4oISEmJy4ALS3PpQYCBA8HGOEVNhhgExoZERUWEBITFzjxJQEFBgIEAwcIAQUGDwQDBwwxhXY5");

        static void AgtXor(byte[] b, int start, int len, int pos)
        {
            for (int i = 0; i < len; i++) b[start + i] ^= AgtKey[(((pos + i - 20) % 90) + 90) % 90];
        }

        static byte[] AgtPlain(byte[] d, int at, int len) { var b = new byte[len]; Array.Copy(d, at, b, 0, len); AgtXor(b, 0, len, at); return b; }

        static byte[] ZlibCompress(byte[] data, int at, int len)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0xDA);
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) ds.Write(data, at, len);
                uint a = 1, b = 0;
                for (int i = 0; i < len; i++) { a = (a + data[at + i]) % 65521; b = (b + a) % 65521; }
                uint ad = (b << 16) | a;
                ms.WriteByte((byte) (ad >> 24)); ms.WriteByte((byte) (ad >> 16)); ms.WriteByte((byte) (ad >> 8)); ms.WriteByte((byte) ad);
                return ms.ToArray();
            }
        }

        static byte[] ZlibDecompress(byte[] data)
        {
            using (var ms = new MemoryStream(data, 2, data.Length - 2))
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var o = new MemoryStream()) { ds.CopyTo(o); return o.ToArray(); }
        }

        public static string ClientInitPath()
        {
            foreach (var p in new[] { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Data_US", "Init_us.agt"),
                                      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Data_US", "Init_us.agt") })
                if (File.Exists(p)) return Path.GetFullPath(p);
            return null;
        }

        // escreve o nome e o nivel de todos os corredores nas linhas deles no Init_us.agt do cliente
        public static void SyncClientAiTable(bool gameClosed)
        {
            if (!RiceServer.Config.AltsAiTableSync) return;
            try
            {
                LoadAi();
                if (!gameClosed && System.Diagnostics.Process.GetProcessesByName("DriftCity").Length > 0) return;   // o jogo ja leu o arquivo
                string path = ClientInitPath();
                if (path == null) { Log.WriteLine("Tabela de IA: nao achei o Data_US\\Init_us.agt (o Rice tem que ficar na pasta do jogo)."); return; }
                var want = new Dictionary<int, Tuple<string, int>>();
                using (var rc = Rice.Database.GetContext())
                    foreach (var c in rc.Characters.ToList())
                    {
                        if (string.IsNullOrEmpty(c.Name)) continue;
                        int row = AltRowFor(c.ID, c.Name, c.Level);
                        if (row > 0) want[row] = Tuple.Create(c.Name, Math.Max(1, Math.Min(99, c.Level)));
                    }
                if (want.Count == 0) return;

                byte[] d = File.ReadAllBytes(path);
                if (d.Length < 64 || Encoding.ASCII.GetString(d, 0, 8) != "NayaPack") return;
                var h = AgtPlain(d, 32, 12);
                int off = BitConverter.ToInt32(h, 0), nch = BitConverter.ToInt32(h, 4), usize = BitConverter.ToInt32(h, 8);
                // as outras entradas (para saber onde a tabela cabe)
                int count = BitConverter.ToInt32(d, 16), p = 44, nextOff = int.MaxValue, endOthers = 0;
                for (int e = 0; e < count && p + 4 <= d.Length; e++)
                {
                    int L = BitConverter.ToInt32(AgtPlain(d, p, 4), 0);
                    if (L <= 0 || L > 260 || p + 16 + L > d.Length) break;
                    var meta = AgtPlain(d, p + 4 + L, 12);
                    int eo = BitConverter.ToInt32(meta, 0), en = BitConverter.ToInt32(meta, 4);
                    if (eo < 0 || en < 0 || eo + 2 * en > d.Length) break;
                    var et = AgtPlain(d, eo, 2 * en);
                    int end = eo + 2 * en;
                    for (int k = 0; k < en; k++) end += BitConverter.ToUInt16(et, 2 * k);
                    if (eo > off) nextOff = Math.Min(nextOff, eo);
                    endOthers = Math.Max(endOthers, end);
                    p += 16 + L;
                }
                var tab = AgtPlain(d, off, 2 * nch);
                var raw = new MemoryStream();
                int q = off + 2 * nch;
                for (int k = 0; k < nch; k++)
                {
                    int n = BitConverter.ToUInt16(tab, 2 * k);
                    var part = ZlibDecompress(AgtPlain(d, q, n));
                    raw.Write(part, 0, part.Length); q += n;
                }
                var bytes = raw.ToArray();
                if (bytes.Length != usize) { Log.WriteError("Tabela de IA do Init_us.agt: tamanho diferente, nao mexi."); return; }
                string text = Encoding.Unicode.GetString(bytes);
                bool bom = text.Length > 0 && text[0] == '﻿';
                if (bom) text = text.Substring(1);
                var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var c = lines[i].Split('\t');
                    int row;
                    if (c.Length < 3 || !int.TryParse(c[0], out row)) continue;
                    // numero com espaco no fim ("227 "): o jogo le igual; libera lugar no arquivo
                    for (int k = 3; k < c.Length; k++)
                    {
                        string t = c[k].TrimEnd(' ');
                        double dv;
                        if (t.Length != c[k].Length && t.Length > 0 && double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv)) c[k] = t;
                    }
                    Tuple<string, int> w;
                    if (want.TryGetValue(row, out w)) { c[1] = w.Item1; c[2] = w.Item2.ToString(); }
                    string nl = string.Join("\t", c);
                    if (nl != lines[i]) { lines[i] = nl; changed = true; }
                }
                if (!changed) return;
                string backup = path + ".antes_corredores";
                if (!File.Exists(backup)) File.Copy(path, backup);
                var plain = Encoding.Unicode.GetBytes((bom ? "﻿" : "") + string.Join("\r\n", lines));
                var chunks = new List<byte[]>();
                for (int k = 0; k < plain.Length; k += 16384) chunks.Add(ZlibCompress(plain, k, Math.Min(16384, plain.Length - k)));
                int size = 2 * chunks.Count + chunks.Sum(x => x.Length);
                // cabe no lugar dela: fica; nao cabe: vai para o fim do arquivo (como o vshop_precos.py)
                int pos, total;
                if (off < endOthers && size <= nextOff - off) { pos = off; total = d.Length; }         // no lugar dela
                else if (off >= endOthers) { pos = off; total = off + size; }                        // ja estava no fim
                else { pos = d.Length; total = d.Length + size; }                                    // vai para o fim
                var outb = new byte[total];
                Array.Copy(d, outb, Math.Min(d.Length, total));
                int w0 = pos;
                foreach (var ch in chunks) { Array.Copy(BitConverter.GetBytes((ushort) ch.Length), 0, outb, w0, 2); w0 += 2; }
                foreach (var ch in chunks) { Array.Copy(ch, 0, outb, w0, ch.Length); w0 += ch.Length; }
                AgtXor(outb, pos, size, pos);
                var nh = new byte[12];
                Array.Copy(BitConverter.GetBytes(pos), 0, nh, 0, 4);
                Array.Copy(BitConverter.GetBytes(chunks.Count), 0, nh, 4, 4);
                Array.Copy(BitConverter.GetBytes(plain.Length), 0, nh, 8, 4);
                AgtXor(nh, 0, 12, 32);
                Array.Copy(nh, 0, outb, 32, 12);
                File.WriteAllBytes(path, outb);
                Log.WriteLine("Tabela de IA: nome e nivel de {0} corredor(es) gravados no Init_us.agt.", want.Count);
            }
            catch (Exception ex) { Log.WriteError("Tabela de IA (corredores): " + ex.Message); }
        }

        static void LoadAi()
        {
            if (drivers != null) return;
            drivers = new List<AiDriver>();
            aiTracks = new HashSet<int>();
            playerCars = new HashSet<int>();
            try
            {
                var j = JObject.Parse(ReadAiJson());
                foreach (var d in j["drivers"])
                    drivers.Add(new AiDriver
                    {
                        Idx = (int) d["idx"], Level = (int) d["level"], Name = (string) d["name"],
                        Cars = d["cars"].Select(x => (int) x).ToList()
                    });
                foreach (var t in j["aiTracks"]) aiTracks.Add((int) t);
                foreach (var c in j["playerCars"]) playerCars.Add((int) c);
                Log.WriteLine("Battle: {0} pilotos IA, {1} pistas com IA.", drivers.Count, aiTracks.Count);
            }
            catch (Exception ex)
            {
                Log.WriteError("Battle: nao consegui ler res/BattleAI.json: " + ex.Message);
            }
        }

        // Procura o arquivo ao lado do Rice.exe; se nao achar, usa a copia embutida no proprio Rice.exe.
        static string ReadAiJson()
        {
            string[] paths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "BattleAI.json"),
                "res/BattleAI.json"
            };
            foreach (var p in paths)
                if (File.Exists(p)) return File.ReadAllText(p);
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("BattleAI.json"))
            {
                if (s == null) throw new FileNotFoundException("BattleAI.json (nem arquivo nem recurso embutido)");
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static ushort visualAge = 200;

        // 1061 VisualUpdate (u16 serial, u16 age, u32 carId, 1 byte, registro do carro): o cliente remonta o carro atual
        // com a cor dele. Usado ao sair de uma sala de time (o carro ficava com a cor do time).
        static void RefreshCar(RiceClient c)
        {
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            var car = ch != null ? ch.Vehicle : null;
            if (car == null) return;
            var p = new RicePacket(1061);
            p.Writer.Write(ch.Serial);
            p.Writer.Write(visualAge++);
            p.Writer.Write((uint) car.CarID);
            p.Writer.Write((byte) 0);
            p.Writer.Write(car.GetInfo());
            c.Send(p);
            Log.WriteLine("Carro {0} remontado com a cor original (saiu de sala de time).", car.CarID);
        }

        // ---- cartas "?" do fim da corrida (BattleZone.BattleReward) --------------------------------------------
        // Achado no DriftCity.exe 2016: clicar numa carta manda 483 (u32 carta, u32 -1); o cliente espera
        // 484 Cmd_PvpBattleBonusResult (handler 0x542720): u32 n, n x 24 bytes (u32 carta, u32 0, u32 serial de quem
        // pegou, u32 tipo, u32 valor, u32 quantidade). Tipo 1 = item (valor = TableIdx do item, nome pela tabela).
        // A carta com o serial do jogador vira a escolha dele; as outras aparecem com o nome de quem pegou (IAs).
        [RicePacket(483, RiceServer.ServerType.Game)]
        public static void PvpBattleBonus(RicePacket packet)
        {
            var p = packet.Buffer;
            var c = packet.Sender;
            var ch = c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null || p.Length < 4) return;
            int card = BitConverter.ToInt32(p, 0);
            if (card < 0 || card > 15 || !RiceServer.Config.BattleRewardCards) return;
            var entries = new List<byte[]>();
            string got = null;
            lock (Sync)
            {
                var room = RoomOfClient(c);
                var me = room != null ? MemberOf(room, c) : null;
                if (room == null || me == null) return;
                if (room.CardsTaken == null) room.CardsTaken = new HashSet<int>();
                if (room.CardsTaken.Contains(card) || room.CardPicked.Contains(me.Serial)) return;
                // so quem cruzou a linha de chegada pega carta
                if (!me.Ended || me.EndType != Finished)
                {
                    Chat.SendSystemMessage(c, "Voce nao terminou a corrida: sem carta dessa vez.");
                    return;
                }
                var e = Auction.CardReward(ch.Level);
                Rice.Game.Item it = null;
                if (e != null && ch.GrantItem(e.ID, 1, out it) && it != null)
                {
                    got = e.Name;
                    room.CardsTaken.Add(card); room.CardPicked.Add(me.Serial);
                    entries.Add(CardEntry(card, me.Serial, 1, (uint) Math.Max(0, it.tblIdx), 1));
                }
                // os bots so pegam as cartas deles quando a corrida acaba (AiCards, no resultado)
            }
            Log.WriteLine("Carta {0} da corrida: {1} ganhou {2}.", card, ch.Name, got ?? "nada");
            if (entries.Count == 0) return;
            Send(c, 484, Build(w => { w.Write((uint) entries.Count); foreach (var e in entries) w.Write(e); }));
            // depois da sua carta, os bots pegam as deles
            System.Threading.Tasks.Task.Delay(700).ContinueWith(_ => AiCardsNow(c));
            if (got != null)
            {
                ch.FlushModInfo(c);
                Chat.SendSystemMessage(c, "Carta da corrida: voce ganhou " + got + "!");
            }
        }

        // fim da corrida: as IAs pegam as cartas que sobraram (aparecem com o nome delas na tela das cartas)
        static void AiCards(Room room)
        {
            if (!RiceServer.Config.BattleRewardCards) return;
            if (room.CardsTaken == null) room.CardsTaken = new HashSet<int>();
            var entries = new List<byte[]>();
            int slots = Math.Min(16, Math.Max(room.Members.Count, 2));
            // os bots que cruzaram a linha de chegada e os que ainda estavam correndo quando o primeiro chegou (o jogo
            // encerra as IAs na sua chegada e elas nao mandam a chegada delas); quem desistiu antes fica sem carta
            foreach (var ai in room.Ais.Where(a => ((a.Ended && a.EndType == Finished) || a.LateEnd || !a.Ended) && !room.CardPicked.Contains(a.Serial)).ToList())
            {
                var free = Enumerable.Range(0, slots).Where(i => !room.CardsTaken.Contains(i)).ToList();
                if (free.Count == 0) break;
                int s = free[Rng.Next(free.Count)];
                var ae = Auction.CardReward(ai.Level);
                int idx = ae != null ? Rice.Server.Structures.Resources.ItemTable.Items.IndexOf(ae) : -1;
                if (idx < 0) continue;
                room.CardsTaken.Add(s); room.CardPicked.Add(ai.Serial);
                entries.Add(CardEntry(s, ai.Serial, 1, (uint) idx, 1));
                // seu corredor na sua party: a carta vale de verdade (o item vai para o inventario dele)
                try
                {
                    var ab = Bots.ByName(ai.Name);
                    var hm = room.Humans.FirstOrDefault(h => h.Char != null && BotSocial.PartyBots(h.Char).Contains(ab));
                    if (ab != null && ab.IsAlt && hm != null)
                    {
                        var ach = BotSocial.AltCharacter(hm.Client.Player, ab);
                        Rice.Game.Item got;
                        if (ach != null && ach.GrantItem(ae.ID, 1, out got) && got != null)
                        {
                            var hc = hm.Client;
                            string line = ab.Name + " (seu corredor) ganhou na carta: " + ae.Name + ".";
                            System.Threading.Tasks.Task.Delay(900).ContinueWith(_ => { try { Chat.SendSystemMessage(hc, line); } catch (Exception) { } });
                        }
                        else if (ach != null) Log.WriteLine("Carta de {0}: inventario cheio ({1}).", ab.Name, ae.Name);
                    }
                }
                catch (Exception ex) { Log.WriteError("Carta (seu corredor): " + ex.Message); }
            }
            if (entries.Count > 0) Cast(room, 484, Build(w => { w.Write((uint) entries.Count); foreach (var e in entries) w.Write(e); }));
        }

        static void AiCardsNow(RiceClient c)
        {
            try { lock (Sync) { var room = RoomOfClient(c); if (room != null) AiCards(room); } }
            catch (Exception ex) { Log.WriteError("Cartas dos bots: " + ex.Message); }
        }

        static byte[] CardEntry(int card, uint serial, uint type, uint value, uint amount)
        {
            return Build(w => { w.Write((uint) card); w.Write(0u); w.Write(serial); w.Write(type); w.Write(value); w.Write(amount); });
        }

        // O ceu da pista fica preto sem clima definido: manda o clima atual antes de carregar a pista.
        static void CastWeather(Room room)
        {
            foreach (var m in room.Members.Where(x => !x.IsAi))
                try { Weather.SendTo(m.Client); } catch (Exception) { }
        }

        static Member NewAi(Room room, int level, string forcedName = null, int forcedCar = 0, int forcedIdx = 0)
        {
            LoadAi();
            var cfg = RiceServer.Config;
            var excluded = new HashSet<int>((cfg.BattleAiExcludedCars ?? new int[0]).Concat(cfg.PoliceCars ?? new int[0]));
            Func<int, bool> usable = c => playerCars.Contains(c) && !excluded.Contains(c);
            var taken = new HashSet<int>(room.Ais.Select(a => a.AiIdx));
            var pool = drivers.Where(d => !taken.Contains(d.Idx) && d.Cars.Any(usable) && !IsAltRow(d.Idx)).ToList();   // linhas dos seus corredores: so eles
            // so os nossos bots na corrida: linhas da tabela de IA com o nome de um bot (os pilotos originais ficam de fora)
            if (forcedName == null && cfg.BattleAiOnlyBots)
            {
                var inRoom = new HashSet<string>(room.Ais.Select(a => a.Name ?? ""), StringComparer.OrdinalIgnoreCase);
                int ar = ArenaOfRoom(room.Channel, room.Type);
                var mine = pool.Where(d => { var bb = Bots.ByName(d.Name); return bb != null && !bb.IsAlt && !inRoom.Contains(bb.Name) && BotFits(ar, bb); }).ToList();
                if (mine.Count > 0) pool = mine;
                else if (ar >= 0) return null;                     // nenhum bot nosso cabe nessa arena (nivel / V do carro)
            }
            if (pool.Count == 0) return null;

            // Dificuldade: o nivel do piloto IA define a velocidade dele (nivel 1 ~227 km/h, nivel 99 ~408 km/h).
            // Alvo = nivel do jogador x BattleAiDifficulty, limitado a BattleAiMaxLevel; com variacao de +-BattleAiLevelSpread.
            int target = (int) Math.Round(Math.Min(level, 99) * cfg.BattleAiDifficulty);
            target = Math.Max(1, Math.Min(target, Math.Max(1, cfg.BattleAiMaxLevel)));
            target += Rng.Next(-cfg.BattleAiLevelSpread, cfg.BattleAiLevelSpread + 1);
            target = Math.Max(1, Math.Min(target, Math.Max(1, cfg.BattleAiMaxLevel)));
            int best = pool.Min(d => Math.Abs(d.Level - target));
            var near = pool.Where(d => Math.Abs(d.Level - target) <= best + 2).ToList();
            var drv = near[Rng.Next(near.Count)];
            // bot da party: a linha da tabela de IA com o nome dele (o cliente mostra o nome pela linha)
            var own = forcedIdx > 0 ? drivers.FirstOrDefault(d => d.Idx == forcedIdx && !taken.Contains(d.Idx)) : null;
            if (own != null) drv = own;
            // piloto que e um bot nosso: corre com o carro e o visual dele
            if (forcedName == null && cfg.BattleAiOnlyBots)
            {
                var mb = Bots.ByName(drv.Name);
                if (mb != null && !mb.IsAlt) { forcedName = mb.Name; if (forcedCar <= 0) forcedCar = mb.Car; }
            }
            var cars = drv.Cars.Where(usable).ToList();
            if (cars.Count == 0 && usable(forcedCar)) cars.Add(forcedCar);
            if (cars.Count == 0) { drv = near[Rng.Next(near.Count)]; cars = drv.Cars.Where(usable).ToList(); }
            int car = cars[Rng.Next(cars.Count)];
            if (forcedCar > 0 && usable(forcedCar)) car = forcedCar;   // bot da party corre com o carro dele

            room.AiKeys++;
            var ai = new Member
            {
                Key = room.AiKeys, AiIdx = drv.Idx, CarType = car, Level = Math.Max(1, drv.Level), Name = forcedName ?? drv.Name,
                Ready = true, Loaded = true
            };
            ai.BaseSerial = (uint) (ai.Key << 16);
            // cor e visual: o bot com o carro dele (pintura, vidro, neon, rodas...); IA comum: uma cor da paleta
            var bp = forcedName != null ? Bots.ByName(forcedName) : null;
            // seu corredor: o nivel de agora no banco (o perfil do bot guardava o do login; subir de nivel jogando com ele
            // depois nao aparecia na sala)
            if (bp != null && bp.IsAlt)
            {
                try
                {
                    using (var rc = Rice.Database.GetContext())
                    {
                        var dc = rc.Characters.Find(bp.Cid);
                        if (dc != null && dc.Level > 0) bp.Level = dc.Level;
                    }
                }
                catch (Exception) { }
            }
            if (bp != null && bp.Level > 0) ai.Level = bp.Level;    // nivel de verdade do bot / do seu corredor (nao o da linha de IA)
            ai.Color = bp != null ? bp.Color : Bots.PaintColor(Rng.Next(0, 29));
            if (bp != null && bp.Look != null && bp.Look.Tint != 0) ai.Tint = bp.Look.Tint;
            ai.UserInfo = Build(w => { w.Write((ushort) 0); w.Write((ushort) ai.Level); w.Write((ushort) 0); w.Write((ushort) 0); w.Write(0); });
            SetAiCarAttr(ai);
            byte[] pi;
            if (bp != null) pi = Sized(CityBots.PlayerInfo(bp), PlayerInfoSize);
            else
            {
                pi = new byte[PlayerInfoSize];
                Array.Copy(WStr(ai.Name, 26), pi, 26);
            }
            BitConverter.GetBytes((ushort) 0).CopyTo(pi, 0x1a);
            BitConverter.GetBytes((ushort) 1).CopyTo(pi, 0x1c);
            BitConverter.GetBytes((ushort) ai.Level).CopyTo(pi, 0x26);
            ai.PlayerInfo = pi;
            if (bp != null && cfg.BattleBotsAsPlayers && room.State == Wait) DressAsPlayer(ai);
            return ai;
        }

        // ---- bot como jogador na sala --------------------------------------------------------------------------------
        // Na sala de espera o bot entra com serial de jogador (<= 0xffff), entao o jogo mostra o slot igual ao seu: nome,
        // nivel, carro e crew. Na hora de carregar a corrida ele sai (469) e volta como IA (468) no mesmo assento - o seu
        // jogo pilota as IAs. Uns segundos depois do resultado, volta a aparecer como jogador.
        static void DressAsPlayer(Member ai)
        {
            ai.AsPlayer = true;
            ai.LobbySerial = (uint) (0xF000 + (ai.Key & 0xfff));
            ai.UserInfo = Build(w => { w.Write((byte) Math.Min(255, ai.Level)); w.Write((byte) 1); w.Write((ushort) 1); w.Write(new byte[12]); });
            ai.CarAttr = Build(w => { w.Write((ushort) 0); w.Write((ushort) ai.CarType); w.Write(ai.Color); w.Write(ai.Tint); });
        }
        static void DressAsAi(Member ai)
        {
            ai.AsPlayer = false;
            ai.UserInfo = Build(w => { w.Write((ushort) 0); w.Write((ushort) ai.Level); w.Write((ushort) 0); w.Write((ushort) 0); w.Write(0); });
            SetAiCarAttr(ai);
        }
        static void RedressAll(Room room)
        {
            room.BackInRoom = true;
            bool any = false;
            foreach (var ai in room.Ais.Where(a => !a.AsPlayer && Bots.ByName(a.Name) != null).ToList()) { Redress(room, ai, true); any = true; }
            if (any || room.AfterRace) Resync(room);
        }

        // 516 (sem nome no jogo): chega duas vezes no fim da corrida - 01-00-FF-FF-FF-FF com o resultado na tela e
        // 00-00-FF-FF-FF-FF quando voce volta para a sala. Usado para os bots voltarem a aparecer como jogador so quando voce esta na tela da sala.
        [RicePacket(516, RiceServer.ServerType.Game)]
        public static void BackToRoom(RicePacket packet)
        {
            var p = packet.Buffer ?? new byte[0];
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                Log.WriteLine("Sala: 516 ({0})", BitConverter.ToString(p));
                // tela das cartas: se voce nao pegar a sua (ou nao terminou), os bots pegam as deles em 6 s
                if (room != null && p.Length > 0 && p[0] == 1)
                {
                    var c = packet.Sender;
                    System.Threading.Tasks.Task.Delay(6000).ContinueWith(_ => AiCardsNow(c));
                }
            }
        }

        // 302 LeaveArea: voce saiu da pista (area da corrida) e voltou de verdade para a tela da sala - so agora os bots
        // voltam a aparecer como jogador (o 516 chega com a tabela de resultado ainda na tela)
        [RicePacket(302, RiceServer.ServerType.Game)]
        public static void LeaveRaceArea(RicePacket packet)
        {
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null || room.State != Wait || room.BackInRoom) return;
                Log.WriteLine("Sala {0}: voce voltou para a sala.", room.Id);
                if (RiceServer.Config.BattleBotsAsPlayers) RedressAll(room); else room.BackInRoom = true;
            }
        }

        // troca na sala: sai com o serial antigo e entra com o novo no mesmo assento
        // O jogo pode conhecer o bot por mais de um serial (o da sala, o de IA e o de IA com piloto do 625): saia com
        // todos antes de entrar de novo. Antes so saia com um, e depois da corrida a sala ficava 16/8 (o bot duplicado).
        static void Redress(Room room, Member ai, bool asPlayer)
        {
            uint old = ai.Serial;
            var olds = new List<uint> { old, (uint) (ai.Key << 16), (uint) (ai.Key << 16 | (ai.Driver & 0xffff)) };
            if (ai.LobbySerial != 0) olds.Add(ai.LobbySerial);
            foreach (var s in olds.Distinct()) Cast(room, 469, LeaveFrame(room, s));
            if (asPlayer) DressAsPlayer(ai); else { DressAsAi(ai); ai.Assigned = false; }
            ReplaceSeat(room, old, ai.Serial);
            Cast(room, 468, JoinFrame(room, ai));
            Cast(room, 485, ReadyFrame(ai));
        }

        // manda a sala inteira de novo (mestre, estado com todos os membros, assentos e prontos): o jogo refaz a lista
        // e a contagem (ex.: 8/8) fica certa
        static void Resync(Room room)
        {
            if (!RiceServer.Config.BattleRoomResync || room.State != Wait) return;
            Cast(room, 622, MasterFrame(room));
            Cast(room, 446, StateFrame(room));
            Cast(room, 621, ModifyFrame(room));
            foreach (var m in room.Members) Cast(room, 485, ReadyFrame(m));
            Cast(room, 823, BitConverter.GetBytes((ushort) Math.Min(room.Spectators.Count, ushort.MaxValue)));
        }

        static void SetAiCarAttr(Member ai)
        {
            ai.CarAttr = Build(w => { w.Write((ushort) ai.Key); w.Write((ushort) ai.CarType); w.Write(ai.Color); w.Write(ai.Tint); });
        }

        static byte[] AiEntry(Member a)
        {
            return Build(w =>
            {
                // o cliente conta as linhas da tabela de IA a partir de 0 (Idx 1 = 0): com o Idx direto aparecia o nome
                // da linha de baixo (Brunizin.X 217 mostrava Joaopvbr 218)
                w.Write((uint) a.Key); w.Write((uint) a.Driver); w.Write((uint) Math.Max(0, a.AiIdx - 1));
                w.Write((uint) a.CarType); w.Write(a.Color); w.Write(a.Tint);
            });
        }

        // corrida de time: IA no time vermelho (assentos pares) pintada de vermelho, no azul (impares) de azul; a cor
        // propria volta quando a corrida acaba (Result)
        static void TeamColors(Room room, bool on)
        {
            if ((room.Type & 1) == 0) return;
            foreach (var ai in room.Ais)
            {
                var bp = Bots.ByName(ai.Name);
                if (on)
                {
                    int seat = Array.IndexOf(room.SeatList, ai.Serial);
                    // assento par = time azul (o mestre da sala fica no 0 e e azul), impar = vermelho
                    ai.Color = seat >= 0 && seat % 2 == 0 ? RiceServer.Config.BattleTeamBlueColor : RiceServer.Config.BattleTeamRedColor;
                }
                else ai.Color = bp != null ? bp.Color : Bots.PaintColor(Rng.Next(0, 29));
                SetAiCarAttr(ai);
            }
        }

        static void ReplaceSeat(Room r, uint oldSerial, uint newSerial)
        {
            for (int i = 0; i < Seats; i++) if (r.SeatList[i] == oldSerial) r.SeatList[i] = newSerial;
        }

        // ------------------------------------------------------------------ sala

        [RicePacket(460, RiceServer.ServerType.Game)]
        public static void RoomCreate(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("RoomCreate ({0} bytes)", p.Length);
            if (p.Length < 0x9a + CarAttrSize || packet.Sender.Player == null) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            lock (Sync)
            {
                LeaveRoom(packet.Sender);
                var room = new Room
                {
                    Id = nextId++,
                    Type = BitConverter.ToUInt32(p, 0x10),
                    Map = BitConverter.ToUInt16(p, 0x14),
                    Capacity = (ushort) Math.Max(1, Math.Min((int) BitConverter.ToUInt16(p, 0x16), Seats)),
                    MapFlag = BitConverter.ToUInt16(p, 0x18),
                    Name = ReadWStr(p, 0x22, 60),
                    Channel = OfflineLobby.CurrentChannel(packet.Sender)
                };
                for (int i = 0; i < Seats; i++) room.SeatList[i] = i < room.Capacity ? Open : Locked;
                var m = new Member
                {
                    Client = packet.Sender, Char = ch, BaseSerial = SerialOf(packet.Sender),
                    UserInfo = Slice(p, 0, UserInfoSize), CarAttr = Slice(p, 0x9a, CarAttrSize),
                    Name = ch.Name, Level = ch.Level, Ready = true
                };
                try { Log.WriteLine("Sala: seu UserInfo {0} | CarAttr {1}", BitConverter.ToString(m.UserInfo), BitConverter.ToString(m.CarAttr)); } catch (Exception) { }
                room.Members.Add(m);
                room.Master = m.Serial;
                room.SeatList[0] = m.Serial;
                Rooms[room.Id] = room;
                RoomOf[packet.Sender] = room.Id;
                Log.WriteLine("Sala {0} criada: '{1}', tipo {2:X}, capacidade {3}, serial {4}.", room.Id, room.Name, room.Type, room.Capacity, m.Serial);

                Send(packet.Sender, 622, MasterFrame(room));
                Send(packet.Sender, 446, StateFrame(room));
                Send(packet.Sender, 461, Build(w =>
                {
                    w.Write(Sized(m.UserInfo, UserInfoSize));
                    w.Write(1); w.Write((uint) room.Id); w.Write((uint) room.Id); w.Write(room.Type);
                    w.Write(room.Map); w.Write((ushort) (room.MapFlag & 0xffff));
                }));
            }
            Chat.SendSystemMessage(packet.Sender, "Sala criada. Clique num assento vazio com o botao de IA para adicionar um corredor IA; escolha uma pista com o icone de IA.");
            AddPartyBots(packet.Sender);
        }

        // Bots da party entram sozinhos na sala que o jogador criou (como corredores IA, com o nome e o carro deles).
        static void AddPartyBots(RiceClient c)
        {
            var bots = BotSocial.PartyBots(c.Player.ActiveCharacter);
            if (bots.Count == 0 || !RiceServer.Config.BattleAiEnabled) return;
            var added = new List<string>();
            lock (Sync)
            {
                var room = RoomOfClient(c);
                if (room == null || room.State != Wait) return;
                var me = MemberOf(room, c);
                if (me == null) return;
                int arena = ArenaOfRoom(room.Channel, room.Type);
                foreach (var b in bots.Where(x => !BotFits(arena, x)).ToList())
                    Bots.SayLater(c, 1500, "party", b.Name, "Nao posso entrar nessa sala (" + ArenaRule(arena) + "): sou nivel " + b.Level + " com carro V" + BotSocial.GradeOf(b) + ".");
                bots = bots.Where(x => BotFits(arena, x)).ToList();
                foreach (var b in bots)
                {
                    int seat = -1;
                    for (int i = 0; i < room.Capacity && i < Seats; i++)
                        if (room.SeatList[i] == Open) { seat = i; break; }
                    if (seat < 0) break;
                    var ai = NewAi(room, b.Level, b.Name, b.Car, b.AiIdx);
                    if (ai == null) break;
                    ai.Driver = (int) (me.Serial & 0xffff);
                    room.Members.Add(ai);
                    CityBots.Errand(b, "battle zone", null, Math.Max(60, RiceServer.Config.BattleBotWaitSeconds));   // vai para a Battle Zone e espera la
                    MarkRacing(b, 1800);
                    room.SeatList[seat] = ai.Serial;
                    Cast(room, 468, JoinFrame(room, ai));
                    Cast(room, 485, ReadyFrame(ai));
                    added.Add(b.Name);
                    Log.WriteLine("Sala {0}: {1} (party) entrou como IA no assento {2}.", room.Id, b.Name, seat);
                }
                if (added.Count > 0) Cast(room, 621, ModifyFrame(room));
            }
            if (added.Count > 0)
                Chat.SendSystemMessage(c, "Sua party entrou na sala: " + string.Join(", ", added) + ". (So corre em pista com icone de IA.)");
        }

        [RicePacket(621, RiceServer.ServerType.Game)]
        public static void RoomModify(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("RoomModify ({0} bytes)", p.Length);
            if (p.Length < 0xc + 4 * Seats) return;
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null) return;
                var me = MemberOf(room, packet.Sender);
                if (me == null || me.Serial != room.Master) return;

                room.Map = BitConverter.ToUInt16(p, 4);
                room.MapFlag = BitConverter.ToUInt32(p, 6);
                ushort cap = (ushort) Math.Max(1, Math.Min((int) BitConverter.ToUInt16(p, 10), Seats));
                if (cap < room.Members.Count) cap = (ushort) room.Members.Count;
                room.Capacity = cap;

                for (int i = 0; i < Seats; i++)
                {
                    uint asked = BitConverter.ToUInt32(p, 0xc + 4 * i);
                    uint cur = room.SeatList[i];
                    if (asked == 0xffff && cur == Open && i < cap && room.State == Wait && RiceServer.Config.BattleAiEnabled)
                    {
                        var ai = NewAi(room, me.Level);
                        if (ai != null)
                        {
                            ai.Driver = (int) (me.Serial & 0xffff);
                            room.Members.Add(ai);
                            room.SeatList[i] = ai.Serial;
                            Log.WriteLine("Sala {0}: IA {1} (carro {2}) no assento {3}.", room.Id, ai.Name, ai.CarType, i);
                            Cast(room, 468, JoinFrame(room, ai));
                            Cast(room, 485, ReadyFrame(ai));
                        }
                        continue;
                    }
                    // clique no bot (IA) no assento: sai da sala (e nao volta sozinho nessa sala)
                    if ((asked == 0xffff || asked == Open || asked == Locked) && cur != Open && cur != Locked && asked != cur && room.State == Wait
                        && room.Members.Any(x => x.IsAi && x.Serial == cur))
                    {
                        var ai = room.Members.FirstOrDefault(x => x.IsAi && x.Serial == cur);
                        if (ai != null)
                        {
                            room.Members.Remove(ai);
                            room.SeatList[i] = asked == Locked || i >= cap ? Locked : Open;
                            if (ai.Name != null) room.Kicked.Add(ai.Name);
                            Cast(room, 469, LeaveFrame(room, cur));
                            var kb = Bots.ByName(ai.Name);
                            if (kb != null) EndRacing(kb, null);
                            Log.WriteLine("Sala {0}: {1} tirou {2} da sala.", room.Id, me.Name, ai.Name);
                        }
                        continue;
                    }
                    if (cur != Open && cur != Locked) continue;
                    room.SeatList[i] = (i >= cap || asked == Locked) ? Locked : Open;
                }
                Cast(room, 621, ModifyFrame(room));
            }
        }

        // 470 RoomBanish (botao de expulsar no assento; DriftCity.exe 0x84ba1a): u32 serial de quem sai. A resposta e o
        // mesmo 470 (0x540480: serial + assentos + tipo, igual ao 469); com o serial de outro, o jogo tira ele da sala.
        [RicePacket(470, RiceServer.ServerType.Game)]
        public static void RoomBanish(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 4) return;
            uint who = BitConverter.ToUInt32(p, 0);
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null || room.State != Wait) return;
                var me = MemberOf(room, packet.Sender);
                if (me == null || me.Serial != room.Master || who == me.Serial) return;
                var ai = room.Members.FirstOrDefault(x => x.IsAi && x.Serial == who);
                if (ai == null) { Log.WriteLine("Sala {0}: expulsar {1:X} - nao achei ({2}).", room.Id, who, BitConverter.ToString(p)); return; }
                room.Members.Remove(ai);
                ReplaceSeat(room, who, Open);
                if (ai.Name != null) room.Kicked.Add(ai.Name);
                room.NextBotJoin = DateTime.UtcNow.AddSeconds(Math.Max(5, RiceServer.Config.BattleBotJoinSeconds));
                Cast(room, 470, LeaveFrame(room, who));
                var kb = Bots.ByName(ai.Name);
                if (kb != null) EndRacing(kb, null);
                Log.WriteLine("Sala {0}: {1} expulsou {2}.", room.Id, me.Name, ai.Name);
            }
        }

        [RicePacket(469, RiceServer.ServerType.Game)]
        public static void RoomLeave(RicePacket packet)
        {
            lock (Sync) LeaveRoom(packet.Sender);
        }

        static void LeaveRoom(RiceClient c)
        {
            var room = RoomOfClient(c);
            RoomOf.Remove(c);
            if (room == null) return;
            var m = MemberOf(room, c);
            if (m == null) return;
            room.Members.Remove(m);
            for (int i = 0; i < Seats; i++) if (room.SeatList[i] == m.Serial) room.SeatList[i] = Open;
            try { Send(c, 469, LeaveFrame(room, m.Serial)); } catch (Exception) { }
            if ((room.Type & 1) != 0 && RiceServer.Config.BattleTeamColorReset)
            {
                try { RefreshCar(c); } catch (Exception ex) { Log.WriteError("RefreshCar: " + ex.Message); }
            }
            if (!room.Humans.Any())
            {
                // voce saiu: os bots da sala continuam a corrida (ou correm sozinhos, se era desafio e nao largou) e so
                // depois voltam; na sua propria sala ainda sem largar eles saem junto com voce
                foreach (var ai in room.Ais)
                {
                    var bb = Bots.ByName(ai.Name);
                    if (bb == null || bb.IsAlt) continue;      // seus corredores: correm, mas nao ganham nada
                    string note;
                    lock (racingUntil) racingNote.TryGetValue(bb.Cid, out note);
                    if (note != null) EndRacing(bb, note);
                    else if (room.State != Wait || room.ChallengeHost != null)
                    {
                        // continua a corrida sem voce; no fim conta a colocacao (e o premio) e volta
                        double secs = room.State != Wait ? Math.Max(30, RaceSeconds() * 0.6) : RaceSeconds();
                        int count = Math.Max(2, room.Ais.Count());
                        MarkRacing(bb, secs + 30);
                        var b3 = bb;
                        System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(secs)).ContinueWith(_ =>
                        {
                            try { SoloRaceReward(b3, 1 + Rng.Next(count), count); }
                            catch (Exception ex) { Log.WriteError("Corrida sem voce: " + ex.Message); }
                        });
                    }
                    else EndRacing(bb, "Ok, sai da sala com voce. To indo te encontrar!");
                }
                foreach (var sp in room.Spectators.Select(Bots.ByCid).Where(x => x != null)) EndRacing(sp, null);
                room.Spectators.Clear(); room.JustWatch.Clear();
                if (room.Timer != null) room.Timer.Dispose();
                Rooms.Remove(room.Id);
                Log.WriteLine("Sala {0} fechada.", room.Id);
            }
        }

        [RicePacket(485, RiceServer.ServerType.Game)]
        public static void PvpReadyState(RicePacket packet)
        {
            var p = packet.Buffer;
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null || p.Length < 5 || room.State != Wait) return;
                var m = MemberOf(room, packet.Sender);
                if (m == null) return;
                m.Ready = p[4] != 0 || m.Serial == room.Master;
                Cast(room, 485, ReadyFrame(m));
            }
        }

        // ------------------------------------------------------------------ corrida

        [RicePacket(486, RiceServer.ServerType.Game)]
        public static void PvpStartReq(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("PvpStartReq ({0} bytes): {1}", p.Length, BitConverter.ToString(p));
            if (p.Length < 12) return;
            int map = BitConverter.ToInt32(p, 0), flag = BitConverter.ToInt32(p, 4), start = BitConverter.ToInt32(p, 8);
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null)
                {
                    // corrida de treino do tutorial: o proprio jogo monta a licao (nao responder)
                    var me0 = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
                    if (me0 != null && me0.Vehicle != null && me0.Vehicle.IsTraining && map == 11 && flag == 0)
                    {
                        Log.WriteLine("Tutorial: corrida de treino local (o jogo controla).");
                        return;
                    }
                    int area = 20000 + (int) SerialOf(packet.Sender);
                    Send(packet.Sender, 487, LoadFrame(Practice, map, flag, area, start));
                    return;
                }
                var me = MemberOf(room, packet.Sender);
                if (me == null || me.Serial != room.Master || room.State != Wait) return;
                var err = StartLoad(room, map, flag, start);
                if (err != null) Chat.SendSystemMessage(packet.Sender, err);
            }
        }

        // carrega a corrida (botao Iniciar do mestre, ou o dono da sala do desafio de um bot). null = comecou.
        static string StartLoad(Room room, int map, int flag, int start)
        {
            if (map >= 0) room.Map = (ushort) map;

            bool hasAi = room.Ais.Any();
            LoadAi();
            if (hasAi && !aiTracks.Contains(room.Map))
            {
                return "Esta pista nao aceita corredores IA: escolha uma pista com o icone de IA.";
            }
            if (!hasAi && room.Members.Count == 1)
            {
                Log.WriteLine("Sala {0}: treino sozinho no mapa {1}.", room.Id, room.Map);
                CastWeather(room); Cast(room, 487, LoadFrame(Practice, room.Map, flag, room.Area, start));
                return null;
            }

            room.State = Loading;
            room.RankCounter = 0;
            room.FirstEnd = null;
            room.CardsTaken.Clear(); room.CardPicked.Clear();
            room.Gates.Clear(); room.Order.Clear(); room.NextTalk = DateTime.UtcNow.AddSeconds(8);
            room.Racers = room.Humans.Select(m => m.Serial).ToList();
            room.Waiting = new HashSet<uint>(room.Racers);
            foreach (var m in room.Members) { m.Loaded = m.IsAi; m.Ended = false; m.EndType = 0; m.Rank = 0; m.Record = 0; }

            // bots que estavam como jogador viram IA para a corrida
            foreach (var ai in room.Ais.Where(a => a.AsPlayer).ToList()) Redress(room, ai, false);
            TeamColors(room, true);
            // a corrida comecou: os bots da sala somem da porta da Battle Zone (estao correndo)
            foreach (var ai in room.Ais) CityBots.Vanish(Bots.ByName(ai.Name), Math.Max(60, RiceServer.Config.BattleBotRaceSeconds));
            // IAs de corrida anterior: saem e entram de novo sem piloto atribuido.
            foreach (var ai in room.Ais.Where(a => a.Assigned).ToList())
            {
                uint old = ai.Serial;
                Cast(room, 469, LeaveFrame(room, old));
                ai.Assigned = false;
                ReplaceSeat(room, old, ai.Serial);
                Cast(room, 468, JoinFrame(room, ai));
                Cast(room, 485, ReadyFrame(ai));
            }

            Log.WriteLine("Sala {0}: carregando mapa {1} (area {2}), {3} humano(s), {4} IA.", room.Id, room.Map, room.Area, room.Racers.Count, room.Ais.Count());
            CastWeather(room); Cast(room, 487, LoadFrame(0, room.Map, flag, room.Area, start));

            // 2016: logo apos o carregamento, um 625 com todas as IAs (o mestre da sala pilota todas).
            var ais = room.Ais.ToList();
            if (ais.Count > 0)
            {
                foreach (var ai in ais)
                {
                    uint old = ai.Serial;
                    ai.Driver = (int) (room.Master & 0xffff);
                    ai.Assigned = true;
                    ReplaceSeat(room, old, ai.Serial);
                }
                Cast(room, 625, Build(w => { w.Write((uint) ais.Count); foreach (var a in ais) w.Write(AiEntry(a)); }));
            }
            StartTimer(room, 120000, Loading);
            return null;
        }

        static byte[] LoadFrame(int type, int map, int flag, int area, int start)
        {
            return Build(w => { w.Write(type); w.Write(map); w.Write(flag); w.Write(area); w.Write(0f); w.Write(start); w.Write(-1); });
        }

        static void StartTimer(Room room, int ms, int state)
        {
            if (room.Timer != null) room.Timer.Dispose();
            room.Timer = new Timer(_ =>
            {
                lock (Sync)
                {
                    if (!Rooms.ContainsKey(room.Id) || room.State != state) return;
                    room.Waiting.Clear();
                    Progress(room);
                }
            }, null, ms, Timeout.Infinite);
        }

        [RicePacket(488, RiceServer.ServerType.Game)]
        public static void PvpLoadingStatus(RicePacket packet) { }

        [RicePacket(489, RiceServer.ServerType.Game)]
        public static void PvpLoadComplete(RicePacket packet)
        {
            var p = packet.Buffer;
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null || room.State != Loading || p.Length < 4) return;
                var m = MemberOf(room, packet.Sender);
                if (m == null) return;
                m.Loaded = true;
                room.StartTime = BitConverter.ToInt32(p, 0);
                room.Waiting.Remove(m.Serial);
                Progress(room);
            }
        }

        static void Progress(Room room)
        {
            if (room.Waiting.Count > 0) return;
            if (room.State == Loading)
            {
                room.State = Racing;
                room.Waiting = new HashSet<uint>(room.Racers.Concat(room.Ais.Select(a => a.Serial)));
                Log.WriteLine("Sala {0}: largada!", room.Id);
                foreach (var a in room.Ais) MarkRacing(Bots.ByName(a.Name), 1800);
                Cast(room, 490, Build(w => { w.Write(room.StartTime); w.Write(19500); w.Write(new byte[32]); }));
                StartTimer(room, 15 * 60 * 1000, Racing);   // limite de seguranca
            }
            else if (room.State == Racing)
            {
                Result(room);
            }
        }

        static Member ByCar(Room room, uint who)
        {
            return room.Ais.FirstOrDefault(a => (uint) a.Key == (who >> 16));
        }

        [RicePacket(493, RiceServer.ServerType.Game)]
        public static void PvpGate(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 20) return;
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null || room.State != Racing) return;
                var me = MemberOf(room, packet.Sender);
                uint who = BitConverter.ToUInt32(p, 0);
                var gateAi = me != null && who != me.Serial ? ByCar(room, who) : null;
                try { Narrate(room, gateAi != null ? 0x80000000u | (uint) gateAi.Key : (me != null ? me.Serial : who)); } catch (Exception ex) { Log.WriteError("Narracao: " + ex.Message); }
                if (gateAi != null)
                {
                    Cast(room, 494, Build(w => { w.Write(who); w.Write(0); }));
                    return;
                }
                float clean = BitConverter.ToSingle(p, 16);
                int reward = 0;
                int roll = Rng.Next(100);
                if (roll < (clean >= 100f ? 35 : 20)) reward = 1 + Rng.Next(6);   // 1..6 (7 = Team Boost)
                Cast(room, 494, Build(w => { w.Write(me != null ? me.Serial : who); w.Write(reward); }));
            }
        }

        [RicePacket(491, RiceServer.ServerType.Game)]
        public static void PvpEnd(RicePacket packet)
        {
            var p = packet.Buffer;
            Log.WriteLine("PvpEnd ({0} bytes)", p.Length);
            if (p.Length < 0x14) return;
            lock (Sync)
            {
                var room = RoomOfClient(packet.Sender);
                if (room == null || room.State != Racing) return;
                var me = MemberOf(room, packet.Sender);
                if (me == null) return;
                uint who = BitConverter.ToUInt32(p, 0);
                var m = me;
                if (who != me.Serial)
                {
                    var ai = ByCar(room, who);
                    if (ai == null) return;
                    m = ai;
                }
                if (m.Ended) return;
                int endType = BitConverter.ToInt32(p, 4), endTime = BitConverter.ToInt32(p, 8);
                // quando voce cruza a chegada o seu jogo da "desistiu" para as IAs que ele pilota: isso encerrava a corrida
                // na hora. Depois do primeiro na chegada, IA que "desiste" continua contando; a corrida so acaba quando
                // todos chegam ou quando a contagem (BattleFinishSeconds) termina - como no jogo.
                if (m.IsAi && endType != Finished && room.FirstEnd != null) { m.LateEnd = true; return; }
                float record = BitConverter.ToSingle(p, 12);
                int rank = 0;
                if (endType == Finished) rank = ++room.RankCounter;
                m.Ended = true; m.EndType = endType; m.EndTime = endTime; m.Record = record; m.Rank = rank;
                Log.WriteLine("Sala {0}: {1} terminou (tipo {2}, tempo {3:0.00}, posicao {4}).", room.Id, m.Name, endType, record, rank);

                var tail = Slice(p, 0x14, 0x4a - 0x14);
                Cast(room, 491, Build(w =>
                {
                    w.Write(m.IsAi ? who : m.Serial); w.Write(endType); w.Write(endTime); w.Write(record); w.Write(rank);
                    w.Write(tail);
                }));
                if (endType == Finished && room.FirstEnd == null)
                {
                    if (RiceServer.Config.BattleSpectators && room.Spectators.Count > 0)
                    {
                        var sp = Bots.ByCid(room.Spectators[Rng.Next(room.Spectators.Count)]);
                        var hh = room.Humans.FirstOrDefault();
                        if (sp != null && hh != null)
                            Bots.SayLater(hh.Client, 300, "room", sp.Name, string.Format(Pick("{0} cruzou a linha em primeiro! Que corrida!", "VITORIA do {0}! Arquibancada em pe!", "E o {0} leva essa! Que corridao!"), m.Name));
                    }
                    room.FirstEnd = endTime;
                    int fin = (int) (Math.Max(3, RiceServer.Config.BattleFinishSeconds) * 1000);
                    Cast(room, 495, BitConverter.GetBytes(endTime + fin));
                    StartTimer(room, fin + 300, Racing);           // a tabela sai assim que a contagem zera
                }
                room.Waiting.Remove(m.Serial);
                Progress(room);
            }
        }

        // Complete Bonus Reward, igual a conta do jogo (DriftCity.exe 0x7e8410/0x7e82c0/0x7e8790, tabela Init\Reward_ExpMito):
        // pelo numero de corredores X: Beginner = Bronze, Pro = Prata (com limite de velocidade, @MapFlag 0x4000) ou Ouro;
        // estrela = a faixa de X; Exp = X x 1,025 x EXP da arena x pontos; Mito = X x 1,8 x EXP x pontos. X = 1 nao tem bonus.
        static readonly int[][] StarRows =
        {
            new[] { 0, 1, 1, 2 }, new[] { 0, 2, 3, 3 }, new[] { 0, 3, 4, 5 }, new[] { 0, 4, 6, 7 }, new[] { 0, 5, 8, 8 },
            new[] { 1, 2, 1, 3 }, new[] { 1, 4, 4, 7 }, new[] { 1, 6, 8, 11 }, new[] { 1, 8, 12, 15 }, new[] { 1, 10, 16, 16 },
            new[] { 2, 3, 1, 3 }, new[] { 2, 6, 4, 7 }, new[] { 2, 9, 8, 11 }, new[] { 2, 12, 12, 15 }, new[] { 2, 15, 16, 16 },
        };
        // exp e mito da Battle Zone (colocacao + Complete Bonus) x BattleRewardScale
        static double BattleScale() { return Math.Max(0, RiceServer.Config.BattleRewardScale); }

        static void CompleteBonus(Room room, int x, out uint exp, out uint mito, out int stars)
        {
            exp = 0; mito = 0; stars = 0;
            if (x <= 1) return;
            int kind, mult;
            if (room.Channel == BeginnerChannel) { kind = 0; mult = 3; }
            else if (room.Channel == ProChannel)
            {
                kind = (room.MapFlag & 0x4000) != 0 ? 1 : 2;
                mult = (room.Type & 0x100000) != 0 ? 16 : (room.Type & 0x80000) != 0 ? 8 : (room.Type & 0x40000) != 0 ? 6 : 4;
            }
            else return;
            int pts = 0, idx = 0;
            foreach (var r in StarRows.Where(r => r[0] == kind))
            {
                idx++;
                if (x >= r[2] && x <= r[3]) { pts = r[1]; stars = idx; }
            }
            if (pts == 0) return;
            exp = (uint) Math.Floor(x * 1.025 * mult * pts + 0.5);
            mito = (uint) Math.Floor(x * 1.8 * mult * pts + 0.5);
        }

        static void Result(Room room)
        {
            if (room.Timer != null) { room.Timer.Dispose(); room.Timer = null; }
            var all = room.Members.Where(m => m.IsAi || room.Racers.Contains(m.Serial)).ToList();
            int n = all.Count;
            var cfg = RiceServer.Config;
            var entries = new List<byte[]>();
            // quem nao terminou (R) tambem precisa de uma colocacao propria depois dos que terminaram: com todos na
            // mesma (n) o jogo escrevia um em cima do outro e a tabela ficava com linhas vazias e so o ultimo "R".
            // O "Record" de quem nao terminou e a distancia ate a chegada ("To Goal").
            int finCount = all.Count(x => x.Ended && x.EndType == Finished && x.Rank > 0);
            int retIdx = 0;
            // corrida de time (tipo com o bit 1): cada um faz pontos pela colocacao (1o = n pontos ... ultimo = 1; quem nao
            // termina = 0) e o placar soma os do time: assento par = azul, impar = vermelho. O jogo le o placar no
            // cabecalho do 492 (@4 azul, @8 vermelho; DriftCity.exe 0x7d6a26 compara os dois) e os pontos de cada um no @0x38.
            bool team = (room.Type & 1) != 0;
            uint blue = 0, red = 0;
            Func<Member, uint> pointsOf = x => x.Ended && x.EndType == Finished && x.Rank > 0 ? (uint) Math.Max(1, n - x.Rank + 1) : 0u;
            Func<Member, bool> isBlue = x => { int st = Array.IndexOf(room.SeatList, x.Serial); return st < 0 || st % 2 == 0; };
            if (team) foreach (var x in all) { if (isBlue(x)) blue += pointsOf(x); else red += pointsOf(x); }
            if (team) Log.WriteLine("Sala {0}: corrida de time - azul {1} x {2} vermelho.", room.Id, blue, red);
            float toGoal = 0;
            // seus corredores na sua party que correram: ganham pela colocacao deles (como voce), sem dividir o seu premio
            var partyAlts = new HashSet<long>(room.Humans.Where(h => h.Char != null).SelectMany(h => BotSocial.PartyBots(h.Char)).Where(b => b.IsAlt).Select(b => b.Cid));
            var altPaid = new HashSet<long>(all.Where(x => x.IsAi).Select(x => Bots.ByName(x.Name)).Where(x => x != null && x.IsAlt && partyAlts.Contains(x.Cid)).Select(x => x.Cid));
            var altLines = new List<string>();
            foreach (var m in all.OrderBy(x => x.Ended && x.EndType == Finished && x.Rank > 0 ? x.Rank : n + 1).ThenBy(x => x.IsAi ? 1 : 0))
            {
                int endType = m.Ended && m.EndType == Finished && m.Rank > 0 ? Finished : Retired;
                int rank = endType == Finished ? m.Rank : n;              // para a recompensa (igual antes)
                int shown = endType == Finished ? m.Rank : finCount + (++retIdx);
                float record = m.Record;
                if (endType != Finished) { toGoal += 120 + Rng.Next(380); record = toGoal; }
                if (m.IsAi)
                {
                    var ab = Bots.ByName(m.Name);
                    var hm = room.Humans.FirstOrDefault();
                    if (ab != null && ab.IsAlt && partyAlts.Contains(ab.Cid) && hm != null && n > 1)
                    {
                        try
                        {
                            var ach = BotSocial.AltCharacter(hm.Client.Player, ab);
                            int aplace = n - rank + 1;
                            long aExp = cfg.BattleExpPerPlace * aplace, aMito = cfg.BattleMitoPerPlace * aplace;
                            if (endType == Finished && cfg.BattleCompleteBonus)
                            {
                                uint abe, abm; int ast;
                                CompleteBonus(room, n, out abe, out abm, out ast);
                                aExp += abe; aMito += abm;
                            }
                            aExp = (long) Math.Round(aExp * BattleScale()); aMito = (long) Math.Round(aMito * BattleScale());
                            if (ach != null)
                            {
                                int before = ach.Level;
                                if (aExp > 0) ach.GrantExperience(aExp);
                                if (aMito > 0) ach.GrantMito((ulong) aMito);
                                ab.Level = ach.Level;
                                altPaid.Add(ab.Cid);
                                Log.WriteLine("Sala {0}: {1} (seu corredor) ganhou +{2} exp e +{3} mito (nivel {4}, mito {5}).", room.Id, ab.Name, aExp, aMito, ach.Level, ach.Mito);
                                BattleRecord.Add((long) ach.CID, team, endType == Finished && (team ? (isBlue(m) ? blue > red : red > blue) : rank == 1));
                                altLines.Add(ab.Name + " (seu corredor) " + (endType == Finished ? shown + "o lugar" : "nao terminou") + ": +" + aExp + " exp, +" + aMito + " mito"
                                             + (ach.Level > before ? " (nivel " + ach.Level + "!)" : "") + ".");
                            }
                        }
                        catch (Exception ex) { Log.WriteError("Corrida (seu corredor): " + ex.Message); }
                    }
                    entries.Add(Build(w =>
                    {
                        w.Write(endType); w.Write(m.Serial); w.Write(0u); w.Write(0UL); w.Write(0u); w.Write(record);
                        w.Write((ushort) shown); w.Write((ushort) m.Level); w.Write(0UL); w.Write(0UL); w.Write(0UL);
                        w.Write(team ? pointsOf(m) : 0u); w.Write(0u); w.Write(new byte[8]);
                    }));
                    continue;
                }
                var c = m.Char;
                int place = n - rank + 1;                 // 1o lugar = n pontos
                uint exp = (uint) Math.Round(n > 1 ? cfg.BattleExpPerPlace * place * BattleScale() : 0);
                uint mito = (uint) Math.Round(n > 1 ? cfg.BattleMitoPerPlace * place * BattleScale() : 0);
                // Complete Bonus Reward (a tabelinha das estrelas): quem termina ganha o bonus do jogo
                if (endType == Finished && cfg.BattleCompleteBonus)
                {
                    uint be, bm; int stars;
                    CompleteBonus(room, n, out be, out bm, out stars);
                    be = (uint) Math.Round(be * BattleScale()); bm = (uint) Math.Round(bm * BattleScale());
                    if (be > 0 || bm > 0)
                    {
                        exp += be; mito += bm;
                        try { Chat.SendSystemMessage(m.Client, "Bonus de corrida completa (" + stars + " estrela(s)): +" + be + " exp e +" + bm + " mito."); } catch (Exception) { }
                    }
                }
                if (room.ChallengeBonus && n > 1)
                {
                    exp += (uint) Math.Max(0, cfg.BotRaceBonusExp); mito += (uint) Math.Max(0, cfg.BotRaceBonusMito);
                    room.ChallengeBonus = false;
                    try { Chat.SendSystemMessage(m.Client, "Desafio aceito: +" + cfg.BotRaceBonusExp + " exp e +" + cfg.BotRaceBonusMito + " mito de bonus."); } catch (Exception) { }
                }
                if (exp > 0) c.GrantExperience(exp);
                if (mito > 0) c.GrantMito(mito);
                BotSocial.ShareRewards(c, exp, mito, altPaid);   // corredores da party que nao correram ganham junto
                if (n > 1) { try { Licenses.CountRace(c, (room.Type & 1) != 0, room.Map); } catch (Exception lex) { Log.WriteError("Licencas (corrida): " + lex.Message); } }
                if (n > 1) BattleRecord.Add((long) c.CID, team, endType == Finished && (team ? (isBlue(m) ? blue > red : red > blue) : rank == 1));
                if (room.DuelBot != 0)
                {
                    long db = room.DuelBot;
                    room.DuelBot = 0;                         // conta uma vez por sala
                    bool won = endType == Finished && rank == 1;
                    if (room.DuelMate != 0)
                    {
                        // dupla: vale se voce ou o bot da sua crew chegou na frente do rival
                        var rb = Bots.ByCid(db); var mb = Bots.ByCid(room.DuelMate);
                        Func<string, int> rk = nm => { var a = room.Ais.FirstOrDefault(x => x.Name == nm); return a != null && a.Ended && a.EndType == Finished && a.Rank > 0 ? a.Rank : 99; };
                        int rr = rb != null ? rk(rb.Name) : 99, mr = mb != null ? rk(mb.Name) : 99, pr = endType == Finished && rank > 0 ? rank : 99;
                        won = Math.Min(pr, mr) < rr;
                        room.DuelMate = 0;
                    }
                    var mc = m.Client; var mch = c; int rid = room.Id;
                    System.Threading.Tasks.Task.Delay(2500).ContinueWith(_ => { try { CrewLadder.DuelResult(mc, mch, db, won); } catch (Exception ex) { Log.WriteError("Mano a mano: " + ex.Message); } });
                    // depois do resultado, quando voce volta para a sala, sai sozinho dela (volta para a lista de salas) e ve o progresso
                    System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(Math.Max(10, RiceServer.Config.DuelAutoLeaveSeconds))).ContinueWith(_ =>
                    {
                        try
                        {
                            lock (Sync)
                            {
                                var r = RoomOfClient(mc);
                                if (r != null && r.Id == rid && r.State == Wait) { LeaveRoom(mc); Log.WriteLine("Mano a mano {0}: {1} saiu da sala sozinho.", rid, mch.Name); }
                            }
                            Chat.SendSystemMessage(mc, "[Escada] " + CrewLadder.Status(mch));
                        }
                        catch (Exception ex) { Log.WriteError("Mano a mano (sair): " + ex.Message); }
                    });
                }
                var ei = c.GetExpInfo();
                Log.WriteLine("Resultado sala {0}: {1} posicao {2}/{3}, +{4} exp, +{5} mito{6}.", room.Id, c.Name, rank, n, exp, mito,
                              VisualShop.ExpMult(c) > 1 || VisualShop.MitoMult(c) > 1
                                  ? string.Format(" (com drinks: exp x{0}, mito x{1})", VisualShop.ExpMult(c), VisualShop.MitoMult(c)) : "");
                if (endType == Finished && n > 1 && rank <= 3) PartyTalk.Praise(c, "corrida");
                entries.Add(Build(w =>
                {
                    w.Write(endType); w.Write(m.Serial); w.Write(mito); w.Write((ulong) c.Mito); w.Write(exp); w.Write(record);
                    w.Write((ushort) shown); w.Write((ushort) c.Level);
                    w.Write((ulong) ei.CurExp); w.Write((ulong) ei.NextExp); w.Write((ulong) ei.BaseExp);
                    w.Write(team ? pointsOf(m) : 0u); w.Write(0u); w.Write(new byte[8]);
                }));
            }
            room.State = Wait;
            room.ResultAt = DateTime.UtcNow;
            room.BackInRoom = false;
            room.AfterRace = true;
            room.Waiting.Clear();
            TeamColors(room, false);
            foreach (var ai in room.Ais)
            {
                CityBots.Vanish(Bots.ByName(ai.Name), 3);   // acabou: voltam a aparecer na porta
                // bot da party: a corrida acabou, volta a te seguir quando voce sair da sala
                int pos = ai.Ended && ai.EndType == Finished && ai.Rank > 0 ? ai.Rank : 0;
                MarkRacing(Bots.ByName(ai.Name), 1800);
                RaceNote(Bots.ByName(ai.Name), pos > 0 ? "Corrida acabou: cheguei em " + pos + "o! Saindo da sala com voce." : "Corrida acabou! Saindo da sala com voce.");
            }
            foreach (var m in room.Members) m.Ready = m.IsAi || m.Serial == room.Master;
            Cast(room, 622, MasterFrame(room));
            Cast(room, 492, Build(w =>
            {
                w.Write(1); w.Write(blue); w.Write(red); w.Write(0f); w.Write(room.FirstEnd ?? 0); w.Write(entries.Count);
                foreach (var e in entries) w.Write(e);
            }));
            var host = room.Members.FirstOrDefault(m => m.Serial == room.Master);
            if (host != null) Cast(room, 485, ReadyFrame(host));
            foreach (var a in room.Ais) Cast(room, 485, ReadyFrame(a));
            foreach (var m in room.Humans)
            {
                try { m.Char.FlushModInfo(m.Client); m.Char.FlushNotices(m.Client); } catch (Exception) { }
                foreach (var al in altLines) { try { Chat.SendSystemMessage(m.Client, al); } catch (Exception) { } }
            }
        }
    }

    // Recorde de corridas na Battle Zone de cada corredor (individual e time, corridas e vitorias): aparece no perfil
    // ("Battle Record"). Salvo em save/<conta>/<corredor>/battle_record.json.
    public static class BattleRecord
    {
        const string FileName = "battle_record.json";
        static readonly object Sync = new object();
        static Dictionary<string, int[]> data;

        static Dictionary<string, int[]> Data()
        {
            if (data != null) return data;
            try { data = CharFiles.Exists(FileName) ? Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int[]>>(CharFiles.ReadAllText(FileName)) : null; }
            catch (Exception ex) { Log.WriteError("battle_record.json: " + ex.Message); data = null; }
            if (data == null) data = new Dictionary<string, int[]>();
            return data;
        }

        // [corridas individuais, vitorias individuais, corridas de time, vitorias de time]
        public static int[] Of(long cid)
        {
            lock (Sync)
            {
                int[] r;
                return Data().TryGetValue(cid + ":pvp", out r) && r != null && r.Length >= 4 ? (int[]) r.Clone() : new int[4];
            }
        }

        public static void Add(long cid, bool team, bool win)
        {
            lock (Sync)
            {
                string k = cid + ":pvp";
                int[] r;
                if (!Data().TryGetValue(k, out r) || r == null || r.Length < 4) r = new int[4];
                int i = team ? 2 : 0;
                r[i]++;
                if (win) r[i + 1]++;
                data[k] = r;
                try { CharFiles.WriteAllText(FileName, Newtonsoft.Json.JsonConvert.SerializeObject(data)); }
                catch (Exception ex) { Log.WriteError("battle_record.json: " + ex.Message); }
            }
        }
    }
}
