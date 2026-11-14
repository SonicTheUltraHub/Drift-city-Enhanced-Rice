using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Bots na rua — formato do cliente 2016 (documentacao do openDC, area.py / misc.py):
    //   - o servidor guarda os movimentos do jogador (0x21D MoveVehicle: XiCarMove 0x3a + XiCarState 0x32);
    //     movimentos com outro serial sao objetos que o cliente dirige (HUV da perseguicao): ficam separados;
    //   - bots da party SEGUEM o jogador e param perto (sem encostar); numa perseguicao de HUV vao atras do HUV;
    //   - outros bots ANDAM pelas faixas da cidade (ultrapassam pelo lado) ou ficam estacionados;
    //   - Driver Dome (area 0): pontos por onde o jogador ja passou ficam salvos (dome_points.json) e os bots
    //     estacionam em posicoes aleatorias entre eles (sempre dentro da area);
    //   - carro de outro jogador: 0x221 MoveVehicleExt (move + state + u32 IP + u16 porta) na conexao de area;
    //     0x226 RemoveVehicle (XiCarMove); 0x23D RequestCar -> 0x23E (XiCarMove);
    //   - nome/crew/visual: 801 PlayerInfoReq -> 809 PlayerInfoRes (XiPlayerInfo 0xd8).
    // XiCarMove 2016: u16 serial @0, u16 idade @2, XiCarAttr @4 (u16, u16 tipo do carro @6, u32 cor @8, ...),
    // u32 tempo @0x14, posicao @0x18 (x, y, z, rotacao em radianos), velocidade @0x28 (4 floats), u16 @0x38.
    // XiCarState @0x3a (0x32): volante, nitro, freio, fumaca... (os bots usam o estado do jogador PARADO).
    public static class CityBots
    {
        const int MoveSize = 0x3a, StateSize = 0x32, FullMove = MoveSize + StateSize;
        const double TrailSeconds = 30;
        const double TickSeconds = 0.25;
        static readonly string DomeFile = Paths.Map("dome_points.json");
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static Timer timer;

        class Sample { public DateTime At; public byte[] Move; public float X, Y; }

        class Driver
        {
            public Rice.Game.CityTraffic.RoadCursor Cur;
            public float Speed;                 // metros por segundo
            public float Side;                  // desvio lateral atual (ultrapassagem)
            public byte[] Last;
        }

        // Neo City: o HUV mais perto de cada bot da party (ele atira de longe, sem ir bater)
        public class NeoTarget { public long Cid; public ushort Number; public float Dist, HX, HY, HZ; public byte[] Move; public DateTime At; }
        public static List<NeoTarget> NeoTargetsOf(Rice.Game.Character ch)
        {
            lock (Sync)
            {
                PlayerState s;
                if (ch == null || !states.TryGetValue(ch.CID, out s)) return new List<NeoTarget>();
                return s.NeoTargets.Values.Where(t => (DateTime.UtcNow - t.At).TotalSeconds < 1.5).ToList();
            }
        }

        // Neo City: HUVs vivos perto de cada bot da party (para os HUVs atacarem os bots)
        public class NeoThreat { public long Cid; public ushort Number; public float Dist, PlayerDist; }
        public static List<NeoThreat> NeoThreats(Rice.Game.Character ch, IList<long> cids, float range)
        {
            var r = new List<NeoThreat>();
            if (ch == null) return r;
            var deadN = NeoCity.DeadNumbers(ch);
            lock (Sync)
            {
                PlayerState s;
                if (!states.TryGetValue(ch.CID, out s) || s.Trail.Count == 0) return r;
                var me = s.Trail[s.Trail.Count - 1];
                foreach (var kv in s.Objects)
                {
                    if (kv.Value.Count == 0) continue;
                    var hl = kv.Value[kv.Value.Count - 1];
                    if ((DateTime.UtcNow - hl.At).TotalSeconds > 20) continue;
                    ushort num = BitConverter.ToUInt16(hl.Move, 4);
                    if (num == 0) num = (ushort) (kv.Key & 0xffff);
                    if (num == 0 || deadN.Contains(num) || hl.At < NeoCity.ReleasedAt(ch, num)) continue;   // morto / ainda nao apareceu
                    float pd = (float) Math.Sqrt((hl.X - me.X) * (hl.X - me.X) + (hl.Y - me.Y) * (hl.Y - me.Y));
                    foreach (var cid in cids)
                    {
                        byte[] at; if (!s.NeoHold.TryGetValue(cid, out at)) s.FollowLast.TryGetValue(cid, out at);
                        if (at == null || at.Length < 0x20) continue;
                        float dx = F(at, 0x18) - hl.X, dy = F(at, 0x1c) - hl.Y, d = (float) Math.Sqrt(dx * dx + dy * dy);
                        if (d <= range) r.Add(new NeoThreat { Cid = cid, Number = num, Dist = d, PlayerDist = pd });
                    }
                }
            }
            return r;
        }

        class PlayerState
        {
            public readonly Dictionary<long, NeoTarget> NeoTargets = new Dictionary<long, NeoTarget>();
            public readonly Dictionary<long, byte[]> NeoHold = new Dictionary<long, byte[]>();   // bot parado mirando no HUV
            public readonly Dictionary<long, DateTime> NeoHoldSeen = new Dictionary<long, DateTime>();
            public int AreaId = -1;
            public readonly List<Sample> Trail = new List<Sample>();
            public readonly Dictionary<uint, List<Sample>> Objects = new Dictionary<uint, List<Sample>>(); // HUVs
            public readonly Dictionary<long, byte[]> Parked = new Dictionary<long, byte[]>();
            public readonly Dictionary<long, Driver> Drivers = new Dictionary<long, Driver>();
            public readonly Dictionary<long, byte[]> FollowLast = new Dictionary<long, byte[]>();
            public readonly HashSet<uint> Shown = new HashSet<uint>();
            public byte[] Idle;                 // estado (XiCarState) do jogador parado
            public DateTime CruiseSince = DateTime.MinValue;   // dirigindo reto desde
            public DateTime Entered = DateTime.UtcNow;
            public bool SpotsMade;
            public List<long> DomeQueue = new List<long>();
            public DateTime ChaseStart = DateTime.MinValue;
            public bool ChaseEngaged;          // voce ja chegou perto do HUV/boss (antes disso a party so te segue)
            public HashSet<long> WasLinked = new HashSet<long>();   // bots que estavam andando junto de outro
            public readonly HashSet<long> WasMeet = new HashSet<long>();   // bots que vieram ate voce para trocar
            public readonly Dictionary<long, float[]> MeetPos = new Dictionary<long, float[]>();   // bot vindo ate voce: onde ele esta
            public readonly Dictionary<long, byte[]> LinkParked = new Dictionary<long, byte[]>();        // onde estava parado antes
            public readonly Dictionary<long, Driver> LinkDriver = new Dictionary<long, Driver>();        // ou andando
            public readonly Dictionary<long, DomeDriver> LinkDome = new Dictionary<long, DomeDriver>();  // ou andando no Dome
            public readonly Dictionary<long, DomeDriver> DomeDrivers = new Dictionary<long, DomeDriver>();   // andando no Driver Dome
            public readonly Dictionary<long, Pursuer> Pursuers = new Dictionary<long, Pursuer>();
            public readonly Dictionary<long, Pursuer> Followers = new Dictionary<long, Pursuer>();   // party seguindo
            public readonly Dictionary<long, Pursuer> Runners = new Dictionary<long, Pursuer>();     // patrulha/entrega: indo a um ponto
            public readonly List<KeyValuePair<string, int>> Reached = new List<KeyValuePair<string, int>>();
            public bool NeoBase;                                                                     // esta na base ROAD (Driver Dome da Neo City)
            public readonly List<uint[]> PendingHits = new List<uint[]>();   // golpes dos bots no HUV (serial, numero)
            public bool InUnderCity;
            public DateTime LastObject = DateTime.MinValue;
            public DateTime StillSince = DateTime.MinValue;
            public Rice.Game.Character Owner;
            public readonly List<Tuple<long, float, ushort>> RushHits = new List<Tuple<long, float, ushort>>();   // bot, dano, numero do HUV
            public DateTime BossPowerAt = DateTime.MinValue;   // Rush: o boss usou o poder (o seu carro pulou)
            public int RushDowns;
            public readonly Dictionary<long, DomeDriver> PartyErrands = new Dictionary<long, DomeDriver>();   // party: bot que foi a uma loja
            public readonly Dictionary<long, DateTime> NextPartyErrand = new Dictionary<long, DateTime>();
            public readonly Dictionary<long, Pursuer> PartyReturn = new Dictionary<long, Pursuer>();
            public readonly Dictionary<long, Pursuer> RaceFrom = new Dictionary<long, Pursuer>();   // onde o bot da party sumiu para correr na Battle Zone
            public readonly HashSet<long> RaceBack = new HashSet<long>();                          // voltando da corrida dirigindo ate voce   // voltando da loja ate o lugar da formacao
            // moradores da cidade (CityWholeCity): viagem ate o posto da Battle Zone, lugar de origem, grupos com sirene
            public readonly Dictionary<long, Trip> Trips = new Dictionary<long, Trip>();
            public readonly Dictionary<long, byte[]> Origins = new Dictionary<long, byte[]>();
            public readonly HashSet<long> SirenBots = new HashSet<long>();
            public readonly HashSet<int> AppliedActs = new HashSet<int>();
            public readonly Dictionary<long, DateTime> GroupUntil = new Dictionary<long, DateTime>();
            public readonly Dictionary<long, float> BaseSpeed = new Dictionary<long, float>();
            public DateTime NextGroup = DateTime.UtcNow.AddSeconds(40);
            public int CityId;                 // cidade em que os bots foram montados (mudou: monta de novo)              // Rush: quantos HUVs ja cairam (mudou: a party volta a te seguir ate o proximo)
        }

        // Bot andando no Driver Dome pelos lugares por onde voce passou (domePoints, na ordem em que voce andou)
        class DomeDriver
        {
            public int Target, Step = 1;
            public float X, Y, Z, Dir, Speed, Max;
            public int Shop = -1;                       // indo para um lugar (indice em Places: lojas e saidas)
            public bool GoShop;                         // ja perto: vai direto na porta
            public DateTime InsideUntil = DateTime.MinValue;   // dentro da loja / fora do lobby (some) ate
            public DateTime ArriveAt = DateTime.MinValue;      // parou na porta em
            public Action Errand;                       // o que faz quando chega (comprar no leilao, trocar de carro...)
            public double Stay;                         // segundos la dentro (0 = sorteado)
            public byte[] Home;                         // estava estacionado aqui: volta e estaciona de novo
            public bool ReturnHome, Done;
            public bool AtShop;                         // parado na porta da loja (aparece) ate InsideUntil
            public bool Swap;                           // saiu para a cidade: outro bot que estava fora entra no lugar dele
            public bool Party;                          // bot da sua party que foi a uma loja: quando sai, volta a te seguir
            public readonly Queue<int> Recent = new Queue<int>();
            public byte[] Last;
        }

        // Bot da party perseguindo um HUV: tem posicao e velocidade proprias (anda como carro, sem teleporte).
        class Pursuer
        {
            public float X, Y, Z, Dir, Speed;
            public uint Huv;
            public bool Parked;                           // seguindo: parado ao seu lado esperando voce andar
            public DateTime StoppedAt = DateTime.MinValue;
            public DateTime FarSince = DateTime.MinValue;   // seguindo: longe do lugar desde
            public DateTime LastContact = DateTime.UtcNow;  // HUV: ultima vez que encostou (ou teleportou)
            public DateTime SlowUntil = DateTime.MinValue;  // Rush: bateu no boss, fica devagar ate
            public DateTime JumpAt = DateTime.MinValue;     // Rush: poder do boss jogou o bot para cima em
            public int Point = -1;                          // missao: lugar que esta indo pegar
            public List<float[]> Route; public int RouteIdx; // missao: caminho pelas ruas ate o ponto
            public DateTime Rebalance = DateTime.MinValue;   // Rush: ultima vez que conferiu a divisao entre os HUVs
            public float Side;                              // replay: desvio lateral atual
            public bool OnHuv;                              // replay: ja esta no caminho do HUV
            public Sample LastSm; public float LX, LY, LZ;   // replay: ultimo ponto usado (nunca volta para tras)
            public DateTime ChaseSince = DateTime.MinValue;
            public DateTime PointSince = DateTime.UtcNow;
            public byte[] Hold;                             // seguindo: parado na formacao (ate voce se afastar PartyHoldRadius m)
        }

        static readonly Dictionary<ulong, PlayerState> states = new Dictionary<ulong, PlayerState>();
        static int objLogs;
        static readonly HashSet<string> objSeen = new HashSet<string>();
        // Driver Dome normal e a base ROAD da Neo City (o jogo trata a base como um Driver Dome, area 0, com a cidade 6):
        // cada um com os seus pontos. curNeo diz qual vale agora (marcado antes de usar, na mesma thread).
        static List<float[]> normalDome, neoDome = new List<float[]>();
        [ThreadStatic] static bool curNeo;
        static List<float[]> domePoints { get { return curNeo ? neoDome : normalDome; } set { normalDome = value; } }
        static readonly string NeoDomeFile = Paths.Map("neo_dome_points.json");
        static bool IsNeoBase(Rice.Game.Character ch, int area) { return area == 0 && ch != null && ch.InNeoBase && RiceServer.Config.NeoBots; }
        static DateTime domeSaved = DateTime.MinValue;

        public static void Start()
        {
            if (timer != null) return;
            BotChatter.Start();
            PartyTalk.Start();
            PartyGrowth.Start();
            CrewLadder.Start();
            LoadDome();
            timer = new Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("CityBots: " + ex.Message); } },
                              null, 1000, (int) (TickSeconds * 1000));
            Log.WriteLine("Bots na rua ligados ({0} pontos do Driver Dome conhecidos).", domePoints.Count);
        }

        static void LoadDome()
        {
            try { domePoints = File.Exists(DomeFile) ? JsonConvert.DeserializeObject<List<float[]>>(File.ReadAllText(DomeFile)) : null; }
            catch (Exception) { domePoints = null; }
            if (domePoints == null || domePoints.Count == 0)
            {
                // pontos do Driver Dome que vem com o projeto (1115 lugares por onde o autor andou)
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var st = asm.GetManifestResourceStream("dome_points.default.json"))
                        if (st != null)
                            using (var rd = new StreamReader(st, Encoding.UTF8))
                                domePoints = JsonConvert.DeserializeObject<List<float[]>>(rd.ReadToEnd());
                }
                catch (Exception ex) { Log.WriteError("dome_points.default.json: " + ex.Message); }
            }
            if (domePoints == null) domePoints = new List<float[]>();
            try { neoDome = File.Exists(NeoDomeFile) ? JsonConvert.DeserializeObject<List<float[]>>(File.ReadAllText(NeoDomeFile)) : null; }
            catch (Exception) { neoDome = null; }
            if (neoDome == null || neoDome.Count == 0)
            {
                // caminho da base ROAD que vem com o projeto (1409 pontos por onde o dono do projeto andou)
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var st = asm.GetManifestResourceStream("neo_dome_points.default.json"))
                        if (st != null)
                            using (var rd = new StreamReader(st, Encoding.UTF8))
                                neoDome = JsonConvert.DeserializeObject<List<float[]>>(rd.ReadToEnd());
                }
                catch (Exception ex) { Log.WriteError("neo_dome_points.default.json: " + ex.Message); }
            }
            if (neoDome == null) neoDome = new List<float[]>();
        }

        static void SaveDome()
        {
            try { File.WriteAllText(curNeo ? NeoDomeFile : DomeFile, JsonConvert.SerializeObject(domePoints)); domeSaved = DateTime.UtcNow; }
            catch (Exception ex) { Log.WriteError("dome_points.json: " + ex.Message); }
        }

        static PlayerState StateOf(Rice.Game.Character ch)
        {
            PlayerState s;
            if (!states.TryGetValue(ch.CID, out s)) { s = new PlayerState(); states[ch.CID] = s; }
            return s;
        }

        static float F(byte[] m, int at) { return BitConverter.ToSingle(m, at); }
        static void SetF(byte[] m, int at, float v) { BitConverter.GetBytes(v).CopyTo(m, at); }

        static float Heading(float dir)
        {
            switch (RiceServer.Config.CityBotHeadingMode)
            {
                case 1: return -dir;
                case 2: return dir - (float) (Math.PI / 2);
                case 3: return (float) (Math.PI / 2) - dir;
                default: return dir;
            }
        }

        // Chamado pelo 0x21D (Area.MoveVehicle).
        public static void OnPlayerMove(Rice.Game.Character ch, byte[] payload)
        {
            if (ch == null || payload == null || payload.Length < FullMove || !RiceServer.Config.CityBotsEnabled) return;
            int area = ch.Area != null ? (ch.InNeoBase ? 0 : ch.Area.ID) : -1;   // base ROAD: funciona como um Driver Dome
            uint serial = BitConverter.ToUInt16(payload, 0);
            ushort objNo = BitConverter.ToUInt16(payload, 4);
            // base ROAD e pequena (ate ~350 m do centro do mapa): o seu carro bem longe disso = e uma cidade de verdade
            if (ch.InNeoBase && serial == ch.Serial && objNo == 0)
            {
                float px = BitConverter.ToSingle(payload, 0x18), py = BitConverter.ToSingle(payload, 0x1c);
                if (px * px + py * py > 700f * 700f) { ch.InNeoBase = false; Log.WriteLine("Rua: {0} saiu da base ROAD (posicao de cidade).", ch.Name); }
            }
            lock (Sync)
            {
                var s = StateOf(ch);
                if (s.AreaId != area) Reset(s, area);
                var copy = new byte[FullMove];
                Array.Copy(payload, copy, FullMove);
                var smp = new Sample { At = DateTime.UtcNow, Move = copy, X = F(copy, 0x18), Y = F(copy, 0x1c) };
                var cut = DateTime.UtcNow.AddSeconds(-TrailSeconds);

                ushort carType = BitConverter.ToUInt16(payload, 6);
                var myCar = ch.Vehicle;
                bool mine = (ch.Serial == 0 || serial == ch.Serial) && (objNo == 0 || (myCar != null && carType == (ushort) myCar.CarType));
                if (!mine)
                {
                    if (objLogs < 12)
                    {
                        string sig = serial + "/" + objNo + "/" + carType;
                        if (objSeen.Add(sig))
                        {
                            objLogs++;
                            Log.WriteLine("Objeto no seu cliente: serial {0} (o seu {1}), numero {2}, tipo {3} (seu carro {4}).",
                                          serial, ch.Serial, objNo, carType, myCar != null ? myCar.CarType : 0);
                        }
                    }
                    if (serial == ch.Serial || ch.Serial == 0) serial = 0x10000u + objNo;   // HUV com o seu serial
                    // objeto dirigido pelo cliente (HUV): nao entra no rastro do jogador
                    List<Sample> list;
                    if (!s.Objects.TryGetValue(serial, out list)) s.Objects[serial] = list = new List<Sample>();
                    list.Add(smp);
                    s.LastObject = DateTime.UtcNow;
                    var keep = DateTime.UtcNow.AddSeconds(-20);
                    int o = list.FindIndex(x => x.At >= keep);
                    if (o > 0) list.RemoveRange(0, o);
                    return;
                }

                s.Trail.Add(smp);
                int old = s.Trail.FindIndex(x => x.At >= cut);
                if (old > 0) s.Trail.RemoveRange(0, old);

                float vx = F(copy, 0x28), vy = F(copy, 0x2c);
                if (vx * vx + vy * vy >= 0.25f) s.StillSince = DateTime.MinValue;
                else if (s.StillSince == DateTime.MinValue) s.StillSince = DateTime.UtcNow;
                // estado neutro: so depois de 2 s parado (sem a fumaca do drift/arrancada que ainda sai)
                if (s.StillSince != DateTime.MinValue && (DateTime.UtcNow - s.StillSince).TotalSeconds > 2
                    && (DateTime.UtcNow - s.LastObject).TotalSeconds > 5)
                {
                    s.Idle = new byte[StateSize];
                    Array.Copy(copy, MoveSize, s.Idle, 0, StateSize);
                }
                // dirigindo reto (sem derrapar) ha 1,5 s: o estado de rodas girando que os bots usam andando
                if (copy.Length >= FullMove)
                {
                    float sp = (float) Math.Sqrt(vx * vx + vy * vy);
                    float sl = sp > 0.1f ? Math.Abs(Wrap(Unheading(F(copy, 0x24)) - (float) Math.Atan2(vy, vx))) : 9f;
                    if (sp >= 8f && sl < 0.06f)
                    {
                        if (s.CruiseSince == DateTime.MinValue) s.CruiseSince = DateTime.UtcNow;
                        else if ((DateTime.UtcNow - s.CruiseSince).TotalSeconds > 1.5)
                        {
                            var cs = new byte[StateHp];
                            Array.Copy(copy, MoveSize, cs, 0, StateHp);
                            cruise = cs;
                        }
                    }
                    else s.CruiseSince = DateTime.MinValue;
                }

                curNeo = IsNeoBase(ch, area);
                if (area == 0 && (!curNeo || RiceServer.Config.NeoDomeLearn) && domePoints != null && domePoints.Count < RiceServer.Config.DomeLearnLimit
                    && domePoints.All(p => (p[0] - smp.X) * (p[0] - smp.X) + (p[1] - smp.Y) * (p[1] - smp.Y) > 36f))
                {
                    domePoints.Add(new[] { smp.X, smp.Y, F(copy, 0x20), F(copy, 0x24) });
                    if ((DateTime.UtcNow - domeSaved).TotalSeconds > 20) SaveDome();
                }
            }
        }

        static void Reset(PlayerState s, int area)
        {
            s.AreaId = area;
            s.Trail.Clear();
            s.Objects.Clear();
            s.Parked.Clear();
            s.Drivers.Clear();
            s.DomeDrivers.Clear();
            s.FollowLast.Clear();
            s.Shown.Clear();
            s.Runners.Clear();
            s.SpotsMade = false;
            s.DomeQueue.Clear();
            s.Pursuers.Clear();
            s.Followers.Clear();
            foreach (var g in s.GroupUntil.Keys) Bots.Ungroup(g);
            s.Trips.Clear(); s.Origins.Clear(); s.SirenBots.Clear(); s.GroupUntil.Clear(); s.BaseSpeed.Clear(); s.AppliedActs.Clear();
            s.NextGroup = DateTime.UtcNow.AddSeconds(40);
            s.Entered = DateTime.UtcNow;
        }

        static bool IsDome(int area) { return area == 0; }
        static bool IsCity(int area) { return area >= 0 && area < 100; }

        // Bot da party perto do HUV que ele persegue (a Undercity usa para os golpes dos bots)
        public class NearInfo { public long Cid; public uint Serial; public ushort Number; public float Dist; }

        public static List<NearInfo> PursuersNear(Rice.Game.Character ch, float radius)
        {
            var list = new List<NearInfo>();
            if (ch == null) return list;
            lock (Sync)
            {
                PlayerState s;
                if (!states.TryGetValue(ch.CID, out s)) return list;
                foreach (var kv in s.Pursuers)
                {
                    List<Sample> trail;
                    if (!s.Objects.TryGetValue(kv.Value.Huv, out trail) || trail.Count == 0) continue;
                    var h = trail[trail.Count - 1];
                    if ((DateTime.UtcNow - h.At).TotalSeconds > Math.Max(2, RiceServer.Config.PartyChaseKeepSeconds)) continue;
                    float dx = h.X - kv.Value.X, dy = h.Y - kv.Value.Y;
                    float d = (float) Math.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    list.Add(new NearInfo { Cid = kv.Key, Serial = kv.Value.Huv, Number = BitConverter.ToUInt16(h.Move, 4), Dist = d });
                }
            }
            return list;
        }

        static void Tick()
        {
            if (!RiceServer.Config.CityBotsEnabled) return;
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                var c = p != null ? p.AreaClient : null;
                if (ch == null || c == null) continue;
                var moves = new List<byte[]>();
                var gone = new List<uint>();
                var fresh = new List<uint>();
                var hits = new List<uint[]>();
                var reached = new List<KeyValuePair<string, int>>();
                var rushHits = new List<Tuple<long, float, ushort>>();
                var points = Missions.OpenPoints(ch);       // patrulha/entrega em andamento: pontos que faltam
                lock (Sync)
                {
                    var s = StateOf(ch);
                    s.Owner = ch;
                    int area = ch.Area != null ? (ch.InNeoBase ? 0 : ch.Area.ID) : -1;   // base ROAD: funciona como um Driver Dome
                    curNeo = IsNeoBase(ch, area);
                    if (s.AreaId != area || (IsCity(area) && !IsDome(area) && s.CityId != 0 && s.CityId != ch.City) || (IsDome(area) && s.NeoBase != curNeo))
                    { Reset(s, area); s.CityId = ch.City; s.NeoBase = curNeo; continue; }
                    if (s.CityId == 0) s.CityId = ch.City;
                    bool uc = UnderCity.InRun(ch);       // Undercity: so a sua party vai junto
                    s.InUnderCity = uc;
                    if (NeoCity.IsNeoArea(area)) uc = true;   // Neo City: tambem so a sua party (sem moradores, vendedores, trafego)
                    if ((!IsCity(area) && !uc) || s.Trail.Count == 0) continue;
                    var last = s.Trail[s.Trail.Count - 1];
                    uint nowTime = BitConverter.ToUInt32(last.Move, 0x14) + (uint) (DateTime.UtcNow - last.At).TotalMilliseconds;
                    var want = new HashSet<uint>();
                    var party = BotSocial.PartyBots(ch);
                    var realParty = new HashSet<long>(party.Select(x => x.Cid));
                    // Rush Time: os corredores que entraram com voce andam junto (seguem e vao no boss)
                    foreach (var rb in Rush.BotsOf(ch)) if (!party.Contains(rb)) party.Add(rb);

                    // HUVs ativos (atualizados nos ultimos 2 s)
                    double huvKeep = Math.Max(2, RiceServer.Config.PartyChaseKeepSeconds);
                    var huvs = s.Objects.Where(kv => kv.Value.Count > 0 && (DateTime.UtcNow - kv.Value[kv.Value.Count - 1].At).TotalSeconds < huvKeep)
                                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                    // Neo City: o mapa inteiro tem HUVs andando (o seu jogo manda todos). A party so vai nos que estao perto de
                    // voce (NeoBotEngageRange m); senao ela ficava pulando de um lado para o outro do mapa atras de cada um.
                    if (NeoCity.IsNeoArea(area) && huvs.Count > 0)
                    {
                        var me0 = s.Trail[s.Trail.Count - 1];
                        float er = Math.Max(20f, RiceServer.Config.NeoBotEngageRange);
                        foreach (var k in huvs.Keys.ToList())
                        {
                            var hl = huvs[k][huvs[k].Count - 1];
                            float ddx = hl.X - me0.X, ddy = hl.Y - me0.Y;
                            if (ddx * ddx + ddy * ddy > er * er) huvs.Remove(k);
                        }
                        foreach (var pq in s.Pursuers.Values) if (pq.Huv != 0 && !huvs.ContainsKey(pq.Huv)) pq.Huv = 0;
                    }
                    // Neo City: a party sai de perto de voce e vai atras dos HUVs perto (um para cada, os menos perseguidos
                    // primeiro), mas para a NeoBotStandoff m dele e atira de la (nao vai bater)
                    Dictionary<uint, List<Sample>> neoHuvs = null;
                    var neoPts = new List<float[]>();
                    if (NeoCity.IsNeoArea(area))
                    {
                        // HUV ja destruido sai da lista (o rastro dele ficava uns segundos e os bots ficavam presos nele)
                        var deadN = NeoCity.DeadNumbers(ch);
                        foreach (var k in huvs.Keys.ToList())
                        {
                            ushort so = BitConverter.ToUInt16(huvs[k][huvs[k].Count - 1].Move, 4);
                            if (so == 0) so = (ushort) (k & 0xffff);
                            if (deadN.Contains(so)) huvs.Remove(k);
                        }
                        foreach (var pq in s.Pursuers.Values) if (pq.Huv != 0 && !huvs.ContainsKey(pq.Huv)) pq.Huv = 0;
                        // HUV parado / que acabou de aparecer: so vira alvo depois que o SEU jogo ja colocou ele no mapa (mexeu
                        // nele depois de liberado) - antes os bots atiravam no ponto de respawn com o HUV ainda nascendo
                        var me1 = s.Trail[s.Trail.Count - 1];
                        float er1 = Math.Max(20f, RiceServer.Config.NeoBotEngageRange);
                        foreach (var tp in NeoCity.TargetPoints(ch))
                        {
                            ushort tn = (ushort) tp[0];
                            DateTime rel = NeoCity.ReleasedAt(ch, tn).AddSeconds(1.5);
                            Sample got = null;
                            foreach (var ob in s.Objects)
                            {
                                if (ob.Value.Count == 0) continue;
                                var ol = ob.Value[ob.Value.Count - 1];
                                ushort on = BitConverter.ToUInt16(ol.Move, 4);
                                if (on == 0) on = (ushort) (ob.Key & 0xffff);
                                if (on == tn && ol.At >= rel) { got = ol; break; }
                            }
                            if (got == null) continue;
                            float qx = got.X - me1.X, qy = got.Y - me1.Y;
                            if (qx * qx + qy * qy <= er1 * er1) neoPts.Add(new[] { (float) tn, got.X, got.Y, F(got.Move, 0x20) });
                        }
                        // HUV ainda nascendo (liberado ha menos de 1,5 s): nao e alvo
                        foreach (var k in huvs.Keys.ToList())
                        {
                            var hl0 = huvs[k][huvs[k].Count - 1];
                            ushort so0 = BitConverter.ToUInt16(hl0.Move, 4);
                            if (so0 == 0) so0 = (ushort) (k & 0xffff);
                            if (hl0.At < NeoCity.ReleasedAt(ch, so0).AddSeconds(1.5)) huvs.Remove(k);
                        }
                        neoHuvs = huvs;
                        s.NeoTargets.Clear();
                        if (huvs.Count > 0) s.ChaseEngaged = true;      // HUV perto de voce: ja parte para cima
                    }

                    // Rush: HUV com vida 0 sai da lista (os bots largam dele); caiu um: a party volta a te seguir ate voce
                    // chegar perto dos que faltam (ou o bot que ja estiver perto de um vai direto nele)
                    bool rushOn = Rush.Running(ch);
                    if (rushOn)
                    {
                        var dead = Rush.DeadSorts(ch);
                        if (dead.Count > 0)
                            foreach (var k in huvs.Keys.ToList())
                            {
                                var hl = huvs[k][huvs[k].Count - 1];
                                ushort so = BitConverter.ToUInt16(hl.Move, 4);
                                if (so == 0) so = (ushort) (k & 0xffff);
                                if (dead.Contains(so)) huvs.Remove(k);
                            }
                        if (dead.Count != s.RushDowns)
                        {
                            s.RushDowns = dead.Count; s.ChaseEngaged = false;
                            foreach (var pq in s.Pursuers.Values) pq.Huv = 0;
                        }
                    }
                    // sirene: o estado do seu carro durante a perseguicao (com a sirene ligada) vira o dos grupos de HUV dos bots
                    if (huvs.Count > 0 && s.ChaseEngaged && sirenState == null)
                    {
                        sirenState = new byte[StateSize];
                        Array.Copy(last.Move, MoveSize, sirenState, 0, StateSize);
                    }
                    if (huvs.Count == 0) { s.ChaseStart = DateTime.MinValue; s.Pursuers.Clear(); s.ChaseEngaged = false; }
                    else if (s.ChaseStart == DateTime.MinValue) s.ChaseStart = DateTime.UtcNow;
                    // a party vai com voce ate o HUV/boss: so parte para cima quando voce chegar a PartyChaseJoinDistance m dele
                    if (huvs.Count > 0 && !s.ChaseEngaged)
                    {
                        var meNow = s.Trail[s.Trail.Count - 1];
                        float near = float.MaxValue;
                        foreach (var hv in huvs.Values)
                        {
                            var hl = hv[hv.Count - 1];
                            float ddx = hl.X - meNow.X, ddy = hl.Y - meNow.Y;
                            near = Math.Min(near, (float) Math.Sqrt(ddx * ddx + ddy * ddy));
                        }
                        if (near <= Math.Max(10f, RiceServer.Config.PartyChaseJoinDistance)) s.ChaseEngaged = true;
                    }
                    for (int i = 0; i < party.Count; i++)
                    {
                        byte[] m = null;
                        bool rushRacer = !realParty.Contains(party[i].Cid);
                        // aceitou corrida na Battle Zone: some de onde estava ate a corrida acabar; depois reaparece no mesmo
                        // lugar e vem dirigindo ate voce
                        long pcid = party[i].Cid;
                        if (!rushRacer && Battle.IsRacing(pcid))
                        {
                            if (!s.RaceFrom.ContainsKey(pcid))
                            {
                                byte[] lm;
                                var rfrom = new Pursuer();
                                if (s.FollowLast.TryGetValue(pcid, out lm) && lm != null && lm.Length >= 0x28)
                                { rfrom.X = F(lm, 0x18); rfrom.Y = F(lm, 0x1c); rfrom.Z = F(lm, 0x20); rfrom.Dir = Unheading(F(lm, 0x24)); }
                                else
                                { var t0 = s.Trail[0]; rfrom.X = t0.X; rfrom.Y = t0.Y; rfrom.Z = F(t0.Move, 0x20); rfrom.Dir = Unheading(F(t0.Move, 0x24)); }
                                rfrom.Huv = (uint) Math.Max(0, area);   // area onde sumiu
                                s.RaceFrom[pcid] = rfrom;
                            }
                            s.Followers.Remove(pcid); s.PartyReturn.Remove(pcid); s.RaceBack.Remove(pcid);
                            continue;
                        }
                        Pursuer rf;
                        if (s.RaceFrom.TryGetValue(pcid, out rf))
                        {
                            s.RaceFrom.Remove(pcid);
                            if ((int) rf.Huv != area)
                            {   // voce mudou de area: aparece um pouco atras de voce (no seu caminho)
                                var t0 = s.Trail[0];
                                rf = new Pursuer { X = t0.X, Y = t0.Y, Z = F(t0.Move, 0x20), Dir = Unheading(F(t0.Move, 0x24)) };
                            }
                            rf.Speed = 0; rf.Huv = 0;
                            s.PartyReturn[pcid] = rf;
                            s.RaceBack.Add(pcid);
                            s.Followers.Remove(pcid);
                        }
                        bool raceBack = s.RaceBack.Contains(pcid);
                        // Neo City: voce ficou a mais de NeoBotTeleportDistance m do bot (ele ficou preso atras de um HUV, num
                        // canto...): ele aparece de novo atras de voce, larga o HUV e volta a te seguir
                        bool neoTp = false;
                        if (NeoCity.IsNeoArea(area) && RiceServer.Config.NeoBotTeleportDistance > 0)
                        {
                            byte[] at; if (!s.NeoHold.TryGetValue(pcid, out at)) s.FollowLast.TryGetValue(pcid, out at);
                            if (at != null && at.Length >= 0x20)
                            {
                                float tx = F(at, 0x18) - last.X, ty = F(at, 0x1c) - last.Y, td = RiceServer.Config.NeoBotTeleportDistance;
                                if (tx * tx + ty * ty > td * td)
                                {
                                    s.FollowLast.Remove(pcid); s.Followers.Remove(pcid); s.Pursuers.Remove(pcid);
                                    s.Runners.Remove(pcid); s.NeoHold.Remove(pcid); s.NeoHoldSeen.Remove(pcid); s.NeoTargets.Remove(pcid);
                                    neoTp = true;
                                    Log.WriteLine("Neo City: {0} ficou a {1:0} m de voce e apareceu atras de voce.", party[i].Name, Math.Sqrt(tx * tx + ty * ty));
                                }
                            }
                        }
                        if (raceBack)
                        {
                            m = PartyReturnMove(s, party[i], i, nowTime);
                            if (m == null) { s.RaceBack.Remove(pcid); raceBack = false; }
                        }
                        // Rush: corredor que esperava no lugar do Rush fica parado la ate o boss aparecer
                        byte[] wpk;
                        if (rushRacer && Rush.Counting(ch) && s.Parked.TryGetValue(party[i].Cid, out wpk))
                        {
                            m = WithIdle(s, BotMove(wpk, party[i], nowTime, true));
                            s.FollowLast[party[i].Cid] = wpk;
                            moves.Add(m); want.Add(BotSocial.SerialOf(party[i]));
                            continue;
                        }
                        // Driver Dome: de vez em quando um bot da party vai a uma loja, faz as coisas dele e volta (avisa na party)
                        if (raceBack) { }
                        else if (IsDome(area) && huvs.Count == 0 && points.Count == 0 && !uc)
                        {
                            m = PartyReturnMove(s, party[i], i, nowTime);
                            if (m == null) m = PartyErrandMove(s, party[i], nowTime, last);
                        }
                        else s.PartyReturn.Remove(party[i].Cid);
                        if (!raceBack && huvs.Count == 0 && points.Count > 0 && !uc)
                        {
                            m = MissionMove(s, party[i], i, points, nowTime);
                            if (m != null) s.Followers.Remove(party[i].Cid);
                        }
                        else s.Runners.Remove(party[i].Cid);
                        bool replay = RiceServer.Config.PartyFollowReplay && !rushRacer;   // quem veio do lugar do Rush sai dirigindo de la
                        bool engaged = s.ChaseEngaged || (rushOn && huvs.Count > 0 && NearHuv(s, party[i], huvs, (float) RiceServer.Config.RushBotNearHuv));
                        if (m == null && huvs.Count > 0 && RiceServer.Config.PartyChaseHuv && !engaged) s.Pursuers.Remove(party[i].Cid);
                        if (m == null && huvs.Count > 0 && RiceServer.Config.PartyChaseHuv && engaged && !neoTp)
                        {
                            if (RiceServer.Config.PartyChaseReplay) m = ChaseReplay(s, party[i], i, huvs, nowTime);
                            else
                            {
                                m = ChaseMove(s, party[i], i, huvs, nowTime);
                                s.Followers.Remove(party[i].Cid);      // ao voltar a seguir, parte de onde parou
                            }
                        }
                        if (m == null) m = replay ? FollowReplay(s, party[i], i, nowTime) : FollowMove(s, party[i], i, nowTime);
                        if (m == null) continue;
                        KeepAway(m, last, RiceServer.Config.PartyKeepAway);   // nao encosta em voce (nao te empurra)
                        if (NeoCity.IsNeoArea(area))
                        {
                            if (NeoCity.IsDown(party[i].Cid)) continue;          // destruido pelos HUVs: some ate voltar
                            if (neoHuvs != null && (neoHuvs.Count > 0 || neoPts.Count > 0))
                            {
                                byte[] hold; s.NeoHold.TryGetValue(party[i].Cid, out hold);
                                float bx = F(hold ?? m, 0x18), by = F(hold ?? m, 0x1c), hr = Math.Max(10f, RiceServer.Config.NeoBotHitRange);
                                NeoTarget best = null;
                                foreach (var kv in neoHuvs)
                                {
                                    var hl = kv.Value[kv.Value.Count - 1];
                                    float age = (float) Math.Min(1.0, (DateTime.UtcNow - hl.At).TotalSeconds);
                                    float hx = hl.X + F(hl.Move, 0x28) * age, hy = hl.Y + F(hl.Move, 0x2c) * age;
                                    float d = (float) Math.Sqrt((hx - bx) * (hx - bx) + (hy - by) * (hy - by));
                                    ushort num = BitConverter.ToUInt16(hl.Move, 4);
                                    if (num == 0) num = (ushort) (kv.Key & 0xffff);
                                    if (d <= hr && (best == null || d < best.Dist))
                                        best = new NeoTarget { Cid = party[i].Cid, Number = num, Dist = d, HX = hx, HY = hy, HZ = F(hl.Move, 0x20), At = DateTime.UtcNow };
                                }
                                // HUV/HUB que acabou de aparecer (ainda nao andou) ou parado: pelo ponto onde apareceu
                                var seen = new HashSet<ushort>(neoHuvs.Values.Select(v => BitConverter.ToUInt16(v[v.Count - 1].Move, 4)));
                                foreach (var tp in neoPts)
                                {
                                    ushort num = (ushort) tp[0];
                                    if (seen.Contains(num)) continue;
                                    float d = (float) Math.Sqrt((tp[1] - bx) * (tp[1] - bx) + (tp[2] - by) * (tp[2] - by));
                                    if (d <= hr && (best == null || d < best.Dist))
                                        best = new NeoTarget { Cid = party[i].Cid, Number = num, Dist = d, HX = tp[1], HY = tp[2], HZ = tp[3], At = DateTime.UtcNow };
                                }
                                // com arma e com alvo: para o carro onde esta, virado para o HUV, e atira (nao fica te seguindo)
                                // vai atras do HUV na velocidade normal (e ja atira no caminho); so para (mirando) quando chega a
                                // NeoBotHoldDistance; parado, so larga se o HUV passar de 1,6x isso ou sumir por 2,5 s
                                DateTime lastSeen;
                                double hd = Math.Max(10.0, RiceServer.Config.NeoBotHoldDistance);
                                bool keepHold;
                                if (best != null) keepHold = best.Dist <= (hold != null ? hd * 1.6 : hd);
                                else keepHold = hold != null && s.NeoHoldSeen.TryGetValue(party[i].Cid, out lastSeen) && (DateTime.UtcNow - lastSeen).TotalSeconds < 2.5;
                                if (best != null && keepHold) s.NeoHoldSeen[party[i].Cid] = DateTime.UtcNow;
                                if (keepHold && RiceServer.Config.NeoBotHoldToShoot && NeoCity.HasWeapon(party[i]))
                                {
                                    if (hold == null) { hold = (byte[]) m.Clone(); s.NeoHold[party[i].Cid] = hold; }
                                    float qx = F(hold, 0x18) - last.X, qy = F(hold, 0x1c) - last.Y, er2 = Math.Max(20f, RiceServer.Config.NeoBotEngageRange);
                                    if (qx * qx + qy * qy > er2 * er2) s.NeoHold.Remove(party[i].Cid);   // voce foi longe: larga e te segue
                                    else
                                    {
                                        var hm = (byte[]) hold.Clone();
                                        Array.Copy(m, 0x14, hm, 0x14, 4);
                                        if (best != null) SetF(hm, 0x24, Heading((float) Math.Atan2(best.HY - F(hm, 0x1c), best.HX - F(hm, 0x18))));
                                        hold[0x24] = hm[0x24]; hold[0x25] = hm[0x25]; hold[0x26] = hm[0x26]; hold[0x27] = hm[0x27];
                                        SetF(hm, 0x28, 0f); SetF(hm, 0x2c, 0f); SetF(hm, 0x30, 0f);
                                        m = hm;
                                        Pursuer hp;
                                        if (s.Pursuers.TryGetValue(party[i].Cid, out hp)) { hp.X = F(hm, 0x18); hp.Y = F(hm, 0x1c); hp.Speed = 0; }
                                    }
                                }
                                else s.NeoHold.Remove(party[i].Cid);
                                if (best != null) { best.Move = (byte[]) m.Clone(); s.NeoTargets[party[i].Cid] = best; }
                            }
                            else s.NeoHold.Remove(party[i].Cid);
                        }
                        else s.NeoHold.Remove(party[i].Cid);
                        moves.Add(m);
                        want.Add(BotSocial.SerialOf(party[i]));
                    }

                    if (!uc && (DateTime.UtcNow - s.Entered).TotalSeconds > 4)
                    {
                        if (!s.SpotsMade) MakeSpots(ch, s, party);
                        if (!IsDome(area)) RushWaitSpots(ch, s, realParty);
                        if (IsDome(area)) GrowDomeSpots(s, party);
                        float range = RiceServer.Config.CityBotViewRange;
                        // bots ligados a outro (andando juntos / indo ate o outro para trocar): saem do lugar deles
                        var linked = new List<KeyValuePair<long, Bots.Link>>();
                        foreach (var lk in Bots.Links())
                        {
                            var fb = Bots.ByCid(lk.Key);
                            if (fb == null || party.Contains(fb) || !BotSocial.Online(fb)) continue;
                            if (lk.Value.Leader != 0 && !s.Parked.ContainsKey(lk.Value.Leader) && !s.Drivers.ContainsKey(lk.Value.Leader)
                                && !s.DomeDrivers.ContainsKey(lk.Value.Leader) && !s.Trips.ContainsKey(lk.Value.Leader)) continue;
                            // guarda onde ele estava, para voltar quando acabar (troca fechou / grupo acabou)
                            byte[] keep; Driver kd; DomeDriver kdd;
                            if (s.Parked.TryGetValue(lk.Key, out keep)) { s.LinkParked[lk.Key] = keep; s.FollowLast[lk.Key] = keep; s.Parked.Remove(lk.Key); }
                            if (s.Drivers.TryGetValue(lk.Key, out kd)) { s.LinkDriver[lk.Key] = kd; s.Drivers.Remove(lk.Key); }
                            if (s.DomeDrivers.TryGetValue(lk.Key, out kdd)) { s.LinkDome[lk.Key] = kdd; s.DomeDrivers.Remove(lk.Key); }
                            if (lk.Value.Meet) s.WasMeet.Add(lk.Key);
                            linked.Add(lk);
                        }
                        // acabou: troca -> volta para o lugar de origem; andar junto -> estaciona onde esta
                        foreach (var cid in s.WasLinked.Where(x => !linked.Any(l => l.Key == x)).ToList())
                        {
                            var pb = Bots.ByCid(cid);
                            byte[] org; Driver od; DomeDriver odd;
                            bool hadP = s.LinkParked.TryGetValue(cid, out org), hadD = s.LinkDriver.TryGetValue(cid, out od), hadDd = s.LinkDome.TryGetValue(cid, out odd);
                            s.LinkParked.Remove(cid); s.LinkDriver.Remove(cid); s.LinkDome.Remove(cid); s.MeetPos.Remove(cid);
                            if (pb == null || party.Contains(pb)) continue;
                            s.WasMeet.Remove(cid);
                            if (hadD) { s.Drivers[cid] = od; continue; }
                            if (hadDd) { s.DomeDrivers[cid] = odd; continue; }
                            if (hadP) { s.Parked[cid] = org; continue; }
                            // veio de longe so para a troca (nao estava na sua tela): nao fica parado na sua frente
                            if (s.WasMeet.Remove(cid)) { s.FollowLast.Remove(cid); continue; }
                            byte[] lastM;
                            if (!s.FollowLast.TryGetValue(cid, out lastM)) continue;
                            var pk2 = (byte[]) lastM.Clone(); Array.Clear(pk2, 0x28, 0x10);
                            s.Parked[cid] = pk2;
                        }
                        s.WasLinked = new HashSet<long>(linked.Select(l => l.Key));
                        foreach (var kv in s.Parked)
                        {
                            var b = Bots.ByCid(kv.Key);
                            if (b == null || party.Contains(b)) continue;
                            if (Dist2(kv.Value, last.X, last.Y) > range * range) continue;
                            moves.Add(WithIdle(s, BotMove(kv.Value, b, nowTime, true)));
                            want.Add(BotSocial.SerialOf(b));
                        }
                        foreach (var kv in s.Drivers.ToList())
                        {
                            var b = Bots.ByCid(kv.Key);
                            if (b == null || party.Contains(b)) { s.Drivers.Remove(kv.Key); continue; }
                            var m = DriveMove(ch, s, b, kv.Value, nowTime, last);
                            if (m == null) continue;
                            if (Dist2(m, last.X, last.Y) > range * range) continue;
                            moves.Add(m);
                            want.Add(BotSocial.SerialOf(b));
                        }
                        foreach (var kv in s.DomeDrivers.ToList())
                        {
                            var b = Bots.ByCid(kv.Key);
                            if (b == null || party.Contains(b)) { s.DomeDrivers.Remove(kv.Key); continue; }
                            var m = DomeMove(s, b, kv.Value, nowTime, last);
                            if (kv.Value.Swap)
                            {
                                // saiu para a cidade: quem estava fora do lobby entra (alternando)
                                kv.Value.Swap = false;
                                var inLobby = new HashSet<long>(s.Parked.Keys.Concat(s.Drivers.Keys).Concat(s.DomeDrivers.Keys));
                                var nb = Bots.All().Where(x => !x.Special && !Bots.IsAce(x) && !x.IsAlt && (curNeo ? x.NeoBot : !x.NeoBot) && !party.Contains(x) && BotSocial.Online(x) && !inLobby.Contains(x.Cid)
                                                               && Bots.LinkOf(x.Cid) == null).OrderBy(x => Rng.Next()).FirstOrDefault();
                                if (nb != null)
                                {
                                    s.DomeDrivers.Remove(kv.Key);
                                    s.DomeDrivers[nb.Cid] = kv.Value;
                                    Log.WriteLine("Rua: {0} saiu do lobby para a cidade; {1} entrou.", b.Name, nb.Name);
                                    continue;
                                }
                            }
                            if (kv.Value.Done)
                            {
                                // voltou para o lugar dele: estaciona de novo
                                s.DomeDrivers.Remove(kv.Key);
                                if (kv.Value.Home != null) s.Parked[kv.Key] = kv.Value.Home;
                                continue;
                            }
                            if (m == null) continue;
                            if (Dist2(m, last.X, last.Y) > range * range) continue;
                            moves.Add(m);
                            want.Add(BotSocial.SerialOf(b));
                        }
                        foreach (var lk in linked)
                        {
                            var fb = Bots.ByCid(lk.Key);
                            var m = LinkMove(s, fb, lk.Value, nowTime);
                            if (m == null) continue;
                            if (Dist2(m, last.X, last.Y) > range * range) continue;
                            moves.Add(m);
                            want.Add(BotSocial.SerialOf(fb));
                        }
                        if (RiceServer.Config.CityWholeCity && !IsDome(area))
                        {
                            try { CityLife(ch, s, party, nowTime, last, moves, want, range); }
                            catch (Exception ex) { Log.WriteError("Moradores da cidade: " + ex.Message); }
                        }
                    }

                    // quem trocou de carro: sai da tela por um instante para voltar com o carro novo
                    foreach (var rs in respawn.Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds < 1.0).Select(kv => kv.Key).ToList())
                        if (s.Shown.Contains(rs)) { want.Remove(rs); moves.RemoveAll(mv => BitConverter.ToUInt16(mv, 0) == (ushort) rs); }
                    foreach (var old in respawn.Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds > 5).Select(kv => kv.Key).ToList()) respawn.Remove(old);
                    foreach (var serial in s.Shown.Where(x => !want.Contains(x)).ToList())
                    {
                        gone.Add(serial);
                        s.Shown.Remove(serial);
                    }
                    foreach (var w in want) if (s.Shown.Add(w)) fresh.Add(w);
                    // Neo City e base ROAD: cada bot com a vida dele no estado do carro (o estado vinha copiado do seu)
                    // (os da sua party em qualquer lugar: a lista da party mostra a vida deles, nao a sua)
                    foreach (var mv in moves)
                    {
                        var bb = BotSocial.BySerial(BitConverter.ToUInt16(mv, 0));
                        if (bb != null && (NeoCity.IsNeoArea(area) || curNeo || realParty.Contains(bb.Cid))) NeoCity.ApplyBotState(mv, bb, curNeo || !NeoCity.IsNeoArea(area));
                    }
                    hits.AddRange(s.PendingHits);
                    s.PendingHits.Clear();
                    if (points.Count == 0) s.Runners.Clear();
                    reached.AddRange(s.Reached);
                    s.Reached.Clear();
                    rushHits.AddRange(s.RushHits);
                    s.RushHits.Clear();
                }
                try
                {
                    foreach (var m in moves)
                    {
                        var pk = new RicePacket(0x221);
                        pk.Writer.Write(m, 0, FullMove);
                        pk.Writer.Write(0u);
                        pk.Writer.Write((ushort) 0);
                        c.Send(pk);
                    }
                    foreach (var hz in hits) SendHuvHit(p, ch, hz[0], (ushort) hz[1]);
                    foreach (var r in reached) Missions.BotReached(p, r.Key, r.Value);
                    foreach (var rh in rushHits) Rush.BotHit(ch, Bots.ByCid(rh.Item1), rh.Item2, rh.Item3);
                    // bot que apareceu agora: manda as licencas montadas (806). Sem isso o minimapa do jogo le licenca 0
                    // ("DrawVehicleNameNonTeam idx error -7000" no GameLog)
                    foreach (var fs in fresh) { var fb = BotSocial.BySerial(fs); if (fb != null) Licenses.SendBotMounted(c, fb); }
                    foreach (var serial in gone)
                    {
                        var pk = new RicePacket(0x226);
                        var mv = new byte[MoveSize];
                        BitConverter.GetBytes((ushort) serial).CopyTo(mv, 0);
                        pk.Writer.Write(mv);
                        c.Send(pk);
                    }
                }
                catch (Exception ex) { Log.WriteError("CityBots envio: " + ex.Message); }
            }
        }

        // 187 ChaseHit: u16 quem bateu, u16 dono do HUV (voce), u16 numero do HUV, f32 dano, resto zerado (44 bytes)
        static int hitLogs;
        static void SendHuvHit(Rice.Game.Player p, Rice.Game.Character ch, uint botSerial, ushort sort)
        {
            var gc = p.GameClient;
            if (gc == null) return;
            float dmg = Chase.BotHitDamage(gc, sort);
            if (dmg <= 0) return;
            var hb = BotSocial.BySerial(botSerial);
            if (RiceServer.Config.BotRealDamage && hb != null) dmg = BotSocial.HitDamage(hb);   // dano normal do carro do bot
            dmg *= (float) Licenses.BotHuvFactor(hb);                                // HUV Hunter bate mais forte
            if (hb != null && !RiceServer.Config.BotRealDamage) dmg *= 0.7f + BotSocial.CarStats(hb)[2] / 400f;
            if (hb != null) dmg *= (float) Math.Max(0.5, Math.Min(3.0, hb.Skill));   // bot fixo bom de HUV (Joaopvbr) bate mais
            var pk = new RicePacket(187);
            pk.Writer.Write((ushort) botSerial);
            pk.Writer.Write(ch.Serial);
            pk.Writer.Write(sort);
            pk.Writer.Write(dmg);
            pk.Writer.Write(new byte[0x16 - 10]);
            var bb = BotSocial.BySerial(botSerial);
            var nm = new byte[44 - 0x16];
            var ne = Encoding.Unicode.GetBytes(bb != null ? bb.Name : "");
            Array.Copy(ne, nm, Math.Min(ne.Length, nm.Length - 2));
            pk.Writer.Write(nm);
            gc.Send(pk);
            if (hitLogs++ < 8) Log.WriteLine("Party: bot {0:X} bateu no HUV {1} (dano {2:0.0}).", botSerial, sort, dmg);
        }

        static float Dist2(byte[] m, float x, float y)
        {
            float dx = F(m, 0x18) - x, dy = F(m, 0x1c) - y;
            return dx * dx + dy * dy;
        }

        // Estado neutro (jogador parado: sem volante, nitro, freio ou fumaca).
        // O estado do jogador parado ainda trazia o volante virado e a fumaca de arrancada/burnout (os bots imitavam o
        // seu carro). Agora a parte de comandos/efeitos (@0..@0x27) vai zerada e so a vida (@0x28..) vem do seu estado.
        const int StateHp = 0x28;
        static byte[] cruise;                  // estado do seu carro dirigindo reto (rodas girando) para os bots andando
        static byte[] WithIdle(PlayerState s, byte[] m)
        {
            Array.Clear(m, MoveSize, StateSize);
            if (s.Idle != null && RiceServer.Config.BotsCopyMyState) Array.Copy(s.Idle, 0, m, MoveSize, StateSize);
            else if (s.Idle != null) Array.Copy(s.Idle, StateHp, m, MoveSize + StateHp, StateSize - StateHp);
            return m;
        }

        // ---- party: seguir dirigindo (sem teleporte), frear antes de voce, parar de lado ------------------------
        // Cada bot tem posicao, direcao e velocidade proprias. Andando: vai pelo seu rastro (mira um ponto a frente
        // no caminho) e controla a velocidade pela distancia ate voce; se voce frear, ele freia antes (distancia de
        // frenagem). Voce parado: ele faz uma curva para um dos lados e para ao seu lado/atras, sem encostar.
        static byte[] FollowMove(PlayerState s, Bots.BotProfile b, int i, uint nowTime)
        {
            var cfg = RiceServer.Config;
            const float dt = (float) TickSeconds;
            var last = s.Trail[s.Trail.Count - 1];
            float px = last.X, py = last.Y;
            float pvx = F(last.Move, 0x28), pvy = F(last.Move, 0x2c);
            float pv = (float) Math.Sqrt(pvx * pvx + pvy * pvy);
            if ((DateTime.UtcNow - last.At).TotalSeconds > 1.5) pv = 0;           // sem movimento novo: parado
            float fwdx, fwdy;
            if (pv > 0.5f) { fwdx = pvx / pv; fwdy = pvy / pv; }
            else { float pd = Unheading(F(last.Move, 0x24)); fwdx = (float) Math.Cos(pd); fwdy = (float) Math.Sin(pd); }
            float lx = -fwdy, ly = fwdx;                                           // esquerda do seu carro
            float min = Math.Max(1.5f, cfg.CityBotMinDistance);

            // parado: formacao (1o e 2o atras e de lado; 3o atras do 1o, 4o atras do 2o).
            // andando: fila, um atras do outro.
            float gap = cfg.CityBotFollowGap + 0.5f;
            bool moving = pv > 3f;
            float back = moving ? gap * (i + 1) : gap * (1 + i / 2);
            back = Math.Max(min + 1f, back);
            float side = moving ? 0f : (cfg.CityBotSideGap + 0.5f) * (i % 2 == 0 ? 1f : -1f);
            float sx = px - fwdx * back + lx * side, sy = py - fwdy * back + ly * side;

            Pursuer f;
            if (!s.Followers.TryGetValue(b.Cid, out f))
            {
                byte[] from;
                if (s.FollowLast.TryGetValue(b.Cid, out from) && Math.Abs(F(from, 0x18) - px) + Math.Abs(F(from, 0x1c) - py) < 300f)
                    f = new Pursuer { X = F(from, 0x18), Y = F(from, 0x1c), Z = F(from, 0x20), Dir = Unheading(F(from, 0x24)), Speed = 0 };
                else
                    f = new Pursuer { X = sx, Y = sy, Z = F(last.Move, 0x20), Dir = (float) Math.Atan2(fwdy, fwdx), Speed = pv };
                s.Followers[b.Cid] = f;
            }

            float dx = px - f.X, dy = py - f.Y;
            float d = (float) Math.Sqrt(dx * dx + dy * dy);
            float ex = sx - f.X, ey = sy - f.Y;
            float dS = (float) Math.Sqrt(ex * ex + ey * ey);
            var cst = BotSocial.CarStats(b);                                       // speed, accel, durab., boost do carro
            float vmax = 28f + cst[0] * 0.16f + Licenses.BotSpeedBonus(b);
            float accUp = 6f + cst[1] * 0.04f;
            float bfx = (float) Math.Cos(f.Dir), bfy = (float) Math.Sin(f.Dir);
            float tx, ty, desired;
            bool stopped = pv < 0.8f;

            // muito longe (voce teleportou, trocou de area...): pula para o lugar dele
            if (d > 150f) { Place(f, sx, sy, fwdx, fwdy, pv); dS = 0; d = back; }

            if (dS > 30f)
            {
                // longe do lugar: vai pelo seu rastro (pela estrada) ate alcancar
                f.Parked = false;
                float off = NearestDist(s.Trail, f.X, f.Y);
                float look = off > 4f ? 3f : Math.Max(4f, Math.Min(8f, f.Speed * 0.5f));
                PathPoint(s.Trail, f.X, f.Y, look, out tx, out ty);
                desired = Math.Min(vmax, pv + 0.6f * dS);
                if (f.FarSince == DateTime.MinValue) f.FarSince = DateTime.UtcNow;
                if ((DateTime.UtcNow - f.FarSince).TotalSeconds > Math.Max(3, cfg.CityBotFarSnapSeconds)) { Place(f, sx, sy, fwdx, fwdy, pv); f.FarSince = DateTime.MinValue; }
            }
            else
            {
                f.FarSince = DateTime.MinValue;
                // quanto ele esta atras do lugar dele (na sua direcao)
                float behindSlot = ex * fwdx + ey * fwdy;
                float slotAhead = ex * bfx + ey * bfy;                  // o lugar esta na frente do bot?
                if (!stopped)
                {
                    f.Parked = false; f.StoppedAt = DateTime.MinValue;
                    // mira um pouco a frente do lugar: o carro fica alinhado com o seu
                    float lead = Math.Max(3f, pv * 0.6f);
                    tx = sx + fwdx * lead; ty = sy + fwdy * lead;
                    desired = pv + Math.Max(-pv, Math.Min(12f, behindSlot * 1.1f));
                }
                else
                {
                    if (f.StoppedAt == DateTime.MinValue) f.StoppedAt = DateTime.UtcNow;
                    tx = sx; ty = sy;
                    if (f.Parked || dS < 1.0f || (slotAhead < 0 && dS < 6f))
                    {
                        // chegou (ou o lugar ficou para tras dele): para, sem dar volta
                        desired = 0f;
                        tx = f.X + bfx * 5f; ty = f.Y + bfy * 5f;
                        if (f.Speed < 0.3f) f.Parked = true;
                    }
                    else desired = Math.Min(12f, (float) Math.Sqrt(2 * 6f * Math.Max(0f, dS - 0.5f)));
                    // nao se arrumou em alguns segundos (ou ficou colado em voce): pula para o lugar
                    double sfor = (DateTime.UtcNow - f.StoppedAt).TotalSeconds;
                    if (cfg.CityBotSnapSeconds > 0 && sfor > cfg.CityBotSnapSeconds && (!f.Parked || d < min))
                    {
                        Place(f, sx, sy, fwdx, fwdy, 0f);
                        f.Parked = true;
                        desired = 0f; tx = f.X + fwdx * 5f; ty = f.Y + fwdy * 5f;
                    }
                }
                // alvo atras do bot e perto: nao vira (nada de 360), so diminui
                float tdx0 = tx - f.X, tdy0 = ty - f.Y;
                if (tdx0 * bfx + tdy0 * bfy < 0 && tdx0 * tdx0 + tdy0 * tdy0 < 100f && !f.Parked)
                {
                    desired = Math.Min(desired, stopped ? 0f : pv * 0.8f);
                    tx = f.X + fwdx * 8f + lx * side * 0.5f; ty = f.Y + fwdy * 8f + ly * side * 0.5f;
                }
            }
            desired = Math.Max(0f, Math.Min(vmax, desired));

            float acc = desired > f.Speed ? accUp : 22f;
            if (Math.Abs(desired - f.Speed) <= acc * dt) f.Speed = desired; else f.Speed += Math.Sign(desired - f.Speed) * acc * dt;
            Steer(f, tx, ty, dt);
            f.X += (float) Math.Cos(f.Dir) * f.Speed * dt;
            f.Y += (float) Math.Sin(f.Dir) * f.Speed * dt;

            // colado em voce: abre para o lado aos poucos (sem parar, sem pular)
            float ox = f.X - px, oy = f.Y - py, od = (float) Math.Sqrt(ox * ox + oy * oy);
            if (od < min)
            {
                if (od < 0.01f) { ox = lx; oy = ly; od = 1f; }
                f.X += ox / od * 0.35f; f.Y += oy / od * 0.35f;
            }
            f.Z = NearestZ(s.Trail, f.X, f.Y, F(last.Move, 0x20));

            var m = BotMove(last.Move, b, nowTime, false);
            ApplyDriveState(s, m, f.X, f.Y, f.Speed);
            SetF(m, 0x18, f.X); SetF(m, 0x1c, f.Y); SetF(m, 0x20, f.Z); SetF(m, 0x24, Heading(f.Dir + Slip(s, f.X, f.Y, f.Speed)));
            SetF(m, 0x28, (float) Math.Cos(f.Dir) * f.Speed); SetF(m, 0x2c, (float) Math.Sin(f.Dir) * f.Speed);
            SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            s.FollowLast[b.Cid] = m;
            return m;
        }

        // ---- party: replay com atraso ---------------------------------------------------------------------------
        // Cada membro repete os seus movimentos (posicao, direcao, drift, fumaca) com atraso: o 1o PartyFollowDelay s
        // atras, o 2o o dobro... sem chegar a menos de (gap x posicao) metros de voce pelo caminho. Voce parado: eles
        // param em fila atras de voce e abrem um pouco para o lado. No HUV: seguem voce ate chegar no caminho do HUV
        // e dai repetem o caminho dele (encostam no ataque); sem chegar em PartyChaseTeleportSeconds, pulam nele.

        // ponto do rastro 'delay' s atras, nunca a menos de 'minBack' m (pelo caminho) do fim. clamped: preso no limite.
        static bool ReplayPoint(List<Sample> trail, double delay, float minBack, out Sample sm, out float x, out float y, out float z, out bool clamped)
        {
            sm = null; x = y = z = 0; clamped = false;
            if (trail == null || trail.Count == 0) return false;
            var target = DateTime.UtcNow.AddSeconds(-delay);
            int k = trail.Count - 1;
            while (k > 0 && trail[k].At > target) k--;
            int lim = trail.Count - 1; float acc = 0;
            while (lim > 0 && acc < minBack)
            {
                float ex = trail[lim].X - trail[lim - 1].X, ey = trail[lim].Y - trail[lim - 1].Y;
                acc += (float) Math.Sqrt(ex * ex + ey * ey);
                lim--;
            }
            if (k >= lim) { k = lim; clamped = true; }
            sm = trail[k];
            x = sm.X; y = sm.Y; z = F(sm.Move, 0x20);
            if (!clamped && k + 1 < trail.Count)
            {
                var n = trail[k + 1];
                double span = (n.At - sm.At).TotalSeconds;
                if (span > 0.001)
                {
                    float t = (float) Math.Max(0, Math.Min(1, (target - sm.At).TotalSeconds / span));
                    x += (n.X - sm.X) * t; y += (n.Y - sm.Y) * t; z += (F(n.Move, 0x20) - z) * t;
                }
            }
            return true;
        }

        static Pursuer ReplayState(PlayerState s, Bots.BotProfile b, float x, float y, float z)
        {
            Pursuer p;
            if (!s.Followers.TryGetValue(b.Cid, out p))
            {
                byte[] from;
                if (s.FollowLast.TryGetValue(b.Cid, out from) && Math.Abs(F(from, 0x18) - x) + Math.Abs(F(from, 0x1c) - y) < 60f)
                    p = new Pursuer { X = F(from, 0x18), Y = F(from, 0x1c), Z = F(from, 0x20) };
                else p = new Pursuer { X = x, Y = y, Z = z };
                s.Followers[b.Cid] = p;
            }
            return p;
        }

        // leva o bot ate o ponto do replay (suave; longe demais = pula) e monta o movimento com o do ponto
        static byte[] ReplayDrive(PlayerState s, Bots.BotProfile b, Pursuer p, Sample sm, float x, float y, float z, float side,
                                  bool stop, bool ownState, uint nowTime)
        {
            float hd = Unheading(F(sm.Move, 0x24));
            float lx = -(float) Math.Sin(hd), ly = (float) Math.Cos(hd);
            float step = 2.5f * (float) TickSeconds;                       // abre para o lado devagar
            if (Math.Abs(p.Side - side) <= step) p.Side = side; else p.Side += Math.Sign(side - p.Side) * step;
            float tx = x + lx * p.Side, ty = y + ly * p.Side;
            float dx = tx - p.X, dy = ty - p.Y;
            if (dx * dx + dy * dy > 40f * 40f) { p.X = tx; p.Y = ty; p.Z = z; }
            else { p.X += dx * 0.55f; p.Y += dy * 0.55f; p.Z += (z - p.Z) * 0.55f; }

            var m = BotMove(sm.Move, b, nowTime, false);
            if (!ownState || stop) WithIdle(s, m);                          // HUV/parado: sem a fumaca/sirene dele
            SetF(m, 0x18, p.X); SetF(m, 0x1c, p.Y); SetF(m, 0x20, p.Z);
            if (stop) { SetF(m, 0x28, 0f); SetF(m, 0x2c, 0f); SetF(m, 0x30, 0f); SetF(m, 0x34, 0f); }
            p.Speed = stop ? 0f : (float) Math.Sqrt(F(m, 0x28) * F(m, 0x28) + F(m, 0x2c) * F(m, 0x2c));
            p.Dir = hd;
            s.FollowLast[b.Cid] = m;
            return m;
        }

        static byte[] FollowReplay(PlayerState s, Bots.BotProfile b, int i, uint nowTime, bool chasing = false)
        {
            var cfg = RiceServer.Config;
            var last = s.Trail[s.Trail.Count - 1];
            float pvx = F(last.Move, 0x28), pvy = F(last.Move, 0x2c);
            bool still = pvx * pvx + pvy * pvy < 0.64f || (DateTime.UtcNow - last.At).TotalSeconds > 1.5;
            float gap = cfg.CityBotFollowGap + 0.5f;
            Sample sm; float x, y, z; bool clamped;
            if (!ReplayPoint(s.Trail, Math.Max(0.2, cfg.PartyFollowDelay) * (i + 1), gap * (i + 1), out sm, out x, out y, out z, out clamped)) return null;
            var p = ReplayState(s, b, x, y, z);
            if (!chasing) { p.OnHuv = false; p.ChaseSince = DateTime.MinValue; }
            // parado na formacao: fica ali ate voce sair de PartyHoldRadius m dele
            if (p.Hold != null)
            {
                float ox = F(p.Hold, 0x18) - last.X, oy = F(p.Hold, 0x1c) - last.Y;
                if (!chasing && ox * ox + oy * oy <= cfg.PartyHoldRadius * cfg.PartyHoldRadius)
                {
                    var hm = BotMove(p.Hold, b, nowTime, true);
                    WithIdle(s, hm);
                    s.FollowLast[b.Cid] = hm;
                    return hm;
                }
                p.Hold = null;
            }
            // nunca volta no rastro (o 1o da fila ia e voltava quando o limite de distancia mudava de ponto)
            if (p.LastSm != null && sm.At < p.LastSm.At && (p.LastSm.At - sm.At).TotalSeconds < 10 && s.Trail.Contains(p.LastSm))
            { sm = p.LastSm; x = p.LX; y = p.LY; z = p.LZ; clamped = true; }
            else { p.LastSm = sm; p.LX = x; p.LY = y; p.LZ = z; }
            bool stop = clamped && still;
            var rm = ReplayDrive(s, b, p, sm, x, y, z, 0f, stop, true, nowTime);   // fica na fila (sem ir para o lado)
            if (rm != null && !chasing && stop && s.StillSince != DateTime.MinValue && (DateTime.UtcNow - s.StillSince).TotalSeconds > 0.8)
            {
                float vx = F(rm, 0x28), vy = F(rm, 0x2c);
                if (vx * vx + vy * vy < 1f) p.Hold = (byte[]) rm.Clone();      // chegou no lugar da formacao: trava ali
            }
            return rm;
        }

        static byte[] ChaseReplay(PlayerState s, Bots.BotProfile b, int i, Dictionary<uint, List<Sample>> huvs, uint nowTime)
        {
            var cfg = RiceServer.Config;
            var last = s.Trail[s.Trail.Count - 1];
            var p = ReplayState(s, b, last.X, last.Y, F(last.Move, 0x20));
            s.Pursuers[b.Cid] = p;
            if (p.ChaseSince == DateTime.MinValue) { p.ChaseSince = DateTime.UtcNow; p.OnHuv = false; }
            if (!huvs.ContainsKey(p.Huv))
            {
                // um HUV para cada (sorteado entre os menos perseguidos)
                p.Huv = huvs.Keys.OrderBy(k => s.Pursuers.Values.Count(o => o != p && o.Huv == k)).ThenBy(k => Rng.Next()).First();
                p.OnHuv = false; p.ChaseSince = DateTime.UtcNow; p.LastContact = DateTime.UtcNow;
            }
            var ht = huvs[p.Huv];
            if (!p.OnHuv)
            {
                // ainda seguindo voce: entra no caminho do HUV quando passar perto dele (ou pula depois de um tempo)
                bool near = NearestDist(ht, p.X, p.Y) < 15f;
                bool late = cfg.PartyChaseTeleportSeconds > 0 && (DateTime.UtcNow - p.ChaseSince).TotalSeconds > cfg.PartyChaseTeleportSeconds;
                if (!near && !late)
                {
                    return FollowReplay(s, b, i, nowTime, true);
                }
                p.OnHuv = true; p.LastContact = DateTime.UtcNow;
            }
            double cycle = Math.Max(1, cfg.PartyChaseCharge + cfg.PartyChaseAttack);
            double tt = ((DateTime.UtcNow - s.ChaseStart).TotalSeconds + i * 1.3) % cycle;
            bool attack = tt >= cfg.PartyChaseCharge;
            int same = s.Pursuers.Count(kv => kv.Value.Huv == p.Huv && kv.Key < b.Cid);    // posicao na fila desse HUV
            double delay = attack ? 0.12 : 0.9 + 0.45 * same;
            Sample hs; float hx, hy, hz; bool cl;
            if (!ReplayPoint(ht, delay, attack ? 0f : 4f, out hs, out hx, out hy, out hz, out cl)) return null;
            // sem encostar por muito tempo: pula para tras do HUV (o contador reinicia)
            if (cfg.PartyChaseTeleportSeconds > 0 && (DateTime.UtcNow - p.LastContact).TotalSeconds > cfg.PartyChaseTeleportSeconds)
            {
                p.X = hx; p.Y = hy; p.Z = hz; p.LastContact = DateTime.UtcNow;
            }
            float side = (attack ? 0.6f : 1.4f) * (same % 2 == 0 ? 1f : -1f);
            var m = ReplayDrive(s, b, p, hs, hx, hy, hz, side, false, false, nowTime);

            // encostou no HUV: golpe
            var h0 = ht[ht.Count - 1];
            float age = (float) Math.Min(1.0, (DateTime.UtcNow - h0.At).TotalSeconds);
            float cx = h0.X + F(h0.Move, 0x28) * age - p.X, cy = h0.Y + F(h0.Move, 0x2c) * age - p.Y;
            float dHuv = (float) Math.Sqrt(cx * cx + cy * cy);
            if (dHuv <= Math.Max(0.5f, cfg.PartyHuvContact)) { p.LastContact = DateTime.UtcNow; TryHuvHit(s, b, p.Huv, h0, dHuv); }
            return m;
        }

        static void Place(Pursuer f, float x, float y, float fx, float fy, float speed)
        {
            f.X = x; f.Y = y; f.Dir = (float) Math.Atan2(fy, fx); f.Speed = speed;
        }

        static float Wrap(float a)
        {
            while (a > Math.PI) a -= (float) (2 * Math.PI);
            while (a < -Math.PI) a += (float) (2 * Math.PI);
            return a;
        }

        // inverso de Heading (rotacao do pacote -> angulo do mundo)
        static float Unheading(float h)
        {
            switch (RiceServer.Config.CityBotHeadingMode)
            {
                case 1: return -h;
                case 2: return h + (float) (Math.PI / 2);
                case 3: return (float) (Math.PI / 2) - h;
                default: return h;
            }
        }

        // distancia do ponto (cx, cy) ao segmento (ax, ay)-(bx, by)
        static float SegmentDist(float ax, float ay, float bx, float by, float cx, float cy)
        {
            float vx = bx - ax, vy = by - ay;
            float l2 = vx * vx + vy * vy;
            float t = l2 > 0.0001f ? Math.Max(0f, Math.Min(1f, ((cx - ax) * vx + (cy - ay) * vy) / l2)) : 0f;
            float qx = ax + vx * t - cx, qy = ay + vy * t - cy;
            return (float) Math.Sqrt(qx * qx + qy * qy);
        }

        // ponto 'look' metros a frente no rastro, a partir do ponto do rastro mais perto do bot
        static void PathPoint(List<Sample> trail, float x, float y, float look, out float tx, out float ty)
        {
            int j = trail.Count - 1;
            float best = float.MaxValue;
            for (int k = 0; k < trail.Count; k++)
            {
                float ex = trail[k].X - x, ey = trail[k].Y - y, e = ex * ex + ey * ey;
                if (e < best) { best = e; j = k; }
            }
            float left = look;
            tx = trail[j].X; ty = trail[j].Y;
            for (int k = j + 1; k < trail.Count; k++)
            {
                float sx = trail[k].X - tx, sy = trail[k].Y - ty;
                float seg = (float) Math.Sqrt(sx * sx + sy * sy);
                if (seg >= left && seg > 0.0001f) { tx += sx / seg * left; ty += sy / seg * left; return; }
                left -= seg;
                tx = trail[k].X; ty = trail[k].Y;
            }
        }

        static float NearestZ(List<Sample> trail, float x, float y, float fallback)
        {
            float best = float.MaxValue, z = fallback;
            for (int k = 0; k < trail.Count; k++)
            {
                float ex = trail[k].X - x, ey = trail[k].Y - y, e = ex * ex + ey * ey;
                if (e < best) { best = e; z = F(trail[k].Move, 0x20); }
            }
            return z;
        }

        // ---- party numa perseguicao de HUV ------------------------------------------------------------------------
        // Cada membro fica com um HUV e corre atras dele PELA ESTRADA: segue o seu rastro (voce esta perseguindo o HUV)
        // ate encostar no trajeto do proprio HUV, e dai segue o trajeto do HUV. Posicao/velocidade proprias (sem
        // teleporte), volante com limite (faz curva). Carrega o booster PartyChaseCharge s atras do HUV e ataca
        // PartyChaseAttack s (vai em cima). Encostou (PartyHuvContact m): bate (CityHuvHits).
        // o bot (onde ele esta agora) esta a menos de N m de algum HUV vivo?
        static bool NearHuv(PlayerState s, Bots.BotProfile b, Dictionary<uint, List<Sample>> huvs, float dist)
        {
            float x, y;
            Pursuer pp; byte[] fl;
            if (s.Pursuers.TryGetValue(b.Cid, out pp)) { x = pp.X; y = pp.Y; }
            else if (s.FollowLast.TryGetValue(b.Cid, out fl)) { x = F(fl, 0x18); y = F(fl, 0x1c); }
            else return false;
            foreach (var hv in huvs.Values)
            {
                var hl = hv[hv.Count - 1];
                float dx = hl.X - x, dy = hl.Y - y;
                if (dx * dx + dy * dy <= dist * dist) return true;
            }
            return false;
        }

        // o seu movimento num instante (para copiar o pulo do poder do boss)
        static Sample TrailAt(List<Sample> t, DateTime at)
        {
            Sample best = null; double bd = double.MaxValue;
            for (int k = t.Count - 1; k >= 0 && k >= t.Count - 400; k--)
            {
                double d = Math.Abs((t[k].At - at).TotalSeconds);
                if (d < bd) { bd = d; best = t[k]; }
            }
            return bd < 0.5 ? best : null;
        }

        static byte[] ChaseMove(PlayerState s, Bots.BotProfile b, int i, Dictionary<uint, List<Sample>> huvs, uint nowTime)
        {
            var cfg = RiceServer.Config;
            const float dt = (float) TickSeconds;
            Pursuer pu;
            if (!s.Pursuers.TryGetValue(b.Cid, out pu))
            {
                byte[] from;
                if (!s.FollowLast.TryGetValue(b.Cid, out from)) from = s.Trail[s.Trail.Count - 1].Move;
                pu = new Pursuer { X = F(from, 0x18), Y = F(from, 0x1c), Z = F(from, 0x20), Dir = Unheading(F(from, 0x24)), Speed = 0, Huv = 0 };
                float fvx = F(from, 0x28), fvy = F(from, 0x2c);
                pu.Speed = (float) Math.Sqrt(fvx * fvx + fvy * fvy);
                if (pu.Speed > 0.5f) pu.Dir = (float) Math.Atan2(fvy, fvx);
                s.Pursuers[b.Cid] = pu;
            }
            if (!huvs.ContainsKey(pu.Huv))
            {
                // um HUV para cada (sorteado entre os menos perseguidos); fica com ele ate o HUV acabar
                pu.Huv = huvs.Keys.OrderBy(k => s.Pursuers.Values.Count(o => o != pu && o.Huv == k)).ThenBy(k => Rng.Next()).First();
                pu.LastContact = DateTime.UtcNow;
            }
            // Rush: dividem o monte entre o boss e os HUVs que ele solta (a cada 3 s quem esta no mais cheio vai no mais vazio)
            if (s.Owner != null && Rush.Running(s.Owner) && huvs.Count > 1 && (DateTime.UtcNow - pu.Rebalance).TotalSeconds > 3)
            {
                pu.Rebalance = DateTime.UtcNow;
                Func<uint, int> cnt = k => s.Pursuers.Values.Count(o => o.Huv == k);
                uint least = huvs.Keys.OrderBy(k => cnt(k)).ThenBy(k => Rng.Next()).First();
                if (cnt(pu.Huv) - cnt(least) >= 2) { pu.Huv = least; pu.LastContact = DateTime.UtcNow; }
            }
            var htrail = huvs[pu.Huv];
            var h0 = htrail[htrail.Count - 1];
            float hvx = F(h0.Move, 0x28), hvy = F(h0.Move, 0x2c);
            float age = (float) Math.Min(2.0, (DateTime.UtcNow - h0.At).TotalSeconds);
            float hx = h0.X + hvx * age, hy = h0.Y + hvy * age;
            float hv = (float) Math.Sqrt(hvx * hvx + hvy * hvy);
            float hdx = hv > 0.5f ? hvx / hv : (float) Math.Cos(pu.Dir), hdy = hv > 0.5f ? hvy / hv : (float) Math.Sin(pu.Dir);

            double cycle = Math.Max(1, cfg.PartyChaseCharge + cfg.PartyChaseAttack);
            double tt = ((DateTime.UtcNow - s.ChaseStart).TotalSeconds + i * 1.3) % cycle;
            bool attack = tt >= cfg.PartyChaseCharge;
            // Rush Time: o boss e grande; os bots encostam na beirada dele (RushBossRadius + RushBotEdgeGap), nunca no meio,
            // e so pulam (para RushBotBehind m atras dele) depois de RushBotJumpSeconds
            bool rush = s.Owner != null && Rush.Running(s.Owner);
            // so o boss e grande: os HUVs pequenos que ele solta os bots encostam e batem como num HUV normal
            // (antes ficavam a distancia do boss e nunca encostavam neles)
            bool onBoss = false;
            if (rush)
            {
                ushort tso = BitConverter.ToUInt16(h0.Move, 4);
                if (tso == 0) tso = (ushort) (pu.Huv & 0xffff);
                ushort bso = Rush.BossSort(s.Owner);
                onBoss = bso == 0 || tso == bso;
            }
            float edge = rush && onBoss ? (float) Math.Max(0.5, cfg.RushBossRadius) : 0f;
            float behind = rush && onBoss ? edge + (float) Math.Max(0.0, cfg.RushBotEdgeGap) - 1f : 0f;   // alvo do ataque: na beirada
            // Neo City: fica a distancia do HUV (atira com a arma) em vez de encostar
            // Neo City: com arma fica a distancia do HUV e atira; sem arma vai para cima dele (bate)
            if (NeoCity.IsNeoArea(s.AreaId) && NeoCity.HasWeapon(b)) { behind = (float) Math.Max(5.0, cfg.NeoBotStandoff); attack = false; }

            float dHuv = (float) Math.Sqrt((hx - pu.X) * (hx - pu.X) + (hy - pu.Y) * (hy - pu.Y));
            float onHuvPath = NearestDist(htrail, pu.X, pu.Y);
            float look = Math.Max(3f, Math.Min(8f, pu.Speed * 0.4f));
            float tx, ty;
            // o HUV esta virando (a direcao dele e a do bot diferem): pega o trecho do caminho do HUV para fazer a mesma
            // curva e depois volta a ir em cima dele
            float huvTurn = Math.Abs(Wrap((float) Math.Atan2(hdy, hdx) - pu.Dir));
            bool curving = huvTurn > 0.5f && onHuvPath < 25f;
            if (dHuv < 10f + behind && !curving)
            {
                // perto: carregando = 4 m atras dele no trajeto; atacando = em cima (encosta)
                if (attack) { tx = hx + hdx * (1f - behind); ty = hy + hdy * (1f - behind); }
                else { tx = hx - hdx * (4f + behind); ty = hy - hdy * (4f + behind); }
            }
            else if (curving || onHuvPath < 8f) PathPoint(htrail, pu.X, pu.Y, curving ? Math.Max(4f, look) : look, out tx, out ty);  // trajeto do HUV
            else if (onHuvPath < 40f) PathPoint(htrail, pu.X, pu.Y, Math.Max(8f, look * 1.5f), out tx, out ty);   // volta para o trajeto do HUV
            else PathPoint(s.Trail, pu.X, pu.Y, look, out tx, out ty);                       // pela estrada (seu rastro)

            // nao passa por cima de voce: perto de voce, abre para o lado
            var me = s.Trail[s.Trail.Count - 1];
            float mdx = me.X - pu.X, mdy = me.Y - pu.Y, md = (float) Math.Sqrt(mdx * mdx + mdy * mdy);
            if (md < 6f)
            {
                float side = cfg.CityBotSideGap * (i % 2 == 0 ? 1f : -1f);
                float ddx = tx - pu.X, ddy = ty - pu.Y, dl = Math.Max(0.01f, (float) Math.Sqrt(ddx * ddx + ddy * ddy));
                tx += -ddy / dl * side; ty += ddx / dl * side;
            }

            var cst = BotSocial.CarStats(b);                                       // speed, accel, durab., boost do carro
            float boostF = (float) cfg.PartyChaseBoost * (0.85f + cst[3] / 800f);
            float skill = (float) Math.Max(0.5, Math.Min(2.0, b.Skill));
            float vmax = (28f + cst[0] * 0.16f + Licenses.BotSpeedBonus(b)) * (attack ? boostF : 1f) * (float) Math.Max(0.5, cfg.PartyChaseSpeed) * (0.9f + skill * 0.1f);
            float accUp = (6f + cst[1] * 0.04f) * (attack ? 1.5f : 1f) * (float) Math.Max(0.5, cfg.PartyChaseSpeed) * skill;
            float desired;
            if (dHuv > 12f) { vmax *= (float) Math.Max(1.0, cfg.PartyChaseCatchUp); accUp *= 1.6f; }   // atras: acelera para alcancar
            if (dHuv > 25f) desired = vmax;                                     // alcancando
            else if (attack) desired = Math.Min(vmax, hv + 6f + dHuv);
            else desired = Math.Max(0f, Math.Min(vmax, hv + (dHuv - 5f) * 1.2f));
            float aheadOfHuv = (pu.X - hx) * hdx + (pu.Y - hy) * hdy;
            if (aheadOfHuv > 2f) desired = Math.Min(desired, hv * 0.6f);        // passou: freia e deixa ele passar
            // curva: diminui antes de virar (nao passa por cima de tudo)
            float turn = Math.Abs(Wrap((float) Math.Atan2(ty - pu.Y, tx - pu.X) - pu.Dir));
            if (dHuv >= 10f)
            {
                if (turn > 1.4f) desired = Math.Min(desired, 9f);
                else if (turn > 0.8f) desired = Math.Min(desired, 15f);
                else if (turn > 0.35f) desired = Math.Min(desired, 26f);
            }
            // Rush: depois de bater no boss fica devagar 2 s (RushBotHitSlowSeconds) e depois volta com tudo (alcancando)
            float jumpZ = 0f;
            if (rush)
            {
                if (DateTime.UtcNow < pu.SlowUntil) desired = Math.Min(desired, Math.Max(4f, hv * 0.4f));
                // poder do boss (o seu carro pulou): o bot e jogado para cima, cai, fica parado 3 s e volta
                // o poder pega quem esta perto de voce quando voce foi jogado para cima (RushPowerRadius)...
                if (s.BossPowerAt > pu.JumpAt && (DateTime.UtcNow - s.BossPowerAt).TotalSeconds < 2)
                {
                    var at = TrailAt(s.Trail, s.BossPowerAt);
                    float px = at != null ? at.X : me.X, py = at != null ? at.Y : me.Y;
                    float pr = (float) Math.Max(5, cfg.RushPowerRadius);
                    if ((pu.X - px) * (pu.X - px) + (pu.Y - py) * (pu.Y - py) <= pr * pr) pu.JumpAt = s.BossPowerAt.AddMilliseconds(Rng.Next(0, 600));
                }
                // ...e o boss tambem acerta os bots sozinho, mesmo sem pegar voce
                if (dHuv < edge + 6f && cfg.RushBotPowerEvery > 0 && (DateTime.UtcNow - pu.JumpAt).TotalSeconds > 8
                    && Rng.NextDouble() < dt / cfg.RushBotPowerEvery) pu.JumpAt = DateTime.UtcNow;
                double jt = (DateTime.UtcNow - pu.JumpAt).TotalSeconds;
                double air = Math.Max(0.5, cfg.RushBotJumpAir), rest = Math.Max(0, cfg.RushBotJumpRest);
                if (jt >= 0 && jt < air) { jumpZ = (float) (cfg.RushBotJumpHeight * Math.Sin(Math.PI * jt / air)); desired = Math.Min(desired, pu.Speed * 0.6f); }
                else if (jt >= air && jt < air + rest) { desired = 0f; pu.Speed = 0f; pu.LastContact = DateTime.UtcNow; }
            }
            float acc = desired > pu.Speed ? accUp : 22f;
            if (Math.Abs(desired - pu.Speed) <= acc * dt) pu.Speed = desired; else pu.Speed += Math.Sign(desired - pu.Speed) * acc * dt;

            // sem encostar no HUV por PartyChaseTeleportSeconds: pula para tras dele (o contador reinicia)
            bool jump;
            if (rush)
            {
                // Rush: cada bot so pula depois de RushBotJumpSeconds sem bater, ou longe (RushBotJumpDistance) por esse tempo
                if (dHuv > cfg.RushBotJumpDistance) { if (pu.FarSince == DateTime.MinValue) pu.FarSince = DateTime.UtcNow; }
                else pu.FarSince = DateTime.MinValue;
                double js = Math.Max(5, cfg.RushBotJumpSeconds);
                jump = (DateTime.UtcNow - pu.LastContact).TotalSeconds > js
                       || (pu.FarSince != DateTime.MinValue && (DateTime.UtcNow - pu.FarSince).TotalSeconds > js);
            }
            else jump = (cfg.PartyChaseTeleportSeconds > 0 && (DateTime.UtcNow - pu.LastContact).TotalSeconds > cfg.PartyChaseTeleportSeconds)
                        || (cfg.PartyChaseTeleportDistance > 0 && dHuv > cfg.PartyChaseTeleportDistance);
            // Neo City: nunca pula para tras do HUV - vai dirigindo ate ele (so volta para perto de voce, NeoBotTeleportDistance)
            if (NeoCity.IsNeoArea(s.AreaId)) { jump = false; pu.LastContact = DateTime.UtcNow; }
            if (jump)
            {
                float sd = cfg.CityBotSideGap * (i % 2 == 0 ? 1f : -1f);
                float back = rush ? (float) Math.Max(2.0, cfg.RushBotBehind) : 8f;
                pu.X = hx - hdx * back - hdy * sd; pu.Y = hy - hdy * back + hdx * sd;
                pu.FarSince = DateTime.MinValue;
                pu.Dir = (float) Math.Atan2(hdy, hdx); pu.Speed = hv;
                pu.LastContact = DateTime.UtcNow;
                dHuv = 8f;
            }

            Steer(pu, tx, ty, dt);
            pu.X += (float) Math.Cos(pu.Dir) * pu.Speed * dt;
            pu.Y += (float) Math.Sin(pu.Dir) * pu.Speed * dt;
            pu.Z = NearestZ(s.Trail, pu.X, pu.Y, F(h0.Move, 0x20));

            var m = BotMove(me.Move, b, nowTime, false);
            ApplyDriveState(s, m, pu.X, pu.Y, pu.Speed);
            if (jumpZ > 0f)
            {
                // no ar: copia o seu pulo (altura e estado do carro no mesmo momento do seu pulo) para aparecer pulando
                var pj = TrailAt(s.Trail, s.BossPowerAt.AddSeconds((DateTime.UtcNow - pu.JumpAt).TotalSeconds));
                var p0 = TrailAt(s.Trail, s.BossPowerAt);
                if (pj != null && p0 != null)
                {
                    float dz = F(pj.Move, 0x20) - F(p0.Move, 0x20);
                    if (dz > 0.3f) jumpZ = dz;
                    if (pj.Move.Length >= FullMove && m.Length >= FullMove) Array.Copy(pj.Move, MoveSize, m, MoveSize, StateSize);
                }
            }
            SetF(m, 0x18, pu.X); SetF(m, 0x1c, pu.Y); SetF(m, 0x20, pu.Z + jumpZ); SetF(m, 0x24, Heading(pu.Dir + Slip(s, pu.X, pu.Y, pu.Speed)));
            SetF(m, 0x28, (float) Math.Cos(pu.Dir) * pu.Speed); SetF(m, 0x2c, (float) Math.Sin(pu.Dir) * pu.Speed);
            SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            s.FollowLast[b.Cid] = m;

            // encostou no HUV: golpe
            float contact = rush && onBoss ? edge + (float) Math.Max(0.0, cfg.RushBotEdgeGap) + 0.8f : Math.Max(0.5f, cfg.PartyHuvContact);
            if (rush && onBoss)
            {
                // nao entra no boss: fica na beirada
                float ex = pu.X - hx, ey = pu.Y - hy, ed = (float) Math.Sqrt(ex * ex + ey * ey);
                if (ed < edge && ed > 0.01f)
                {
                    pu.X = hx + ex / ed * edge; pu.Y = hy + ey / ed * edge;
                    SetF(m, 0x18, pu.X); SetF(m, 0x1c, pu.Y);
                    pu.Speed = Math.Min(pu.Speed, hv);
                }
            }
            if (dHuv <= contact)
            {
                pu.LastContact = DateTime.UtcNow; TryHuvHit(s, b, pu.Huv, h0, dHuv);
                if (rush && DateTime.UtcNow >= pu.SlowUntil) pu.SlowUntil = DateTime.UtcNow.AddSeconds(Math.Max(0, cfg.RushBotHitSlowSeconds));
            }
            return m;
        }

        // Patrulha / entrega com a party: o bot larga de seguir e vai ao ponto mais perto que falta (que nenhum outro
        // bot pegou), pega ele e vai ao proximo. Anda reto ate o ponto; sem chegar em MissionBotTeleportSeconds, pula la.
        static byte[] MissionMove(PlayerState s, Bots.BotProfile b, int i, List<float[]> points, uint nowTime)
        {
            var cfg = RiceServer.Config;
            const float dt = (float) TickSeconds;
            Pursuer pu;
            if (!s.Runners.TryGetValue(b.Cid, out pu))
            {
                byte[] from;
                if (!s.FollowLast.TryGetValue(b.Cid, out from)) from = s.Trail[s.Trail.Count - 1].Move;
                pu = new Pursuer { X = F(from, 0x18), Y = F(from, 0x1c), Z = F(from, 0x20), Dir = Unheading(F(from, 0x24)) };
                float fvx = F(from, 0x28), fvy = F(from, 0x2c);
                pu.Speed = (float) Math.Sqrt(fvx * fvx + fvy * fvy);
                if (pu.Speed > 0.5f) pu.Dir = (float) Math.Atan2(fvy, fvx);
                s.Runners[b.Cid] = pu;
            }
            var me = s.Trail[s.Trail.Count - 1];
            float[] target = points.FirstOrDefault(pt => (int) pt[0] == pu.Point);
            if (target == null)
            {
                // o mais perto do bot que nenhum outro bot esta indo pegar (o mais perto de voce fica para voce)
                var mine = points.OrderBy(pt => (pt[1] - me.X) * (pt[1] - me.X) + (pt[2] - me.Y) * (pt[2] - me.Y)).FirstOrDefault();
                var free = points.Where(pt => !s.Runners.Values.Any(o => o != pu && o.Point == (int) pt[0]) && (points.Count <= 1 || pt != mine))
                                 .OrderBy(pt => (pt[1] - pu.X) * (pt[1] - pu.X) + (pt[2] - pu.Y) * (pt[2] - pu.Y)).ToList();
                if (free.Count == 0) { pu.Point = -1; return null; }        // nada para pegar: volta a seguir
                target = free[0];
                pu.Point = (int) target[0];
                pu.PointSince = DateTime.UtcNow;
                // vai dirigindo pelas ruas (faixas do transito) ate o ponto
                pu.Route = null; pu.RouteIdx = 0;
                int city = s.Owner != null ? s.Owner.City : 1;
                if (cfg.MissionBotRoads) { try { pu.Route = Rice.Game.CityTraffic.Route(city, pu.X, pu.Y, target[1], target[2]); } catch (Exception) { pu.Route = null; } }
            }
            float ptx = target[1], pty = target[2];
            float tx = ptx, ty = pty;
            float d = (float) Math.Sqrt((tx - pu.X) * (tx - pu.X) + (ty - pu.Y) * (ty - pu.Y));
            float tz = target.Length > 3 ? target[3] : pu.Z;
            if (pu.Route != null && pu.Route.Count > 0)
            {
                // segue o caminho: mira no proximo ponto da rua (pula os que ja passou)
                while (pu.RouteIdx < pu.Route.Count - 1)
                {
                    var rp = pu.Route[pu.RouteIdx];
                    float rd = (rp[0] - pu.X) * (rp[0] - pu.X) + (rp[1] - pu.Y) * (rp[1] - pu.Y);
                    if (rd > 6f * 6f) break;
                    pu.RouteIdx++;
                }
                var wp = pu.Route[Math.Min(pu.RouteIdx, pu.Route.Count - 1)];
                tx = wp[0]; ty = wp[1]; if (wp.Length > 2 && pu.RouteIdx < pu.Route.Count - 1) tz = wp[2];
            }
            // longe (outra rua) e sem caminho: andar reto sairia do mapa -> aparece no ponto depois de um instante
            bool far = pu.Route == null && d > Math.Max(20f, cfg.MissionBotDriveRange);
            double wait = far ? 1.5 + i * 0.7 : cfg.MissionBotTeleportSeconds;
            if (wait > 0 && (DateTime.UtcNow - pu.PointSince).TotalSeconds > wait)
            {
                pu.X = ptx - (float) Math.Cos(pu.Dir) * 3f; pu.Y = pty - (float) Math.Sin(pu.Dir) * 3f; pu.Z = tz; d = 3f;
                pu.Speed = 0f; pu.Route = null;
            }
            else if (far)
            {
                // esperando para aparecer: fica parado onde esta (nao sai andando pelo meio dos predios)
                var me0 = BotMove(me.Move, b, nowTime, false);
                WithIdle(s, me0);
                SetF(me0, 0x18, pu.X); SetF(me0, 0x1c, pu.Y); SetF(me0, 0x20, pu.Z); SetF(me0, 0x24, Heading(pu.Dir));
                SetF(me0, 0x28, 0f); SetF(me0, 0x2c, 0f); SetF(me0, 0x30, 0f); SetF(me0, 0x34, 0f);
                s.FollowLast[b.Cid] = me0;
                return me0;
            }
            var cst = BotSocial.CarStats(b);
            float vmax = 28f + cst[0] * 0.16f + Licenses.BotSpeedBonus(b);
            float accUp = 6f + cst[1] * 0.04f;
            float turn = Math.Abs(Wrap((float) Math.Atan2(ty - pu.Y, tx - pu.X) - pu.Dir));
            float dWp = (float) Math.Sqrt((tx - pu.X) * (tx - pu.X) + (ty - pu.Y) * (ty - pu.Y));
            float desired = Math.Min(vmax, 6f + (pu.Route != null ? Math.Max(dWp, Math.Min(d, 40f)) : d) * 0.8f);
            if (turn > 1.4f) desired = Math.Min(desired, 9f);
            else if (turn > 0.8f) desired = Math.Min(desired, 15f);
            float acc = desired > pu.Speed ? accUp : 22f;
            if (Math.Abs(desired - pu.Speed) <= acc * dt) pu.Speed = desired; else pu.Speed += Math.Sign(desired - pu.Speed) * acc * dt;
            Steer(pu, tx, ty, dt);
            pu.X += (float) Math.Cos(pu.Dir) * pu.Speed * dt;
            pu.Y += (float) Math.Sin(pu.Dir) * pu.Speed * dt;
            pu.Z += (tz - pu.Z) * 0.3f;

            if (d <= Math.Max(3f, cfg.MissionBotReach))
            {
                s.Reached.Add(new KeyValuePair<string, int>(b.Name, pu.Point));
                pu.Point = -1;
            }
            var m = BotMove(me.Move, b, nowTime, false);
            ApplyDriveState(s, m, pu.X, pu.Y, pu.Speed);
            SetF(m, 0x18, pu.X); SetF(m, 0x1c, pu.Y); SetF(m, 0x20, pu.Z); SetF(m, 0x24, Heading(pu.Dir));
            SetF(m, 0x28, (float) Math.Cos(pu.Dir) * pu.Speed); SetF(m, 0x2c, (float) Math.Sin(pu.Dir) * pu.Speed);
            SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            s.FollowLast[b.Cid] = m;
            return m;
        }

        // volante com limite por segundo (curva, nao gira no lugar)
        static void Steer(Pursuer pu, float tx, float ty, float dt)
        {
            float tdx = tx - pu.X, tdy = ty - pu.Y;
            if (tdx * tdx + tdy * tdy < 0.09f || pu.Speed < 0.2f) return;
            float diff = Wrap((float) Math.Atan2(tdy, tdx) - pu.Dir);
            float rate = 3.2f / (1f + pu.Speed / 18f);
            pu.Dir = Wrap(pu.Dir + Math.Max(-rate * dt, Math.Min(rate * dt, diff)));
        }

        static float NearestDist(List<Sample> trail, float x, float y)
        {
            float best = float.MaxValue;
            for (int k = 0; k < trail.Count; k++)
            {
                float ex = trail[k].X - x, ey = trail[k].Y - y, e = ex * ex + ey * ey;
                if (e < best) best = e;
            }
            return (float) Math.Sqrt(best);
        }

        static Sample NearestSample(List<Sample> trail, float x, float y)
        {
            Sample bs = null; float best = float.MaxValue;
            for (int k = 0; k < trail.Count; k++)
            {
                float ex = trail[k].X - x, ey = trail[k].Y - y, e = ex * ex + ey * ey;
                if (e < best) { best = e; bs = trail[k]; }
            }
            return bs;
        }

        // Drift: o bot "derrapa" onde voce derrapou (o angulo entre a frente do seu carro e a sua direcao naquele ponto)
        static float Slip(PlayerState s, float x, float y, float speed)
        {
            if (!RiceServer.Config.PartyBotDrift || speed < 6f) return 0f;
            var sm = NearestSample(s.Trail, x, y);
            if (sm == null) return 0f;
            float vx = F(sm.Move, 0x28), vy = F(sm.Move, 0x2c);
            if (vx * vx + vy * vy < 36f) return 0f;
            float slip = Wrap(Unheading(F(sm.Move, 0x24)) - (float) Math.Atan2(vy, vx));
            return Math.Max(-0.8f, Math.Min(0.8f, slip));
        }

        // Estado do carro andando: o seu estado naquele ponto do caminho (fumaca do drift, luzes); parado: neutro
        static void ApplyDriveState(PlayerState s, byte[] m, float x, float y, float speed)
        {
            // bots da party: andam com o estado do seu carro naquele ponto (volante, drift, luzes) - PartyBotsCopyMyState
            if (speed < 3f || !RiceServer.Config.PartyBotDrift || !(RiceServer.Config.BotsCopyMyState || RiceServer.Config.PartyBotsCopyMyState)) { WithIdle(s, m); return; }
            var sm = NearestSample(s.Trail, x, y);
            if (sm == null) { WithIdle(s, m); return; }
            Array.Copy(sm.Move, MoveSize, m, MoveSize, StateSize);
        }

        // ---- golpes dos bots no HUV da perseguicao da cidade -----------------------------------------------------
        // 187 ChaseHit para o seu cliente (u16 quem bateu, u16 dono do HUV = voce, u16 numero do HUV, f32 dano, ...;
        // cliente 0x525310 soma o dano no HUV). O numero do HUV vem do movimento dele (+4).
        static readonly Dictionary<long, DateTime> lastHit = new Dictionary<long, DateTime>();
        static void TryHuvHit(PlayerState s, Bots.BotProfile b, uint key, Sample h, float dist)
        {
            var cfg = RiceServer.Config;
            if (UnderCityActive(s)) return;
            if (NeoCity.IsNeoArea(s.AreaId)) return;      // Neo City: os bots atiram de longe (NeoCity.BotTick)
            if (s.Owner != null && Rush.Running(s.Owner))
            {
                // Rush Time: o golpe tira vida do boss (o servidor conta e manda o estado)
                DateTime lr;
                if (lastHit.TryGetValue(b.Cid, out lr) && (DateTime.UtcNow - lr).TotalSeconds < Math.Max(0.3, cfg.CityHuvHitSeconds)) return;
                lastHit[b.Cid] = DateTime.UtcNow;
                var st = BotSocial.CarStats(b);
                ushort rsort = BitConverter.ToUInt16(h.Move, 4);
                if (rsort == 0) rsort = (ushort) (key & 0xffff);
                // forca do golpe pelos atributos do carro (velocidade, aceleracao, durabilidade, boost) e a habilidade do bot;
                // o Rush transforma isso numa fracao da vida do HUV (RushBotBossHit / RushBotHuvHit)
                double power = (0.6 + (st[0] + st[1] + st[2] + st[3]) / 1600.0) * Math.Max(0.5, Math.Min(2.0, b.Skill));
                s.RushHits.Add(new Tuple<long, float, ushort>(b.Cid, (float) power, rsort));
                return;
            }
            if (!cfg.CityHuvHits) return;
            DateTime last;
            if (lastHit.TryGetValue(b.Cid, out last) && (DateTime.UtcNow - last).TotalSeconds < Math.Max(0.3, cfg.CityHuvHitSeconds)) return;
            lastHit[b.Cid] = DateTime.UtcNow;
            ushort sort = BitConverter.ToUInt16(h.Move, 4);
            if (sort == 0) sort = (ushort) (key & 0xffff);
            s.PendingHits.Add(new[] { BotSocial.SerialOf(b), (uint) sort });
        }

        static bool UnderCityActive(PlayerState s) { return s.InUnderCity; }

        // ---- estacionados e andando ---------------------------------------------------------------------------
        static void MakeSpots(Rice.Game.Character ch, PlayerState s, List<Bots.BotProfile> party)
        {
            s.SpotsMade = true;
            var cfg = RiceServer.Config;
            var first = s.Trail[0].Move;
            float x0 = F(first, 0x18), y0 = F(first, 0x1c);
            // vendedores e o Joaopvbr: juntos, so na rua de Oros e so as vezes
            var sellers = cfg.CityBotsSellers ? Bots.All().Where(x => (x.Special || Bots.IsAce(x)) && !party.Contains(x)).ToList() : new List<Bots.BotProfile>();
            // vendedores (Brunizin.X, Marsteh): so na rua de Oros (CitySellersCity) e nem sempre (CitySellersChance); nao no lobby
            if (IsDome(s.AreaId) || (cfg.CitySellersCity > 0 && ch.City != cfg.CitySellersCity)) sellers.Clear();
            sellers = sellers.Where(x => Rng.NextDouble() < cfg.CitySellersChance).ToList();
            // base ROAD (Neo City): so os bots que moram la; no Driver Dome normal e nas cidades, nunca eles
            if (curNeo) sellers.Clear();
            var pool = Bots.All().Where(x => !x.Special && !Bots.IsAce(x) && (curNeo ? x.NeoBot : !x.NeoBot) && !party.Contains(x) && BotSocial.Online(x)).OrderBy(x => Rng.Next()).ToList();

            if (IsDome(s.AreaId))
            {
                // Driver Dome: posicoes aleatorias entre os pontos onde voce ja andou (sempre dentro da area)
                var bots = sellers.Concat(pool.Take(Math.Max(0, cfg.DomeParkedBots - sellers.Count()))).Take(Math.Max(sellers.Count(), cfg.DomeParkedBots)).ToList();   // no maximo DomeParkedBots no lobby
                // seus outros corredores (todas as contas) ficam sempre estacionados no lobby (AltsInLobby)
                if (cfg.AltsInLobby && !curNeo)
                    bots = Bots.AltsOf(ch).Where(x => !party.Contains(x)).Concat(bots.Where(x => !x.IsAlt)).ToList();
                var pts = domePoints.OrderBy(x => Rng.Next()).ToList();
                var used = new List<float[]>();
                foreach (var b in bots)
                {
                    // parte dos bots anda pelo seu trajeto (ate DomeDriveSpeedMax, 80 mph); os outros ficam parados
                    if (!b.Special && domePoints.Count > 20 && Rng.NextDouble() < cfg.DomeDrivingShare)
                    {
                        int k = Rng.Next(domePoints.Count);
                        var p0 = domePoints[k];
                        s.DomeDrivers[b.Cid] = new DomeDriver
                        {
                            Target = (k + 1) % domePoints.Count, X = p0[0], Y = p0[1], Z = p0[2], Dir = Unheading(p0[3]),
                            Max = (float) (cfg.DomeDriveSpeedMin + Rng.NextDouble() * Math.Max(0, cfg.DomeDriveSpeedMax - cfg.DomeDriveSpeedMin))
                        };
                        continue;
                    }
                    var pt = pts.FirstOrDefault(q => used.All(u => (u[0] - q[0]) * (u[0] - q[0]) + (u[1] - q[1]) * (u[1] - q[1]) > 100f)
                                                     && (q[0] - x0) * (q[0] - x0) + (q[1] - y0) * (q[1] - y0) > 64f);
                    if (pt == null) { s.DomeQueue.Add(b.Cid); continue; }
                    used.Add(pt);
                    var spot = BotMove(first, b, 0, true);
                    float off = (float) (Rng.NextDouble() * 2 - 1) * 1.5f;
                    SetF(spot, 0x18, pt[0] + (float) Math.Sin(pt[3]) * off);
                    SetF(spot, 0x1c, pt[1] - (float) Math.Cos(pt[3]) * off);
                    SetF(spot, 0x20, pt[2]);
                    SetF(spot, 0x24, (float) (Rng.NextDouble() < 0.5 ? pt[3] : pt[3] + Math.PI));
                    s.Parked[b.Cid] = spot;
                }
                Log.WriteLine("Rua: {0} - {1} bot(s) estacionado(s), {2} andando, {3} esperando voce andar mais ({4} pontos conhecidos).",
                    curNeo ? "Base ROAD (Neo City)" : "Driver Dome", s.Parked.Count, s.DomeDrivers.Count, s.DomeQueue.Count, domePoints.Count);
                return;
            }

            var usedC = new List<float[]>();
            foreach (var b in sellers)
            {
                float x, y, z, dir;
                if (!RoadSpotFree(ch.City, x0, y0, 20f, 90f, usedC, out x, out y, out z, out dir)) continue;
                var spot = BotMove(first, b, 0, true);
                SetF(spot, 0x18, x); SetF(spot, 0x1c, y); SetF(spot, 0x20, z); SetF(spot, 0x24, Heading(dir));
                s.Parked[b.Cid] = spot;
            }
            int parked = 0, driving = 0;
            if (cfg.CityWholeCity)
            {
                // moradores desta cidade, espalhados pela cidade inteira (os de crew no ponto da crew)
                var res = Bots.Residents(ch.City).Where(x => !party.Contains(x) && Bots.LinkOf(x.Cid) == null).Take(Math.Max(0, cfg.CityBotsPerCity)).ToList();
                // Most Wanted: so depois de vencer a LegendsDC (os da cidade, do mais fraco ao mais forte)
                if (cfg.CrewLadder && CrewLadder.Of(ch).MostWanted)
                    foreach (var m in Bots.MostWantedIn(ch.City).Where(x => !party.Contains(x) && Bots.LinkOf(x.Cid) == null)) res.Add(m);
                // Oros: a LegendsDC (amPerl) anda pela cidade (escada de crews)
                if (ch.City == 4 && cfg.CrewLadder)
                {
                    var lend = Crew.ByName(Bots.EliteCrew);
                    if (lend != null)
                        foreach (var m in lend.Members.Select(Bots.ByCid).Where(x => x != null && !party.Contains(x) && !res.Contains(x)))
                        { res.Add(m); sellers.Remove(m); }
                }
                foreach (var b in res)
                {
                    s.Parked.Remove(b.Cid);
                    float ax, ay; bool crewSpot = CrewAnchor(ch.City, b, out ax, out ay);
                    float rmin = 0f, rmax = crewSpot ? 90f : 1e7f;
                    if (!crewSpot) { ax = x0; ay = y0; rmin = 60f; }
                    // todos andando (CityAllDriving); os de crew saem do ponto da crew
                    if (cfg.CityDrivingBots && (cfg.CityAllDriving || (!crewSpot && Rng.NextDouble() < cfg.CityDrivingShare)))
                    {
                        var cur = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, ax, ay, rmin, rmax)
                                  ?? Rice.Game.CityTraffic.StartCursor(ch.City, Rng, ax, ay, 0f, crewSpot ? 400f : 1e7f);
                        if (cur != null)
                        {
                            s.Drivers[b.Cid] = new Driver { Cur = cur, Speed = (float) (cfg.CityDriveSpeedMin + Rng.NextDouble() * (cfg.CityDriveSpeedMax - cfg.CityDriveSpeedMin)) };
                            driving++;
                            continue;
                        }
                    }
                    float x, y, z, dir;
                    if (!RoadSpotFree(ch.City, ax, ay, rmin, rmax, usedC, out x, out y, out z, out dir)
                        && !RoadSpotFree(ch.City, x0, y0, 60f, 1e7f, usedC, out x, out y, out z, out dir)) continue;
                    var spot = BotMove(first, b, 0, true);
                    SetF(spot, 0x18, x); SetF(spot, 0x1c, y); SetF(spot, 0x20, z); SetF(spot, 0x24, Heading(dir));
                    s.Parked[b.Cid] = spot;
                    s.Origins[b.Cid] = spot;
                    parked++;
                }
                Log.WriteLine("Rua: cidade {0}: {1} morador(es) - {2} andando, {3} estacionado(s) pela cidade, {4} vendedor(es).",
                              ch.City, res.Count, driving, parked, sellers.Count);
                return;
            }
            foreach (var b in pool.Take(Math.Max(0, cfg.CityParkedBots)))
            {
                if (cfg.CityDrivingBots && Rng.NextDouble() < cfg.CityDrivingShare)
                {
                    var cur = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, x0, y0, 30f, cfg.CityParkedRadius);
                    if (cur != null)
                    {
                        s.Drivers[b.Cid] = new Driver { Cur = cur, Speed = (float) (cfg.CityDriveSpeedMin + Rng.NextDouble() * (cfg.CityDriveSpeedMax - cfg.CityDriveSpeedMin)) };
                        driving++;
                        continue;
                    }
                }
                if (!cfg.CityParkBots)
                {
                    // sem estacionados na cidade: quem nao saiu andando tenta andar; senao nao aparece
                    var cur2 = cfg.CityDrivingBots ? Rice.Game.CityTraffic.StartCursor(ch.City, Rng, x0, y0, 30f, cfg.CityParkedRadius) : null;
                    if (cur2 != null)
                    {
                        s.Drivers[b.Cid] = new Driver { Cur = cur2, Speed = (float) (cfg.CityDriveSpeedMin + Rng.NextDouble() * (cfg.CityDriveSpeedMax - cfg.CityDriveSpeedMin)) };
                        driving++;
                    }
                    continue;
                }
                float x, y, z, dir;
                if (!RoadSpotFree(ch.City, x0, y0, 40f, cfg.CityParkedRadius, usedC, out x, out y, out z, out dir)) continue;
                var spot = BotMove(first, b, 0, true);
                SetF(spot, 0x18, x); SetF(spot, 0x1c, y); SetF(spot, 0x20, z); SetF(spot, 0x24, Heading(dir));
                s.Parked[b.Cid] = spot;
                parked++;
            }
            Log.WriteLine("Rua: cidade {0}: {1} bot(s) andando, {2} estacionado(s), {3} vendedor(es).", ch.City, driving, parked, sellers.Count);
        }

        // os corredores do Rush esperando no lugar dele (em roda, de frente para o meio); some quem saiu da turma
        static void RushWaitSpots(Rice.Game.Character ch, PlayerState s, HashSet<long> realParty)
        {
            if (!RiceServer.Config.RushWaitAtSpot || Rush.Running(ch) || Rush.Counting(ch) || s.Trail.Count == 0) return;
            var sp = Rush.SpotOf(ch.City);
            if (sp == null) return;
            var bots = Rush.Waiting(ch.City, realParty);
            var first = s.Trail[0].Move;
            for (int k = 0; k < bots.Count; k++)
            {
                var b = bots[k];
                if (s.Parked.ContainsKey(b.Cid) || s.Drivers.ContainsKey(b.Cid)) continue;
                double ang = 2 * Math.PI * k / Math.Max(1, bots.Count);
                float r = 10f + (k % 2) * 6f;
                float x = sp[0] + (float) Math.Cos(ang) * r, y = sp[1] + (float) Math.Sin(ang) * r;
                var spot = BotMove(first, b, 0, true);
                SetF(spot, 0x18, x); SetF(spot, 0x1c, y);
                SetF(spot, 0x20, NearestZ(s.Trail, x, y, F(first, 0x20)));
                SetF(spot, 0x24, Heading((float) Math.Atan2(sp[1] - y, sp[0] - x)));
                s.Parked[b.Cid] = spot;
            }
        }

        // Driver Dome: quem nao coube ainda estaciona num ponto novo por onde voce passar.
        static void GrowDomeSpots(PlayerState s, List<Bots.BotProfile> party)
        {
            if (s.DomeQueue.Count == 0) return;
            var last = s.Trail[s.Trail.Count - 1];
            var old = DateTime.UtcNow.AddSeconds(-6);
            foreach (var smp in s.Trail)
            {
                if (smp.At > old) break;
                bool free = s.Parked.Values.All(v => Dist2(v, smp.X, smp.Y) > 100f);
                float dx = smp.X - last.X, dy = smp.Y - last.Y;
                if (!free || dx * dx + dy * dy < 144f) continue;
                long cid = s.DomeQueue[0];
                s.DomeQueue.RemoveAt(0);
                var b = Bots.ByCid(cid);
                if (b == null || party.Contains(b)) return;
                var spot = BotMove(smp.Move, b, 0, true);
                float rot = F(smp.Move, 0x24);
                SetF(spot, 0x18, smp.X + (float) Math.Sin(rot) * 3f);
                SetF(spot, 0x1c, smp.Y - (float) Math.Cos(rot) * 3f);
                s.Parked[cid] = spot;
                return;
            }
        }

        static bool RoadSpotFree(int city, float x0, float y0, float minD, float maxD, List<float[]> used,
                                 out float x, out float y, out float z, out float dir)
        {
            x = y = z = dir = 0;
            for (int t = 0; t < 10; t++)
            {
                if (!Rice.Game.CityTraffic.RoadSpot(city, Rng, x0, y0, minD, maxD, out x, out y, out z, out dir)) return false;
                float px = x, py = y;
                if (used.All(u => (u[0] - px) * (u[0] - px) + (u[1] - py) * (u[1] - py) > 144f))
                {
                    used.Add(new[] { x, y });
                    return true;
                }
            }
            return false;
        }

        static byte[] DriveMove(Rice.Game.Character ch, PlayerState s, Bots.BotProfile b, Driver d, uint nowTime, Sample last)
        {
            var cfg = RiceServer.Config;
            float x, y, z, dir;
            bool ok = Rice.Game.CityTraffic.Advance(ch.City, d.Cur, d.Speed * (float) TickSeconds, Rng, out x, out y, out z, out dir);
            float dx = x - last.X, dy = y - last.Y;
            if (cfg.CityWholeCity && !ok)
            {
                // rua sem saida: continua de uma rua perto de onde ele esta (a cidade inteira; nao volta para perto de voce)
                var lastPos = d.Last;
                float px = lastPos != null ? F(lastPos, 0x18) : last.X, py = lastPos != null ? F(lastPos, 0x1c) : last.Y;
                var cur = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, px, py, 0f, 300f) ?? Rice.Game.CityTraffic.StartCursor(ch.City, Rng, px, py, 0f, 1e7f);
                if (cur == null) return null;
                d.Cur = cur; d.Side = 0;
                if (!Rice.Game.CityTraffic.Advance(ch.City, d.Cur, 0.1f, Rng, out x, out y, out z, out dir)) return null;
            }
            else if (!cfg.CityWholeCity && (!ok || dx * dx + dy * dy > cfg.CityParkedRadius * cfg.CityParkedRadius * 2.25f))
            {
                var cur = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, last.X, last.Y, Math.Min(250f, cfg.CityBotViewRange * 0.6f), cfg.CityParkedRadius);
                if (cur == null) return null;
                d.Cur = cur; d.Side = 0;
                if (!Rice.Game.CityTraffic.Advance(ch.City, d.Cur, 0.1f, Rng, out x, out y, out z, out dir)) return null;
            }

            // ultrapassagem: carro do transito, outro bot ou voce na frente -> vai para o lado; depois volta
            bool blocked = Rice.Game.CityTraffic.TrafficAhead(ch.City, x, y, dir, 14f) || SomethingAhead(s, d, x, y, dir, last);
            float target = blocked ? cfg.CityOvertakeSide : 0f;
            float step = 3.5f * (float) TickSeconds;
            if (Math.Abs(d.Side - target) <= step) d.Side = target; else d.Side += Math.Sign(target - d.Side) * step;
            float lx = -(float) Math.Sin(dir), ly = (float) Math.Cos(dir);     // esquerda
            x += lx * d.Side; y += ly * d.Side;

            var m = BotMove(last.Move, b, nowTime, false);
            WithIdle(s, m);
            SetF(m, 0x18, x); SetF(m, 0x1c, y); SetF(m, 0x20, z); SetF(m, 0x24, Heading(dir));
            SetF(m, 0x28, (float) Math.Cos(dir) * d.Speed);
            SetF(m, 0x2c, (float) Math.Sin(dir) * d.Speed);
            SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            Siren(s, b, m);
            d.Last = m;
            // bot de crew fica no lado da crew: saiu do lado e esta longe de voce -> volta a andar dentro do lado dele
            if (cfg.CityWholeCity && Dist2(m, last.X, last.Y) > cfg.CityBotViewRange * cfg.CityBotViewRange)
            {
                var reg = RegionOf(ch.City, b);
                if (reg != null && (x < reg[0] || x > reg[1] || y < reg[2] || y > reg[3]))
                {
                    float rx, ry, rz, rd;
                    if (Rice.Game.CityTraffic.RoadSpotIn(ch.City, Rng, reg[0], reg[1], reg[2], reg[3], out rx, out ry, out rz, out rd))
                    {
                        var nc = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, rx, ry, 0f, 40f);
                        if (nc != null) { d.Cur = nc; d.Side = 0; }
                    }
                }
            }
            return m;
        }

        // nao deixa o bot ficar a menos de "gap" m de voce (parado do seu lado ele nao te empurra)
        static void KeepAway(byte[] m, Sample me, float gap)
        {
            if (gap <= 0) return;
            float x = F(m, 0x18), y = F(m, 0x1c), dx = x - me.X, dy = y - me.Y;
            float d = (float) Math.Sqrt(dx * dx + dy * dy);
            if (d >= gap) return;
            if (d < 0.01f) { dx = 1f; dy = 0f; d = 1f; }
            SetF(m, 0x18, me.X + dx / d * gap); SetF(m, 0x1c, me.Y + dy / d * gap);
            // indo na sua direcao: para
            float vx = F(m, 0x28), vy = F(m, 0x2c);
            if (vx * -dx + vy * -dy > 0) { SetF(m, 0x28, 0f); SetF(m, 0x2c, 0f); SetF(m, 0x30, 0f); SetF(m, 0x34, 0f); }
        }

        // Driver Dome: vai de ponto em ponto pelo caminho que voce fez; sem proximo ponto perto, pega o mais perto a
        // frente ou volta pelo caminho. Velocidade ate DomeDriveSpeedMax (80 mph = 35,8 m/s), mais devagar nas curvas.
        // Lugares (marcadores do mapa parkinglot_a.nif e as saidas para a cidade): o bot vai ate a porta, para, espera
        // DomeShopWaitSeconds, faz o que foi fazer (Errand), entra (some) e depois sai de frente para o lobby.
        class DomePlace { public string Name; public float X, Y; public bool Exit; }

        static List<DomePlace> Places()
        {
            var cfg = RiceServer.Config;
            var l = new List<DomePlace>();
            if (cfg.DomeShops != null)
                foreach (var kv in cfg.DomeShops) if (kv.Value != null && kv.Value.Length >= 2) l.Add(new DomePlace { Name = kv.Key, X = kv.Value[0], Y = kv.Value[1] });
            if (cfg.DomeExits != null)
                foreach (var e in cfg.DomeExits) if (e != null && e.Length >= 2) l.Add(new DomePlace { Name = "saida", X = e[0], Y = e[1], Exit = true });
            return l;
        }

        static int PlaceIndex(string name)
        {
            var l = Places();
            return l.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        static int NearestPoint(float x, float y, float fx, float fy, bool ahead)
        {
            var pts = domePoints;
            int nk = 0; float nd = float.MaxValue;
            for (int k = 0; k < pts.Count; k++)
            {
                float qx = pts[k][0] - x, qy = pts[k][1] - y, qd = qx * qx + qy * qy;
                if (ahead && qd > 1f && (qx * fx + qy * fy) < 0) qd *= 6f;
                if (qd < nd) { nd = qd; nk = k; }
            }
            return nk;
        }

        // proximo ponto do caminho que mais aproxima de (gx, gy); -1 = ja esta perto (vai direto) ; -2 = sem caminho
        static int Toward(DomeDriver d, float gx, float gy)
        {
            var pts = domePoints;
            var cur = pts[d.Target];
            float cd = (cur[0] - gx) * (cur[0] - gx) + (cur[1] - gy) * (cur[1] - gy);
            if (cd < 30f * 30f) return -1;
            int bk = -2; float bs = cd;
            for (int k = 0; k < pts.Count; k++)
            {
                if (k == d.Target || d.Recent.Contains(k)) continue;
                float dx = pts[k][0] - cur[0], dy = pts[k][1] - cur[1];
                if (dx * dx + dy * dy > 40f * 40f) continue;
                float sd = (pts[k][0] - gx) * (pts[k][0] - gx) + (pts[k][1] - gy) * (pts[k][1] - gy);
                if (sd < bs) { bs = sd; bk = k; }
            }
            return bk;
        }

        static int NextDomePoint(DomeDriver d, Bots.BotProfile b)
        {
            var pts = domePoints;
            var cur = pts[d.Target];
            var places = Places();
            var cfg = RiceServer.Config;
            if (d.ReturnHome && d.Home != null)
            {
                int k = Toward(d, F(d.Home, 0x18), F(d.Home, 0x1c));
                if (k == -1) { d.GoShop = true; return d.Target; }
                if (k >= 0) return k;
                d.Done = true; return d.Target;                  // sem caminho: estaciona onde estiver
            }
            if (d.Shop < 0 && d.Home == null && places.Count > 0)
            {
                double r = Rng.NextDouble();
                var exits = Enumerable.Range(0, places.Count).Where(i => places[i].Exit).ToList();
                var shops = Enumerable.Range(0, places.Count).Where(i => !places[i].Exit).ToList();
                if (exits.Count > 0 && r < cfg.DomeExitChance) d.Shop = exits[Rng.Next(exits.Count)];
                else if (shops.Count > 0 && r < cfg.DomeExitChance + cfg.DomeShopChance)
                {
                    d.Shop = shops[Rng.Next(shops.Count)];
                    d.Errand = ShopErrand(b, places[d.Shop].Name);
                }
            }
            if (d.Shop >= 0 && d.Shop < places.Count)
            {
                int k = Toward(d, places[d.Shop].X, places[d.Shop].Y);
                if (k == -1) { d.GoShop = true; return d.Target; }
                if (k >= 0) return k;
                d.Shop = -1; d.Errand = null;                    // sem caminho conhecido ate la: desiste
            }
            int n = d.Target + d.Step;
            // segue o caminho na ordem em que voce andou, mas de vez em quando muda de rota (vai mais longe)
            bool wander = Rng.NextDouble() < 0.25;
            if (!wander && n >= 0 && n < pts.Count && !d.Recent.Contains(n))
            {
                var q = pts[n];
                if ((q[0] - cur[0]) * (q[0] - cur[0]) + (q[1] - cur[1]) * (q[1] - cur[1]) < 35f * 35f) return n;
            }
            float fx = (float) Math.Cos(d.Dir), fy = (float) Math.Sin(d.Dir);
            int best = -1; float bd = float.MaxValue;
            for (int k = 0; k < pts.Count; k++)
            {
                if (k == d.Target || d.Recent.Contains(k)) continue;
                float dx = pts[k][0] - cur[0], dy = pts[k][1] - cur[1], dd = dx * dx + dy * dy;
                if (dd > 40f * 40f || dd < 1f) continue;
                float ahead = (dx * fx + dy * fy) / (float) Math.Sqrt(dd);
                if (ahead < -0.2f) continue;                                      // nao da re
                float score = (wander ? (1600f - dd) + (float) Rng.NextDouble() * 400f : dd) * (ahead > 0.5f ? 1f : 3f);
                if (score < bd) { bd = score; best = k; }
            }
            if (best >= 0) return best;
            d.Step = -d.Step;
            n = d.Target + d.Step;
            return n >= 0 && n < pts.Count ? n : Rng.Next(pts.Count);
        }

        // o que o bot faz quando entra numa loja sozinho
        static Action ShopErrand(Bots.BotProfile b, string place)
        {
            if (b == null || b.IsAlt) return null;          // seus corredores so vao ate a loja: nao compram nem equipam
            string p = (place ?? "").ToLowerInvariant();
            if (p.Contains("part")) return () => BotChatter.ShopParts(b);
            if (p.Contains("garag")) return () => BotChatter.ShopGarage(b, false);
            if (p.Contains("concession")) return () => BotChatter.ShopGarage(b, true);
            if (p.Contains("battle")) return () => BotChatter.ShopBattle(b);
            if (p.Contains("leil")) return () => BotChatter.ShopAuction(b);
            return null;
        }

        // Bot da sua party no lobby: sai da formacao, vai ate o lugar, para na porta, faz a acao e volta dirigindo ate voce.
        public static bool PartyErrand(Bots.BotProfile b, string place, Action act, double stay)
        {
            if (b == null || domePoints == null || domePoints.Count < 20) return false;
            int pi = PlaceIndex(place);
            if (pi < 0) return false;
            lock (Sync)
            {
                foreach (var s in states.Values)
                {
                    if (!IsDome(s.AreaId) || s.Trail.Count == 0 || s.Owner == null || !BotSocial.PartyBots(s.Owner).Contains(b)) continue;
                    if (s.PartyErrands.ContainsKey(b.Cid)) return false;
                    byte[] from;
                    if (!s.FollowLast.TryGetValue(b.Cid, out from)) return false;
                    var d = new DomeDriver { X = F(from, 0x18), Y = F(from, 0x1c), Z = F(from, 0x20), Dir = Unheading(F(from, 0x24)),
                                             Max = (float) Math.Max(10, RiceServer.Config.DomeDriveSpeedMax), Party = true };
                    d.Target = NearestPoint(d.X, d.Y, (float) Math.Cos(d.Dir), (float) Math.Sin(d.Dir), true);
                    d.Shop = pi; d.GoShop = false; d.Errand = act; d.Stay = stay;
                    s.PartyReturn.Remove(b.Cid);
                    s.PartyErrands[b.Cid] = d;
                    return true;
                }
            }
            return false;
        }

        // Manda um bot fazer algo num lugar do Driver Dome (ex.: comprar no leilao). Retorna false se ele nao esta no
        // lobby de nenhum jogador (quem chamou faz na hora).
        public static bool Errand(Bots.BotProfile b, string place, Action act, double stay)
        {
            if (b == null || domePoints == null || domePoints.Count < 20) return false;
            int pi = PlaceIndex(place);
            if (pi < 0) return false;
            lock (Sync)
            {
                foreach (var s in states.Values)
                {
                    if (!IsDome(s.AreaId) || s.Trail.Count == 0) continue;
                    DomeDriver d;
                    if (!s.DomeDrivers.TryGetValue(b.Cid, out d))
                    {
                        byte[] spot;
                        if (!s.Parked.TryGetValue(b.Cid, out spot)) continue;
                        // estava estacionado: sai, vai la e depois volta para a mesma vaga
                        float x = F(spot, 0x18), y = F(spot, 0x1c);
                        d = new DomeDriver { X = x, Y = y, Z = F(spot, 0x20), Dir = Unheading(F(spot, 0x24)), Home = spot,
                                             Max = (float) Math.Max(10, RiceServer.Config.DomeDriveSpeedMin) };
                        d.Target = NearestPoint(x, y, (float) Math.Cos(d.Dir), (float) Math.Sin(d.Dir), true);
                        s.Parked.Remove(b.Cid);
                        s.DomeDrivers[b.Cid] = d;
                    }
                    if (d.InsideUntil != DateTime.MinValue) continue;
                    d.Shop = pi; d.GoShop = false; d.ArriveAt = DateTime.MinValue; d.Errand = act; d.Stay = stay; d.ReturnHome = false;
                    return true;
                }
            }
            return false;
        }

        // o bot "entra" (some) por N s: usado quando a corrida da Battle Zone comeca; depois ele aparece na porta
        public static void Vanish(Bots.BotProfile b, double seconds)
        {
            if (b == null) return;
            lock (Sync)
            {
                foreach (var s in states.Values)
                {
                    DomeDriver d;
                    if (!s.DomeDrivers.TryGetValue(b.Cid, out d)) continue;
                    d.Errand = null; d.AtShop = seconds <= 5 && d.Shop >= 0;
                    d.InsideUntil = DateTime.UtcNow.AddSeconds(Math.Max(0.5, seconds));
                }
            }
        }

        static string PlaceName(string p)
        {
            switch ((p ?? "").ToLowerInvariant())
            {
                case "leilao": return "leilao";
                case "concessionaria": return "concessionaria";
                case "battle zone": return "Battle Zone";
                case "garagem": return "garagem";
                case "part shop": return "Part Shop";
                case "crew center": return "Crew Center";
            }
            return p;
        }

        // bot da party no Driver Dome: a cada ~PartyDomeErrandMinutes cada um (independente dos outros) vai a uma loja, compra/olha,
        // fica um pouco na porta e volta a te seguir. Avisa no chat da party quando sai e quando volta.
        static byte[] PartyErrandMove(PlayerState s, Bots.BotProfile b, uint nowTime, Sample last)
        {
            var cfg = RiceServer.Config;
            if (cfg.PartyDomeErrandMinutes <= 0 || domePoints == null || domePoints.Count < 20 || b.Special) return null;   // seus corredores tambem vao (so olham: ShopErrand)
            DomeDriver d;
            if (!s.PartyErrands.TryGetValue(b.Cid, out d))
            {
                DateTime next;
                double avg = cfg.PartyDomeErrandMinutes * 60;
                if (!s.NextPartyErrand.TryGetValue(b.Cid, out next)) { s.NextPartyErrand[b.Cid] = DateTime.UtcNow.AddSeconds(30 + Rng.NextDouble() * avg); return null; }
                if (DateTime.UtcNow < next) return null;      // cada um no seu tempo (varios podem sair ao mesmo tempo)
                // so quando voce esta parado no lobby ha um tempo
                if (s.StillSince == DateTime.MinValue || (DateTime.UtcNow - s.StillSince).TotalSeconds < Math.Max(0, cfg.PartyDomeErrandIdle)) return null;
                s.NextPartyErrand[b.Cid] = DateTime.UtcNow.AddSeconds(avg * (0.6 + Rng.NextDouble() * 0.8));
                byte[] from;
                if (!s.FollowLast.TryGetValue(b.Cid, out from)) return null;
                var places = Places();
                var shops = Enumerable.Range(0, places.Count).Where(k => !places[k].Exit).ToList();
                if (shops.Count == 0) return null;
                int pi = shops[Rng.Next(shops.Count)];
                d = new DomeDriver { X = F(from, 0x18), Y = F(from, 0x1c), Z = F(from, 0x20), Dir = Unheading(F(from, 0x24)),
                                     Max = (float) Math.Max(10, cfg.DomeDriveSpeedMax), Party = true };
                d.Target = NearestPoint(d.X, d.Y, (float) Math.Cos(d.Dir), (float) Math.Sin(d.Dir), true);
                d.Shop = pi; d.GoShop = false; d.Errand = ShopErrand(b, places[pi].Name);
                d.Stay = cfg.DomeShopStayMin + Rng.NextDouble() * Math.Max(0, cfg.DomeShopStayMax - cfg.DomeShopStayMin);
                s.PartyErrands[b.Cid] = d;
                string where = PlaceName(places[pi].Name);
                BotChatter.Tell(b, BotChatter.PickLine("Vou ali no " + where + " rapidinho, ja volto!", "Pera, vou dar um pulo no " + where + ".",
                                                       "Ja volto, vou no " + where + "!"));
            }
            var m = DomeMove(s, b, d, nowTime, last);
            if (d.Done)
            {
                // saiu da loja: volta dirigindo ate o lugar dele na formacao (sem pular)
                s.PartyErrands.Remove(b.Cid);
                if (d.Last != null) s.FollowLast[b.Cid] = d.Last;
                s.Followers.Remove(b.Cid);
                s.PartyReturn[b.Cid] = new Pursuer { X = d.X, Y = d.Y, Z = d.Z, Dir = d.Dir, Speed = 0 };
                BotChatter.Tell(b, BotChatter.PickLine("Voltei! Bora.", "To de volta, partiu!", "Pronto, voltei."));
                return PartyReturnMove(s, b, 0, nowTime);
            }
            return m;
        }

        // volta da loja: dirige ate o lugar da formacao atras de voce (1o e 2o atras e de lado, 3o e 4o atras deles) e
        // fica ali enquanto voce estiver parado; quando voce sai andando e ele ja esta perto, volta a te seguir normal
        static byte[] PartyReturnMove(PlayerState s, Bots.BotProfile b, int i, uint nowTime)
        {
            Pursuer f;
            if (!s.PartyReturn.TryGetValue(b.Cid, out f) || s.Trail.Count == 0) return null;
            var cfg = RiceServer.Config;
            const float dt = (float) TickSeconds;
            var last = s.Trail[s.Trail.Count - 1];
            float px = last.X, py = last.Y;
            float pvx = F(last.Move, 0x28), pvy = F(last.Move, 0x2c);
            float pv = (float) Math.Sqrt(pvx * pvx + pvy * pvy);
            if ((DateTime.UtcNow - last.At).TotalSeconds > 1.5) pv = 0;
            float fwdx, fwdy;
            if (pv > 0.5f) { fwdx = pvx / pv; fwdy = pvy / pv; }
            else { float pd = Unheading(F(last.Move, 0x24)); fwdx = (float) Math.Cos(pd); fwdy = (float) Math.Sin(pd); }
            float lx = -fwdy, ly = fwdx;
            int slot = Math.Max(0, BotSocial.PartyBots(s.Owner).IndexOf(b));
            float gap = cfg.CityBotFollowGap + 0.5f;
            float back = Math.Max(Math.Max(1.5f, cfg.CityBotMinDistance) + 1f, gap * (1 + slot / 2));
            float side = (cfg.CityBotSideGap + 0.5f) * (slot % 2 == 0 ? 1f : -1f);
            float sx = px - fwdx * back + lx * side, sy = py - fwdy * back + ly * side;
            float ex = sx - f.X, ey = sy - f.Y, dS = (float) Math.Sqrt(ex * ex + ey * ey);
            // voce saiu andando e ele ja esta perto: volta a te seguir (replay)
            if (pv > 3f && dS < 10f) { s.PartyReturn.Remove(b.Cid); s.Followers.Remove(b.Cid); return null; }
            var cst = BotSocial.CarStats(b);
            float vmax = Math.Min(28f + cst[0] * 0.16f, (float) Math.Max(10, cfg.DomeDriveSpeedMax));
            float desired = dS < 0.8f ? 0f : Math.Min(vmax, 2f + dS * 0.9f);
            float tx = sx, ty = sy;
            if (dS < 0.8f) { tx = f.X + fwdx * 5f; ty = f.Y + fwdy * 5f; }
            float acc = desired > f.Speed ? 6f + cst[1] * 0.04f : 18f;
            if (Math.Abs(desired - f.Speed) <= acc * dt) f.Speed = desired; else f.Speed += Math.Sign(desired - f.Speed) * acc * dt;
            if (dS >= 0.8f) Steer(f, tx, ty, dt);
            else { float want = (float) Math.Atan2(fwdy, fwdx), turn = Wrap(want - f.Dir); f.Dir += Math.Max(-1.5f * dt, Math.Min(1.5f * dt, turn)); }
            f.X += (float) Math.Cos(f.Dir) * f.Speed * dt;
            f.Y += (float) Math.Sin(f.Dir) * f.Speed * dt;
            f.Z = NearestZ(s.Trail, f.X, f.Y, F(last.Move, 0x20));
            var m = BotMove(last.Move, b, nowTime, f.Speed <= 0.1f);
            if (f.Speed < 3f) WithIdle(s, m); else ApplyDriveState(s, m, f.X, f.Y, f.Speed);
            SetF(m, 0x18, f.X); SetF(m, 0x1c, f.Y); SetF(m, 0x20, f.Z); SetF(m, 0x24, Heading(f.Dir));
            SetF(m, 0x28, (float) Math.Cos(f.Dir) * f.Speed); SetF(m, 0x2c, (float) Math.Sin(f.Dir) * f.Speed);
            SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            s.FollowLast[b.Cid] = m;
            return m;
        }

        static byte[] DomeMove(PlayerState s, Bots.BotProfile b, DomeDriver d, uint nowTime, Sample last)
        {
            var pts = domePoints;
            if (pts == null || pts.Count < 2) return null;
            const float dt = (float) TickSeconds;
            var cfg = RiceServer.Config;
            if (d.Target < 0 || d.Target >= pts.Count) d.Target = Rng.Next(pts.Count);
            var places = Places();
            // dentro da loja / fora do lobby: nao aparece; quando sai, aparece de frente para o lobby
            if (d.InsideUntil != DateTime.MinValue)
            {
                if (DateTime.UtcNow < d.InsideUntil)
                {
                    if (!d.AtShop) return null;                     // fora do lobby (na cidade): nao aparece
                    // na loja: fica parado na porta, aparecendo
                    var pm = BotMove(last.Move, b, nowTime, true);
                    WithIdle(s, pm);
                    SetF(pm, 0x18, d.X); SetF(pm, 0x1c, d.Y); SetF(pm, 0x20, d.Z); SetF(pm, 0x24, Heading(d.Dir));
                    d.Last = pm;
                    return pm;
                }
                d.InsideUntil = DateTime.MinValue;
                if (d.Party) { d.Done = true; return null; }     // bot da party: terminou na loja, volta a te seguir
                if (!d.AtShop && d.Shop >= 0 && d.Shop < places.Count && places[d.Shop].Exit) d.Swap = true;   // volta outro bot
                d.AtShop = false;
                bool wasExit = d.Shop >= 0 && d.Shop < places.Count && places[d.Shop].Exit;
                if (wasExit)
                {
                    // volta para o lobby por uma das saidas
                    var ex = places.Where(q => q.Exit).ToList();
                    var e = ex[Rng.Next(ex.Count)];
                    d.X = e.X; d.Y = e.Y;
                }
                float cx = -d.X, cy = -d.Y, cl = (float) Math.Sqrt(cx * cx + cy * cy);
                if (cl > 0.1f) { cx /= cl; cy /= cl; } else { cx = 1f; cy = 0f; }
                if (wasExit)
                {
                    d.Dir = (float) Math.Atan2(cy, cx);            // entra no lobby de frente para o meio dele
                    d.X += cx * 3f; d.Y += cy * 3f; d.Speed = 6f;
                }
                else d.Speed = 0f;                                  // saindo da loja: so arranca e faz a curva
                d.Shop = -1; d.GoShop = false; d.ArriveAt = DateTime.MinValue; d.Errand = null; d.Stay = 0;
                d.Target = NearestPoint(d.X, d.Y, cx, cy, true); d.Recent.Clear();
                if (d.Home != null) d.ReturnHome = true;
            }
            float gx, gy, gz = d.Z;
            bool direct = d.GoShop;
            if (direct && d.ReturnHome && d.Home != null) { gx = F(d.Home, 0x18); gy = F(d.Home, 0x1c); gz = F(d.Home, 0x20); }
            else if (direct && d.Shop >= 0 && d.Shop < places.Count) { gx = places[d.Shop].X; gy = places[d.Shop].Y; }
            else { direct = false; gx = pts[d.Target][0]; gy = pts[d.Target][1]; gz = pts[d.Target][2]; }
            float dx = gx - d.X, dy = gy - d.Y, dist = (float) Math.Sqrt(dx * dx + dy * dy);
            if (direct && dist < 4f)
            {
                if (d.ReturnHome) { d.Done = true; return null; }      // de volta na vaga
                // na porta: para, espera, faz o que veio fazer e entra
                d.Speed = Math.Max(0f, d.Speed - 14f * dt);
                if (d.Speed < 0.5f)
                {
                    d.Speed = 0;
                    if (d.ArriveAt == DateTime.MinValue) d.ArriveAt = DateTime.UtcNow;
                    bool exit = d.Shop >= 0 && d.Shop < places.Count && places[d.Shop].Exit;
                    if ((DateTime.UtcNow - d.ArriveAt).TotalSeconds >= (exit ? 0.5 : Math.Max(0, cfg.DomeShopWaitSeconds)))
                    {
                        var act = d.Errand; d.Errand = null;
                        if (act != null) { try { act(); } catch (Exception ex) { Log.WriteError("Bot na loja: " + ex.Message); } }
                        double stay = d.Stay > 0 ? d.Stay : exit ? cfg.DomeExitStayMin + Rng.NextDouble() * Math.Max(0, cfg.DomeExitStayMax - cfg.DomeExitStayMin)
                                                     : cfg.DomeShopStayMin + Rng.NextDouble() * Math.Max(0, cfg.DomeShopStayMax - cfg.DomeShopStayMin);
                        d.InsideUntil = DateTime.UtcNow.AddSeconds(stay);
                        d.AtShop = !exit;                               // loja: fica parado na porta (nao some); saida: some
                        if (exit) return null;
                    }
                }
            }
            else if (!direct && (dist < 3.5f || dist > 60f))
            {
                if (dist > 60f) { d.X = gx; d.Y = gy; d.Z = gz; }      // perdeu o caminho: volta para ele
                d.Recent.Enqueue(d.Target); while (d.Recent.Count > 60) d.Recent.Dequeue();   // nao fica rodando no mesmo lugar
                d.Target = NextDomePoint(d, b);
                if (!d.GoShop) { gx = pts[d.Target][0]; gy = pts[d.Target][1]; gz = pts[d.Target][2]; }
                dx = gx - d.X; dy = gy - d.Y; dist = (float) Math.Sqrt(dx * dx + dy * dy);
            }
            float fx = (float) Math.Cos(d.Dir), fy = (float) Math.Sin(d.Dir);
            if (!(direct && dist < 4f))
            {
                float want = (float) Math.Atan2(dy, dx), turn = Wrap(want - d.Dir);
                float rate = 2.6f * dt;
                d.Dir += Math.Max(-rate, Math.Min(rate, turn));
                float desired = d.Max;
                if (direct && dist < 16f) desired = Math.Min(desired, Math.Max(2f, dist * 0.7f));   // chegando: freia ate parar
                if (Math.Abs(turn) > 1.2f) desired = Math.Min(desired, 8f);
                else if (Math.Abs(turn) > 0.6f) desired = Math.Min(desired, 15f);
                else if (Math.Abs(turn) > 0.3f) desired = Math.Min(desired, 24f);
                // voce na frente: freia
                fx = (float) Math.Cos(d.Dir); fy = (float) Math.Sin(d.Dir);
                float px = last.X - d.X, py = last.Y - d.Y;
                float a = px * fx + py * fy;
                if (a > 0 && a < 14f && Math.Abs(-px * fy + py * fx) < 3f) desired = Math.Min(desired, 2f);
                float acc = desired > d.Speed ? 6f : 14f;
                if (Math.Abs(desired - d.Speed) <= acc * dt) d.Speed = desired; else d.Speed += Math.Sign(desired - d.Speed) * acc * dt;
            }
            d.X += fx * d.Speed * dt; d.Y += fy * d.Speed * dt;
            d.Z += (gz - d.Z) * 0.3f;
            var m = BotMove(last.Move, b, nowTime, d.Speed <= 0);
            WithIdle(s, m);
            SetF(m, 0x18, d.X); SetF(m, 0x1c, d.Y); SetF(m, 0x20, d.Z); SetF(m, 0x24, Heading(d.Dir));
            SetF(m, 0x28, fx * d.Speed); SetF(m, 0x2c, fy * d.Speed); SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            d.Last = m;
            return m;
        }

        // ======================================================================================================
        // Moradores da cidade (CityWholeCity): grupos, HUV com sirene, ida a Battle Zone pelo posto
        // ======================================================================================================
        class Trip
        {
            public int Phase;                    // 0 indo ao posto, 1 parado no posto, 2 dentro da Battle Zone, 3 voltando
            public List<float[]> Route; public int Idx;
            public float X, Y, Z, Dir, Speed;
            public float[] Post;
            public float HomeX, HomeY;
            public byte[] HomeParked;            // estava estacionado: estaciona de novo no mesmo lugar
            public DateTime Until;
            public byte[] Last;
        }

        static byte[] sirenState;                // estado do seu carro com a sirene ligada (copiado numa perseguicao de HUV)

        static void Siren(PlayerState s, Bots.BotProfile b, byte[] m, bool force = false)
        {
            if (sirenState == null || m == null) return;
            if (force || s.SirenBots.Contains(b.Cid)) Array.Copy(sirenState, 0, m, MoveSize, StateSize);
        }

        // ponto da crew na cidade (de onde os membros sempre aparecem); fora de crew = false
        static readonly Dictionary<string, float[]> crewAnchors = new Dictionary<string, float[]>();
        static bool CrewAnchor(int city, Bots.BotProfile b, out float x, out float y)
        {
            x = y = 0;
            var crew = Crew.Of(b.Cid);
            if (crew == null) return false;
            string key = city + ":" + crew.Tid;
            float[] a;
            if (!crewAnchors.TryGetValue(key, out a))
            {
                float[] fixedA;
                float[] reg; float[] bb = Rice.Game.CityTraffic.Bounds(city);
                if (RiceServer.Config.CrewSpots != null && RiceServer.Config.CrewSpots.TryGetValue(crew.Name ?? "", out fixedA) && fixedA != null && fixedA.Length >= 2)
                    a = new[] { fixedA[0], fixedA[1] };
                else if (bb != null && RiceServer.Config.CrewLadder && RiceServer.Config.CrewRegions != null && RiceServer.Config.CrewRegions.TryGetValue(crew.Name ?? "", out reg) && reg != null && reg.Length >= 4)
                {
                    // ponto da crew no lado dela da cidade (Rookies esquerda, MoonFlow direita, MitoKings sul...)
                    var r = new Random((int) (crew.Tid * 31 + city));
                    float rx, ry, rz, rd;
                    float x0r = bb[0] + (bb[1] - bb[0]) * reg[0], x1r = bb[0] + (bb[1] - bb[0]) * reg[1];
                    float y0r = bb[2] + (bb[3] - bb[2]) * reg[2], y1r = bb[2] + (bb[3] - bb[2]) * reg[3];
                    a = Rice.Game.CityTraffic.RoadSpotIn(city, r, x0r, x1r, y0r, y1r, out rx, out ry, out rz, out rd) ? new[] { rx, ry } : null;
                }
                else
                {
                    var r = new Random((int) (crew.Tid * 31 + city));
                    float rx, ry, rz, rd;
                    a = Rice.Game.CityTraffic.RoadSpot(city, r, 0f, 0f, 0f, 1e7f, out rx, out ry, out rz, out rd) ? new[] { rx, ry } : null;
                }
                crewAnchors[key] = a;
            }
            if (a == null) return false;
            x = a[0]; y = a[1];
            return true;
        }

        // ---- saida do lobby e HUV na cidade (chamada no chat) ---------------------------------------------------
        // vai ate a saida do lobby mais perto e some por N s (foi para a cidade dele)
        public static void LeaveLobby(Bots.BotProfile b, double seconds)
        {
            if (b == null || domePoints == null || domePoints.Count < 20) return;
            var places = Places();
            lock (Sync)
            {
                foreach (var s in states.Values)
                {
                    if (!IsDome(s.AreaId) || s.Trail.Count == 0) continue;
                    DomeDriver d;
                    if (!s.DomeDrivers.TryGetValue(b.Cid, out d))
                    {
                        byte[] spot;
                        if (!s.Parked.TryGetValue(b.Cid, out spot)) continue;
                        float x = F(spot, 0x18), y = F(spot, 0x1c);
                        d = new DomeDriver { X = x, Y = y, Z = F(spot, 0x20), Dir = Unheading(F(spot, 0x24)), Home = spot,
                                             Max = (float) Math.Max(10, RiceServer.Config.DomeDriveSpeedMax) };
                        d.Target = NearestPoint(x, y, (float) Math.Cos(d.Dir), (float) Math.Sin(d.Dir), true);
                        s.Parked.Remove(b.Cid);
                        s.DomeDrivers[b.Cid] = d;
                    }
                    if (d.InsideUntil != DateTime.MinValue) continue;
                    int best = -1; float bd = float.MaxValue;
                    for (int i = 0; i < places.Count; i++)
                    {
                        if (!places[i].Exit) continue;
                        float dd = (places[i].X - d.X) * (places[i].X - d.X) + (places[i].Y - d.Y) * (places[i].Y - d.Y);
                        if (dd < bd) { bd = dd; best = i; }
                    }
                    if (best < 0) continue;
                    d.Shop = best; d.GoShop = false; d.ArriveAt = DateTime.MinValue; d.Errand = null; d.Stay = seconds; d.ReturnHome = false;
                    d.Max = (float) Math.Max(d.Max, RiceServer.Config.DomeDriveSpeedMax);
                }
            }
        }

        class CityAct { public int City; public long Leader; public List<long> Members; public DateTime Until; public int Id; }
        static readonly List<CityAct> cityActs = new List<CityAct>();
        static int nextAct = 1;

        // grupo de HUV na cidade: quando voce estiver nessa cidade, eles aparecem andando juntos com a sirene
        public static void CityHuv(int city, Bots.BotProfile leader, List<Bots.BotProfile> members, double minutes)
        {
            lock (Sync)
            {
                cityActs.RemoveAll(a => DateTime.UtcNow > a.Until || a.Leader == leader.Cid);
                cityActs.Add(new CityAct { City = city, Leader = leader.Cid, Members = members.Select(m => m.Cid).ToList(),
                                           Until = DateTime.UtcNow.AddMinutes(minutes), Id = nextAct++ });
            }
        }

        static void ApplyCityActs(Rice.Game.Character ch, PlayerState s, List<Bots.BotProfile> party)
        {
            var cfg = RiceServer.Config;
            foreach (var a in cityActs.Where(x => x.City == ch.City && DateTime.UtcNow < x.Until && !s.AppliedActs.Contains(x.Id)).ToList())
            {
                var leader = Bots.ByCid(a.Leader);
                if (leader == null || party.Contains(leader) || s.Trips.ContainsKey(a.Leader)) { s.AppliedActs.Add(a.Id); continue; }
                Driver d;
                if (!s.Drivers.TryGetValue(a.Leader, out d))
                {
                    byte[] spot;
                    if (!s.Parked.TryGetValue(a.Leader, out spot)) continue;    // ainda nao apareceu nesta cidade
                    var cur = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, F(spot, 0x18), F(spot, 0x1c), 0f, 60f);
                    if (cur == null) { s.AppliedActs.Add(a.Id); continue; }
                    s.Parked.Remove(a.Leader);
                    s.Drivers[a.Leader] = d = new Driver { Cur = cur, Speed = (float) cfg.CityDriveSpeedMax };
                }
                if (s.GroupUntil.ContainsKey(a.Leader)) EndGroup(s, a.Leader);
                double secs = Math.Max(30, (a.Until - DateTime.UtcNow).TotalSeconds);
                var mem = a.Members.Select(Bots.ByCid).Where(m => m != null && !party.Contains(m)).ToList();
                Bots.Group(leader, mem, secs);
                s.GroupUntil[a.Leader] = DateTime.UtcNow.AddSeconds(secs);
                s.BaseSpeed[a.Leader] = d.Speed;
                d.Speed = Math.Min(40f, d.Speed * (float) Math.Max(1, cfg.CityHuvSpeedFactor));
                s.SirenBots.Add(a.Leader);
                s.AppliedActs.Add(a.Id);
                Log.WriteLine("Rua: {0} esta no HUV nesta cidade{1}.", leader.Name, mem.Count > 0 ? " com " + string.Join(", ", mem.Select(m => m.Name)) : " (sozinho)");
            }
        }

        // retangulo do lado da crew do bot nesta cidade (CrewRegions); null = sem lado (anda pela cidade toda)
        static float[] RegionOf(int city, Bots.BotProfile b)
        {
            var crew = Crew.Of(b.Cid);
            float[] reg;
            if (crew == null || !crew.IsBot || !RiceServer.Config.CrewLadder || RiceServer.Config.CrewRegions == null || !RiceServer.Config.CrewRegions.TryGetValue(crew.Name ?? "", out reg)
                || reg == null || reg.Length < 4 || Bots.CityOfCrew(crew.Name) != city) return null;
            var bb = Rice.Game.CityTraffic.Bounds(city);
            if (bb == null) return null;
            return new[] { bb[0] + (bb[1] - bb[0]) * reg[0], bb[0] + (bb[1] - bb[0]) * reg[1], bb[2] + (bb[3] - bb[2]) * reg[2], bb[2] + (bb[3] - bb[2]) * reg[3] };
        }

        // ---- postos da Battle Zone: aprendidos quando voce entra na Battle Zone pela rua (battle_posts.json) ----
        const string PostsFile = "battle_posts2.json";      // (o battle_posts.json antigo tinha postos errados)
        static Dictionary<int, List<float[]>> posts;
        static readonly Dictionary<ulong, Tuple<DateTime, int, float[]>> lastLeave = new Dictionary<ulong, Tuple<DateTime, int, float[]>>();

        static Dictionary<int, List<float[]>> Posts()
        {
            if (posts != null) return posts;
            try { posts = File.Exists(PostsFile) ? JsonConvert.DeserializeObject<Dictionary<int, List<float[]>>>(File.ReadAllText(PostsFile)) : null; }
            catch (Exception ex) { Log.WriteError(PostsFile + ": " + ex.Message); }
            if (posts == null) posts = new Dictionary<int, List<float[]>>();
            var cfgP = RiceServer.Config.CityBattlePosts;
            if (cfgP != null)
                foreach (var kv in cfgP)
                {
                    List<float[]> l;
                    if (!posts.TryGetValue(kv.Key, out l)) posts[kv.Key] = l = new List<float[]>();
                    foreach (var pt in kv.Value) if (pt != null && pt.Length >= 2 && !l.Any(q => Near(q, pt[0], pt[1], 30f))) l.Add(pt);
                }
            return posts;
        }

        static bool Near(float[] q, float x, float y, float r) { return (q[0] - x) * (q[0] - x) + (q[1] - y) * (q[1] - y) < r * r; }

        // o jogo salva a posicao do carro (162 SaveCarPos) logo antes de abrir a lista de salas da Battle Zone no posto
        public static void NotePost(Rice.Game.Character ch, int city, float x, float y, float z, float rot)
        {
            if (ch == null) return;
            if (city < 1 || city > 4) city = ch.City;
            lock (Sync) lastLeave[ch.CID] = Tuple.Create(DateTime.UtcNow, city, new[] { x, y, z, rot, (float) ch.City });
        }

        // saindo da rua (loja, Battle Zone...): guarda onde voce estava
        public static void NoteLeave(Rice.Game.Character ch)
        {
            if (ch == null || ch.Area == null) return;
            lock (Sync)
            {
                PlayerState s;
                if (!states.TryGetValue(ch.CID, out s) || s.Trail.Count == 0 || ch.InNeoBase || IsDome(ch.Area.ID) || !IsCity(ch.Area.ID)) return;
                var m = s.Trail[s.Trail.Count - 1].Move;
                lastLeave[ch.CID] = Tuple.Create(DateTime.UtcNow, ch.City, new[] { F(m, 0x18), F(m, 0x1c), F(m, 0x20), F(m, 0x24) });
            }
        }

        // entrou na Battle Zone logo depois de sair da rua: esse lugar e um posto da Battle Zone daquela cidade
        public static void LearnPost(Rice.Game.Character ch)
        {
            if (ch == null) return;
            lock (Sync)
            {
                Tuple<DateTime, int, float[]> lv;
                // so vale a posicao salva pelo SaveCarPos (5 itens) nos ultimos 10 s
                if (!lastLeave.TryGetValue(ch.CID, out lv) || lv.Item3.Length < 5 || (DateTime.UtcNow - lv.Item1).TotalSeconds > 10 || lv.Item2 <= 0) return;
                lastLeave.Remove(ch.CID);
                // a cidade que o jogo mandou; se o ponto nao estiver numa rua dela, a cidade pela sua posicao
                int city = lv.Item2;
                float rx, ry, rz, rd;
                if (!Rice.Game.CityTraffic.RoadSpot(city, Rng, lv.Item3[0], lv.Item3[1], 0f, 60f, out rx, out ry, out rz, out rd))
                {
                    int alt = (int) lv.Item3[4];
                    if (alt == city || alt < 1 || !Rice.Game.CityTraffic.RoadSpot(alt, Rng, lv.Item3[0], lv.Item3[1], 0f, 60f, out rx, out ry, out rz, out rd)) return;
                    city = alt;
                }
                lv = Tuple.Create(lv.Item1, city, new[] { lv.Item3[0], lv.Item3[1], lv.Item3[2], lv.Item3[3] });
                var all = Posts();
                List<float[]> l;
                if (!all.TryGetValue(lv.Item2, out l)) all[lv.Item2] = l = new List<float[]>();
                if (l.Any(q => Near(q, lv.Item3[0], lv.Item3[1], 30f))) return;
                l.Add(lv.Item3);
                try { File.WriteAllText(PostsFile, JsonConvert.SerializeObject(all)); } catch (Exception) { }
                Log.WriteLine("Rua: posto da Battle Zone aprendido na cidade {0} ({1:0}, {2:0}); {3} posto(s) nessa cidade.", lv.Item2, lv.Item3[0], lv.Item3[1], l.Count);
            }
        }

        static float[] PosOf(PlayerState s, long cid)
        {
            Driver d; byte[] m;
            if (s.Drivers.TryGetValue(cid, out d) && d.Last != null) m = d.Last;
            else if (!s.Parked.TryGetValue(cid, out m)) return null;
            return new[] { F(m, 0x18), F(m, 0x1c), F(m, 0x20), F(m, 0x24) };
        }

        static void EndGroup(PlayerState s, long leader)
        {
            // grupo de HUV / Battle Zone acabou: quem e de crew de jogador rende metade para o lider da crew
            if (s.SirenBots.Contains(leader) || s.Trips.ContainsKey(leader))
            {
                var who = Bots.Links().Where(kv => kv.Value.Leader == leader).Select(kv => kv.Key).Concat(new[] { leader }).ToList();
                foreach (var cid in who)
                {
                    var gb = Bots.ByCid(cid);
                    if (gb == null) continue;
                    long earned = 500 + 120L * Math.Max(1, gb.Level);
                    if (gb.Mito < 0) gb.Mito = 0;
                    gb.Mito += earned;
                    CrewLadder.LeaderShare(cid, earned, "grupo");
                }
            }
            Bots.Ungroup(leader);
            s.GroupUntil.Remove(leader);
            s.SirenBots.Remove(leader);
            float sp; Driver d;
            if (s.BaseSpeed.TryGetValue(leader, out sp) && s.Drivers.TryGetValue(leader, out d)) d.Speed = sp;
            s.BaseSpeed.Remove(leader);
        }

        // um tick dos moradores: termina grupos, move as viagens ate o posto e as vezes monta um grupo novo
        static void CityLife(Rice.Game.Character ch, PlayerState s, List<Bots.BotProfile> party, uint nowTime, Sample last,
                             List<byte[]> moves, HashSet<uint> want, float range)
        {
            var cfg = RiceServer.Config;
            var now = DateTime.UtcNow;
            foreach (var g in s.GroupUntil.Where(kv => now > kv.Value && !s.Trips.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList()) EndGroup(s, g);
            ApplyCityActs(ch, s, party);

            foreach (var kv in s.Trips.ToList())
            {
                var b = Bots.ByCid(kv.Key);
                if (b == null || party.Contains(b)) { s.Trips.Remove(kv.Key); EndGroup(s, kv.Key); continue; }
                var m = TripMove(ch, s, b, kv.Value, nowTime, last);
                if (m == null) continue;
                if (Dist2(m, last.X, last.Y) > range * range) continue;
                moves.Add(m);
                want.Add(BotSocial.SerialOf(b));
            }

            if (now < s.NextGroup) return;
            s.NextGroup = now.AddSeconds(Math.Max(20, cfg.CityGroupGapMin) + Rng.NextDouble() * Math.Max(0, cfg.CityGroupGapMax - cfg.CityGroupGapMin));
            var busy = new HashSet<long>(s.GroupUntil.Keys.Concat(s.Trips.Keys));
            var cand = s.Drivers.Keys.Concat(s.Parked.Keys).Distinct().Select(Bots.ByCid)
                        .Where(b => b != null && b.HomeCity == ch.City && !b.Special && !Bots.IsAce(b) && !party.Contains(b)
                                    && !busy.Contains(b.Cid) && Bots.LinkOf(b.Cid) == null).ToList();
            if (cand.Count < 2) return;
            var postList = Posts().ContainsKey(ch.City) ? Posts()[ch.City] : null;
            double roll = Rng.NextDouble();
            string act = postList != null && postList.Count > 0 && roll < cfg.CityGroupBattleChance ? "battle"
                       : roll < cfg.CityGroupBattleChance + cfg.CityGroupHuvChance ? "huv" : "passeio";
            var leaders = act == "battle" ? cand : cand.Where(b => s.Drivers.ContainsKey(b.Cid)).ToList();
            if (leaders.Count == 0) return;
            var leader = leaders[Rng.Next(leaders.Count)];
            var lp = PosOf(s, leader.Cid);
            if (lp == null) return;
            var lc = Crew.Of(leader.Cid);
            // os de crew so andam com a propria crew; os sem crew com os sem crew
            var mates = cand.Where(b => b != leader && (lc == null ? Crew.Of(b.Cid) == null : (Crew.Of(b.Cid) != null && Crew.Of(b.Cid).Tid == lc.Tid)))
                            .Select(b => new { B = b, P = PosOf(s, b.Cid) }).Where(x => x.P != null)
                            .OrderBy(x => (x.P[0] - lp[0]) * (x.P[0] - lp[0]) + (x.P[1] - lp[1]) * (x.P[1] - lp[1]))
                            .Take(Rng.Next(1, Math.Max(2, cfg.CityGroupMax))).Select(x => x.B).ToList();
            if (mates.Count == 0) return;
            if (act == "battle")
            {
                var post = postList.OrderBy(q => (q[0] - lp[0]) * (q[0] - lp[0]) + (q[1] - lp[1]) * (q[1] - lp[1])).First();
                List<float[]> route = null;
                try { route = Rice.Game.CityTraffic.Route(ch.City, lp[0], lp[1], post[0], post[1]); } catch (Exception) { route = null; }
                if (route == null || route.Count == 0) return;
                byte[] parkedSpot; s.Parked.TryGetValue(leader.Cid, out parkedSpot);
                s.Parked.Remove(leader.Cid); s.Drivers.Remove(leader.Cid);
                s.Trips[leader.Cid] = new Trip { Phase = 0, Route = route, X = lp[0], Y = lp[1], Z = lp[2], Dir = Unheading(lp[3]), Post = post,
                                                 HomeX = lp[0], HomeY = lp[1], HomeParked = parkedSpot };
                Bots.Group(leader, mates, 3600);
                s.GroupUntil[leader.Cid] = now.AddHours(1);
            }
            else
            {
                double secs = act == "huv" ? 120 + Rng.Next(120) : 120 + Rng.Next(180);
                Bots.Group(leader, mates, secs);
                s.GroupUntil[leader.Cid] = now.AddSeconds(secs);
                if (act == "huv")
                {
                    var d = s.Drivers[leader.Cid];
                    s.BaseSpeed[leader.Cid] = d.Speed;
                    d.Speed = Math.Min(40f, d.Speed * (float) Math.Max(1, cfg.CityHuvSpeedFactor));
                    s.SirenBots.Add(leader.Cid);
                }
            }
            Log.WriteLine("Rua: cidade {0}: {1} saiu em grupo ({2}) com {3}.", ch.City, leader.Name, act, string.Join(", ", mates.Select(x => x.Name)));
        }

        static byte[] TripMove(Rice.Game.Character ch, PlayerState s, Bots.BotProfile b, Trip tr, uint nowTime, Sample last)
        {
            var cfg = RiceServer.Config;
            const float dt = (float) TickSeconds;
            var now = DateTime.UtcNow;
            if (tr.Phase == 1)
            {
                if (now >= tr.Until)
                {
                    tr.Phase = 2;
                    tr.Until = now.AddMinutes(cfg.CityBattleMinMinutes + Rng.NextDouble() * Math.Max(0, cfg.CityBattleMaxMinutes - cfg.CityBattleMinMinutes));
                    Log.WriteLine("Rua: {0} entrou na Battle Zone com o grupo.", b.Name);
                    return null;
                }
            }
            else if (tr.Phase == 2)
            {
                if (now < tr.Until) return null;
                // saiu da Battle Zone: aparece no mesmo posto e volta para o lugar de origem
                tr.Phase = 3; tr.X = tr.Post[0]; tr.Y = tr.Post[1]; tr.Z = tr.Post.Length > 2 ? tr.Post[2] : tr.Z; tr.Speed = 0;
                tr.Dir = tr.Post.Length > 3 ? Unheading(tr.Post[3]) + (float) Math.PI : tr.Dir;
                try { tr.Route = Rice.Game.CityTraffic.Route(ch.City, tr.X, tr.Y, tr.HomeX, tr.HomeY); } catch (Exception) { tr.Route = null; }
                tr.Idx = 0;
                if (tr.Route == null || tr.Route.Count == 0) { FinishTrip(ch, s, b, tr); return null; }
            }
            if (tr.Phase == 0 || tr.Phase == 3)
            {
                while (tr.Idx < tr.Route.Count - 1 && (tr.Route[tr.Idx][0] - tr.X) * (tr.Route[tr.Idx][0] - tr.X) + (tr.Route[tr.Idx][1] - tr.Y) * (tr.Route[tr.Idx][1] - tr.Y) < 36f) tr.Idx++;
                var wp = tr.Route[tr.Idx];
                float dx = wp[0] - tr.X, dy = wp[1] - tr.Y, dist = (float) Math.Sqrt(dx * dx + dy * dy);
                if (tr.Idx >= tr.Route.Count - 1 && dist < 6f)
                {
                    if (tr.Phase == 0) { tr.Phase = 1; tr.Speed = 0; tr.Until = now.AddSeconds(Math.Max(0, cfg.CityPostWaitSeconds)); }
                    else { FinishTrip(ch, s, b, tr); return null; }
                }
                else
                {
                    var cst = BotSocial.CarStats(b);
                    float vmax = Math.Min(30f, 16f + cst[0] * 0.08f);
                    float turn = Math.Abs(Wrap((float) Math.Atan2(dy, dx) - tr.Dir));
                    float desired = turn > 1.2f ? 8f : turn > 0.6f ? 14f : vmax;
                    if (tr.Idx >= tr.Route.Count - 1) desired = Math.Min(desired, 4f + dist * 0.6f);
                    float acc = desired > tr.Speed ? 6f : 14f;
                    tr.Speed = Math.Abs(desired - tr.Speed) <= acc * dt ? desired : tr.Speed + Math.Sign(desired - tr.Speed) * acc * dt;
                    var pu = new Pursuer { X = tr.X, Y = tr.Y, Dir = tr.Dir, Speed = tr.Speed };
                    Steer(pu, wp[0], wp[1], dt);
                    tr.Dir = pu.Dir;
                    tr.X += (float) Math.Cos(tr.Dir) * tr.Speed * dt;
                    tr.Y += (float) Math.Sin(tr.Dir) * tr.Speed * dt;
                    if (wp.Length > 2) tr.Z += (wp[2] - tr.Z) * 0.3f;
                }
            }
            var m = BotMove(last.Move, b, nowTime, tr.Phase == 1);
            WithIdle(s, m);
            SetF(m, 0x18, tr.X); SetF(m, 0x1c, tr.Y); SetF(m, 0x20, tr.Z); SetF(m, 0x24, Heading(tr.Dir));
            float v = tr.Phase == 1 ? 0f : tr.Speed;
            SetF(m, 0x28, (float) Math.Cos(tr.Dir) * v); SetF(m, 0x2c, (float) Math.Sin(tr.Dir) * v);
            SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
            tr.Last = m;
            return m;
        }

        static void FinishTrip(Rice.Game.Character ch, PlayerState s, Bots.BotProfile b, Trip tr)
        {
            var cfg = RiceServer.Config;
            EndGroup(s, b.Cid);
            s.Trips.Remove(b.Cid);
            if (tr.HomeParked != null) { s.Parked[b.Cid] = tr.HomeParked; return; }
            var cur = Rice.Game.CityTraffic.StartCursor(ch.City, Rng, tr.HomeX, tr.HomeY, 0f, 60f) ?? Rice.Game.CityTraffic.StartCursor(ch.City, Rng, tr.X, tr.Y, 0f, 300f);
            if (cur != null) s.Drivers[b.Cid] = new Driver { Cur = cur, Speed = (float) (cfg.CityDriveSpeedMin + Rng.NextDouble() * (cfg.CityDriveSpeedMax - cfg.CityDriveSpeedMin)) };
            Log.WriteLine("Rua: {0} voltou da Battle Zone para o lugar dele.", b.Name);
        }

        // distancia de voce ate um bot que esta aparecendo no seu jogo (float.MaxValue = nao esta perto / nao aparece)
        public static float DistanceTo(Rice.Game.Character ch, long cid)
        {
            if (ch == null) return float.MaxValue;
            lock (Sync)
            {
                PlayerState s;
                if (!states.TryGetValue(ch.CID, out s) || s.Trail.Count == 0) return float.MaxValue;
                byte[] m = null; Driver dr; DomeDriver dd;
                if (s.DomeDrivers.TryGetValue(cid, out dd)) m = dd.InsideUntil == DateTime.MinValue ? dd.Last : null;
                else if (s.Drivers.TryGetValue(cid, out dr)) m = dr.Last;
                else if (!s.Parked.TryGetValue(cid, out m)) s.FollowLast.TryGetValue(cid, out m);
                if (m == null) return float.MaxValue;
                var me = s.Trail[s.Trail.Count - 1];
                float x = F(m, 0x18) - me.X, y = F(m, 0x1c) - me.Y;
                return (float) Math.Sqrt(x * x + y * y);
            }
        }

        // bot que aceitou a party de outro bot anda atras dele (fila); na troca, para de frente para o outro
        static byte[] LinkMove(PlayerState s, Bots.BotProfile b, Bots.Link l, uint nowTime)
        {
            byte[] lm = null; Driver ld; DomeDriver dd; bool parked = false;
            if (l.Leader == 0) { lm = s.Trail.Count > 0 ? s.Trail[s.Trail.Count - 1].Move : null; parked = true; }   // voce (troca)
            else if (s.Drivers.TryGetValue(l.Leader, out ld) && ld.Last != null) lm = ld.Last;
            else if (s.DomeDrivers.TryGetValue(l.Leader, out dd) && dd.Last != null) lm = dd.Last;
            else if (s.Parked.TryGetValue(l.Leader, out lm)) parked = true;
            else
            {
                Trip tr;
                if (s.Trips.TryGetValue(l.Leader, out tr))
                {
                    if (tr.Phase == 2) return null;                 // o grupo entrou na Battle Zone: some junto
                    lm = tr.Last; parked = tr.Phase == 1;
                }
            }
            if (lm == null) return null;
            float dir = Unheading(F(lm, 0x24));
            float fx = (float) Math.Cos(dir), fy = (float) Math.Sin(dir);
            float x = F(lm, 0x18), y = F(lm, 0x1c);
            var m = BotMove(lm, b, nowTime, l.Meet || parked);
            WithIdle(s, m);
            if (l.Meet)
            {
                // vem dirigindo do lugar dele ate ficar MeetDistance m na sua frente, virado para voce, e para
                float gap = Math.Max(2f, RiceServer.Config.MeetDistance);
                float tx = x + fx * gap, ty = y + fy * gap;
                float[] p;
                if (!s.MeetPos.TryGetValue(b.Cid, out p))
                {
                    byte[] from; Driver od2;
                    if (!s.FollowLast.TryGetValue(b.Cid, out from) && s.LinkDriver.TryGetValue(b.Cid, out od2)) from = od2.Last;
                    if (!s.FollowLast.ContainsKey(b.Cid) && from == null) s.LinkParked.TryGetValue(b.Cid, out from);
                    p = from != null ? new[] { F(from, 0x18), F(from, 0x1c), F(from, 0x20) } : new[] { tx - fx * 120f, ty - fy * 120f, F(lm, 0x20) };
                    float sx = p[0] - tx, sy = p[1] - ty, sd = (float) Math.Sqrt(sx * sx + sy * sy);
                    if (sd > 200f) { p[0] = tx + sx / sd * 200f; p[1] = ty + sy / sd * 200f; }   // muito longe: aparece a 200 m e vem
                    s.MeetPos[b.Cid] = p;
                }
                float ddx = tx - p[0], ddy = ty - p[1], dist = (float) Math.Sqrt(ddx * ddx + ddy * ddy);
                float spd = Math.Min(18f, 4f + dist * 0.5f), step = spd * (float) TickSeconds;
                if (dist > step + 0.3f)
                {
                    p[0] += ddx / dist * step; p[1] += ddy / dist * step; p[2] += (F(lm, 0x20) - p[2]) * 0.2f;
                    float hd = (float) Math.Atan2(ddy, ddx);
                    SetF(m, 0x18, p[0]); SetF(m, 0x1c, p[1]); SetF(m, 0x20, p[2]); SetF(m, 0x24, Heading(hd));
                    SetF(m, 0x28, (float) Math.Cos(hd) * spd); SetF(m, 0x2c, (float) Math.Sin(hd) * spd); SetF(m, 0x30, 0f); SetF(m, 0x34, 0f);
                }
                else
                {
                    p[0] = tx; p[1] = ty;
                    SetF(m, 0x18, tx); SetF(m, 0x1c, ty);
                    SetF(m, 0x24, Heading(dir + (float) Math.PI));
                    Array.Clear(m, 0x28, 0x10);
                }
            }
            else
            {
                float back = 7f * (l.Slot + 1), side = (l.Slot % 2 == 0 ? 1f : -1f) * 1.2f;
                SetF(m, 0x18, x - fx * back - fy * side); SetF(m, 0x1c, y - fy * back + fx * side);
            }
            if (s.SirenBots.Contains(l.Leader)) Siren(s, b, m, true);
            s.FollowLast[b.Cid] = m;
            return m;
        }

        static bool SomethingAhead(PlayerState s, Driver me, float x, float y, float dir, Sample player)
        {
            float fx = (float) Math.Cos(dir), fy = (float) Math.Sin(dir);
            Func<float, float, bool> ahead = (ox, oy) =>
            {
                float ax = ox - x, ay = oy - y;
                float a = ax * fx + ay * fy;
                return a > 0 && a < 14f && Math.Abs(-ax * fy + ay * fx) < 2.5f;
            };
            if (ahead(player.X, player.Y)) return true;
            foreach (var o in s.Drivers.Values)
                if (o != me && o.Last != null && ahead(F(o.Last, 0x18), F(o.Last, 0x1c))) return true;
            return false;
        }

        // Movimento do bot: copia um movimento e troca serial, carro, cor e tempo.
        static byte[] BotMove(byte[] template, Bots.BotProfile b, uint time, bool parked)
        {
            var m = (byte[]) template.Clone();
            BitConverter.GetBytes((ushort) BotSocial.SerialOf(b)).CopyTo(m, 0);
            // idade do carro: o 804 (stickers de outro carro) so acha carro com idade 0 (DriftCity.exe 0x53ac3b)
            BitConverter.GetBytes((ushort) RiceServer.Config.BotCarAge).CopyTo(m, 2);
            BitConverter.GetBytes((ushort) 0).CopyTo(m, 4);     // 0 = carro de jogador (num. de objeto/HUV fica aqui)
            BitConverter.GetBytes((ushort) b.Car).CopyTo(m, 6);
            BitConverter.GetBytes(b.Color).CopyTo(m, 8);
            // vidro (XiCarAttr): o do bot; sem vidro proprio, o de fabrica (antes ficava o do movimento copiado, ou seja,
            // o vidro do SEU carro aparecia em todos os bots)
            BitConverter.GetBytes(b.Look != null && b.Look.Tint != 0 ? b.Look.Tint : VisualShop.DefaultTint).CopyTo(m, 0x0c);
            BitConverter.GetBytes(time).CopyTo(m, 0x14);
            if (parked) Array.Clear(m, 0x28, 0x10);
            // sem o volante/fumaca/freio do seu carro (BotsCopyMyState). Andando: o estado de "dirigindo reto" (rodas girando,
            // sem drift; BotsDriveAnim) para nao andar com as rodas paradas
            if (!RiceServer.Config.BotsCopyMyState && m.Length >= FullMove)
            {
                var cr = cruise;
                if (!parked && cr != null && RiceServer.Config.BotsDriveAnim) Array.Copy(cr, 0, m, MoveSize, StateHp);
                else Array.Clear(m, MoveSize, StateHp);
            }
            return m;
        }

        // segundos que voce esta parado (0 = andando); ocupado = perseguicao de HUV, Rush ou Undercity
        // Rush: o boss usou o poder (o cliente manda 549 JumpVehicle quando o seu carro e jogado para cima)
        public static void BossPower(Rice.Game.Character ch)
        {
            if (ch == null || !Rush.Running(ch)) return;
            lock (Sync)
            {
                PlayerState s;
                if (states.TryGetValue(ch.CID, out s)) s.BossPowerAt = DateTime.UtcNow;
            }
            Log.WriteLine("Rush: poder do boss, os bots pulam.");
        }

        // voce esta parado agora (ultimo movimento quase sem velocidade)?
        static bool StillNow(PlayerState s)
        {
            if (s.Trail.Count == 0) return false;
            var mv = s.Trail[s.Trail.Count - 1].Move;
            float vx = F(mv, 0x28), vy = F(mv, 0x2c);
            return vx * vx + vy * vy < 2f;
        }

        public static double StillSeconds(Rice.Game.Character ch)
        {
            lock (Sync)
            {
                PlayerState s;
                if (ch == null || !states.TryGetValue(ch.CID, out s) || s.StillSince == DateTime.MinValue) return 0;
                return (DateTime.UtcNow - s.StillSince).TotalSeconds;
            }
        }

        // o bot trocou de carro: some e aparece de novo no proximo instante (o jogo carrega o carro novo)
        static readonly Dictionary<uint, DateTime> respawn = new Dictionary<uint, DateTime>();
        public static void Respawn(Bots.BotProfile b)
        {
            if (b == null) return;
            lock (Sync) respawn[BotSocial.SerialOf(b)] = DateTime.UtcNow;
        }

        public static bool Busy(Rice.Game.Character ch)
        {
            if (ch == null) return false;
            if (Rush.Running(ch)) return true;
            lock (Sync)
            {
                PlayerState s;
                return states.TryGetValue(ch.CID, out s) && (s.ChaseStart != DateTime.MinValue || s.InUnderCity || s.Runners.Count > 0);
            }
        }

        public static byte[] CarMoveOf(Rice.Game.Character ch, uint serial)
        {
            lock (Sync)
            {
                var s = StateOf(ch);
                if (s.Trail.Count == 0) return null;
                var b = BotSocial.BySerial(serial);
                if (b == null) return null;
                byte[] tpl;
                Driver d;
                if (!s.Parked.TryGetValue(b.Cid, out tpl))
                {
                    if (s.Drivers.TryGetValue(b.Cid, out d) && d.Last != null) tpl = d.Last;
                    else if (!s.FollowLast.TryGetValue(b.Cid, out tpl)) tpl = BotMove(s.Trail[s.Trail.Count - 1].Move, b, 0, true);
                }
                var r = new byte[MoveSize];
                Array.Copy(tpl, r, MoveSize);
                BitConverter.GetBytes(BitConverter.ToUInt32(s.Trail[s.Trail.Count - 1].Move, 0x14)).CopyTo(r, 0x14);
                return r;
            }
        }

        // XiPlayerInfo (0xd8): nome @0, serial @0x1a, idade @0x1c, cid @0x1e, nivel @0x26, exp @0x28, crew @0x2c,
        // emblema @0x34, nome da crew @0x3c, nivel da crew @0x58 (cor da placa: 0 verde, 1 azul, 2 rosa, 3 laranja),
        // itens visuais @0x5a (0x36), avatar @0x96.
        public static byte[] PlayerInfo(Bots.BotProfile b)
        {
            var o = new byte[0xd8];
            var t = Crew.Of(b.Cid);
            var n = Encoding.Unicode.GetBytes(b.Name ?? "");
            Array.Copy(n, o, Math.Min(n.Length, 24));
            BitConverter.GetBytes((ushort) BotSocial.SerialOf(b)).CopyTo(o, 0x1a);
            BitConverter.GetBytes((ushort) RiceServer.Config.BotCarAge).CopyTo(o, 0x1c);
            BitConverter.GetBytes(b.Cid).CopyTo(o, 0x1e);
            BitConverter.GetBytes((ushort) b.Level).CopyTo(o, 0x26);
            BitConverter.GetBytes(t != null ? t.Tid : -1L).CopyTo(o, 0x2c);
            BitConverter.GetBytes(t != null ? t.Mark : -1L).CopyTo(o, 0x34);
            if (t != null)
            {
                var tn = Encoding.Unicode.GetBytes(t.Name);
                Array.Copy(tn, 0, o, 0x3c, Math.Min(tn.Length, 26));
                BitConverter.GetBytes((ushort) Crew.LevelOf(t)).CopyTo(o, 0x58);
            }
            if (b.Look != null || (RiceServer.Config.BotVisuals && !b.Special && !b.IsAlt))
                Visuals(b).CopyTo(o, 0x5a);
            BitConverter.GetBytes((ushort) b.Avatar).CopyTo(o, 0x96);
            return o;
        }

        // XiVisualItem (0x36): um i16 por categoria com o numero do item; o cliente ignora o que nao servir no carro.
        // neon @0x00 (cor @0x0a), adesivo @0x04 (cor @0x06), rodas @0x16, aerofolio @0x18. Fixo por bot.
        static byte[] Visuals(Bots.BotProfile b)
        {
            var v = new byte[0x36];
            if (b.Look != null)
            {
                // visual fixo (Joaopvbr, Marsteh, Brunizin.X ou !!visualbot)
                foreach (var kv in b.Look.Slots)
                {
                    int at;
                    if (!int.TryParse(kv.Key, System.Globalization.NumberStyles.HexNumber, null, out at) || at < 0 || at > 0x1e) continue;
                    BitConverter.GetBytes((short) kv.Value).CopyTo(v, at);
                }
                if (b.Look.DecalColor != 0) BitConverter.GetBytes(b.Look.DecalColor).CopyTo(v, 0x06);
                if (b.Look.NeonColor != 0) BitConverter.GetBytes(b.Look.NeonColor).CopyTo(v, 0x0a);
                if (!string.IsNullOrEmpty(b.Look.Plate))
                {
                    var pt = Encoding.Unicode.GetBytes(b.Look.Plate.Length > 8 ? b.Look.Plate.Substring(0, 8) : b.Look.Plate);
                    Array.Copy(pt, 0, v, 0x26, Math.Min(pt.Length, 16));
                }
                return v;
            }
            if (b.CustomLook)
            {
                // visual feito pelo jogador (!!visualbot): so as pecas dele. Ordem do XiVisualItem: neon @0, placa @2,
                // adesivo @4 (cor @6), cor do neon @0xa, para-choque @0xe, entrada de ar @0x10, aero @0x12, chama @0x14,
                // rodas @0x16, aerofolio @0x18
                if (b.Visuals != null)
                    foreach (var kv in b.Visuals)
                    {
                        int cat = VisualShop.CatOfUid(kv.Value), n = VisualShop.NumberOf(kv.Value);
                        if (n <= 0) continue;
                        int at = cat == 2 ? 0x00 : cat == 9 ? 0x02 : cat == 11 ? 0x04 : cat == 4 ? 0x0e : cat == 52 ? 0x10
                               : cat == 6 ? 0x12 : cat == 10 ? 0x14 : (cat == 8 || cat == 48) ? 0x16 : cat == 7 ? 0x18 : -1;
                        if (at < 0) continue;
                        BitConverter.GetBytes((short) n).CopyTo(v, at);
                        if (cat == 11) BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(v, 0x06);
                    }
                return v;
            }
            var r = new Random((int) (b.Cid & 0x7fffffff));
            // neon: todo bot acima do nivel BotNeonLevel (10) tem; abaixo, nao
            short neonN = (short) r.Next(1, 9); uint neonC = (uint) r.Next(0x404040, 0xFFFFFF);
            if (b.Level > RiceServer.Config.BotNeonLevel) { BitConverter.GetBytes(neonN).CopyTo(v, 0x00); BitConverter.GetBytes(neonC).CopyTo(v, 0x0a); }
            if (r.NextDouble() < 0.5) { BitConverter.GetBytes((short) r.Next(1, 21)).CopyTo(v, 0x04); BitConverter.GetBytes(0xFF000000u | (uint) r.Next(0x202020, 0xFFFFFF)).CopyTo(v, 0x06); }
            BitConverter.GetBytes((short) r.Next(1, 19)).CopyTo(v, 0x16);
            // aerofolio: os de qualquer carro (TB, GT, Mitron... SPORTS = 24-37) ou o do proprio modelo (o resto nao aparece)
            if (r.NextDouble() < RiceServer.Config.BotSpoilerChance)
            {
                var own = VisualShop.SpoilersFor(b.Car);
                short sp = own.Count > 0 && r.NextDouble() < 0.4 ? (short) own[r.Next(own.Count)] : (short) r.Next(24, 38);
                BitConverter.GetBytes(sp).CopyTo(v, 0x18);
            }
            // pecas visuais que ganhou na troca (neon, adesivo, rodas)
            if (b.Visuals != null)
                foreach (var kv in b.Visuals)
                {
                    int cat = VisualShop.CatOfUid(kv.Value), n = VisualShop.NumberOf(kv.Value);
                    if (n <= 0) continue;
                    if (cat == 2) BitConverter.GetBytes((short) n).CopyTo(v, 0x00);
                    else if (cat == 11) BitConverter.GetBytes((short) n).CopyTo(v, 0x04);
                    else if (cat == 8 || cat == 48) BitConverter.GetBytes((short) n).CopyTo(v, 0x16);
                }
            return v;
        }
    }
}
