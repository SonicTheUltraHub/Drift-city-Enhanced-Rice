using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Jogadores simulados (bots) do modo offline.
    //   bots.json  : perfis (nome, nivel, carro, crew, personalidade, contadores do leilao)
    //   crews.json : as crews dos bots (IsBot = true) ficam junto com as crews do jogador
    // Gerados automaticamente na primeira vez; apague bots.json para gerar de novo.
    public static class Bots
    {
        public const long CidBase = 900000000;
        static readonly string FilePath = Paths.World("bots.json");
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();

        public class BotProfile
        {
            public long Cid;
            public string Name = "";
            public int Level;
            public int Car;
            public uint Color = 0x01010102;
            public string Personality = "amigavel";   // amigavel, competitivo, comerciante, calado
            public long CrewTid;
            public string CrewName = "";
            public int Joined;
            public uint Reg, Bought, Sold;            // contadores do leilao (aparecem junto do vendedor)
            public bool Special;                      // bot fixo (pedido do dono do projeto)
            public int Avatar = 1;                    // personagem (rosto) do bot
            public int AiIdx;                         // linha da tabela de IA do cliente (AICarParam) com o nome dele
            public bool MostWanted;                   // crew Most Wanted (aparece nas 4 cidades)
            public bool NeoBot;
            public Dictionary<string, int> NeoKits = new Dictionary<string, int>();   // kits de recuperacao que ganhou na troca                       // mora na Neo City (so aparece la; da para chamar para a party nas missoes)
            public int HomeCity;                      // cidade onde mora (1 Moon Palace, 2 Koinonia, 3 Cras, 4 Oros); 0 = ainda sem
            public bool Beginner;                     // corredor iniciante (vira amigo de corredor novo)
            public long FriendOf;                     // CID do corredor de quem ele e amigo inicial (evoluem juntos)
            public Dictionary<string, string> Parts = new Dictionary<string, string>(); // pecas que ganhou na troca (categoria -> item)
            public int CarGrade;                      // grau do carro que ganhou na troca (0 = o da tabela)
            public Dictionary<string, int> Visuals = new Dictionary<string, int>(); // pecas visuais que ganhou (lugar -> item do drift shop)
            [JsonIgnore] public bool IsAlt;           // outro corredor da sua conta (vira bot quando nao esta em uso)
            [JsonIgnore] public long AltOwner;        // CID do corredor que esta jogando
            [JsonIgnore] public bool InTutorial;      // outro corredor seu que ainda esta com o carro de treino do tutorial
            public List<int> CarsForSale = new List<int>(); // carros que ele vende quando os bots forem para a rua
            public double Skill = 1.0;                // habilidade (HUV, Rush e corrida): 1 = normal
            public bool CustomLook;                   // visual feito pelo jogador (!!visualbot): carro, cor e pecas visuais fixos
            public BotLook Look;                      // visual fixo (bots fixos ou !!visualbot)
            // economia do bot: mito e exp proprios; sem mito/nivel/peca para o que quer, corre atras (meta)
            public long Mito = -1;
            public Licenses.BotLic Lic;
            public string Focus;
            public List<BotCar> Garage;               // carros que o bot tem (o atual esta aqui tambem)                      // "corrida" (velocidade/aceleracao) ou "bater" (durabilidade: HUV/Rush)               // licencas do bot (comeca no Rookie e evolui com o tempo)
            public int Exp;
            public string GoalKind = "";              // "" nenhuma, mito, nivel, peca, up, carro
            public string GoalName = "", GoalItem = "";
            public long GoalMito;
            public int GoalLevel, GoalMinutes;
            public uint GoalDeal;
            [JsonIgnore] public DateTime GoalSaid = DateTime.MinValue;
            [JsonIgnore] public long GoalHelper;      // CID do jogador que entrou na party para ajudar na meta
            [JsonIgnore] public long GoalBotHelper;   // bot que esta ajudando (andam juntos)
            [JsonIgnore] public bool AskedTrade;      // ja pediu a peca para o jogador da party
        }

        // Bots fixos: sempre existem e serao os primeiros a aparecer na rua, vendendo carros V9.
        static readonly BotProfile[] SpecialBots =
        {
            new BotProfile { Name = "Marsteh", Level = 90, Car = 422, Personality = "comerciante", Special = true,
                             CarsForSale = new List<int> { 422 } },     // PLUCK ST (V9)
            new BotProfile { Name = "Brunizin.X", Level = 90, Car = 280, Personality = "comerciante", Special = true,
                             CarsForSale = new List<int> { 280 } },     // Mento MINI (V9)
        };

        // Bots fixos que andam pela cidade (nao ficam parados vendendo): sempre online, carro e pecas fixos.
        // Joaopvbr: Dragoon V9 com as pecas no maximo, bom em HUV e corrida (linha 218 da tabela de IA do cliente).
        static readonly BotProfile[] AceBots =
        {
            new BotProfile { Name = "Joaopvbr", Level = 90, Car = 367, CarGrade = 9, Personality = "competitivo", AiIdx = 218,
                             Avatar = 1, Color = 0x16000002, Skill = 1.6 },
            // amPerl (autor do emulador Rice): nivel 100, Shadow Xmas V9, na LegendsDC com o RiceAdmin (lider)
            new BotProfile { Name = "amPerl", Level = 100, Car = 276, CarGrade = 9, Personality = "amigavel",
                             Avatar = 2, Color = 0x01000002, Skill = 1.7 },
            // Xerender13 (autor do openDC): nivel 88, N1 (McLaren P1) V9, sem crew
            new BotProfile { Name = "Xerender13", Level = 88, Car = 328, CarGrade = 9, Personality = "competitivo",
                             Avatar = 3, Color = 0x16000002, Skill = 1.5 },
        };

        // crew de cada bot fixo ("" = sem crew); quem entrou numa crew de jogador fica nela
        static string FixedCrew(string name) { return string.Equals(name, "amPerl", StringComparison.OrdinalIgnoreCase) ? EliteCrew : ""; }

        // pecas de corrida do nivel (as melhores que o nivel usa): 2 de cada categoria; suffix "_b" = Sorched, se existir
        static Dictionary<string, string> LevelParts(int level, string suffix)
        {
            var d = new Dictionary<string, string>();
            string[] pre = { "i_s_", "i_a_", "i_c_", "i_b_" };
            string[] cats = { "speed", "accel", "crash", "boost" };
            var items = Rice.Server.Structures.Resources.ItemTable.Items.OfType<Rice.Server.Structures.Resources.ItemTableEntry>().ToList();
            for (int i = 0; i < 4; i++)
            {
                Func<string, Rice.Server.Structures.Resources.ItemTableEntry> best = suf => items
                    .Where(x => x.ID != null && System.Text.RegularExpressions.Regex.IsMatch(x.ID, "^" + pre[i] + "\\d{6}" + suf + "$"))
                    .Where(x => { int ml; return int.TryParse(x.MinLevel, out ml) && ml <= level; })
                    .OrderByDescending(x => { int ml; int.TryParse(x.MinLevel, out ml); return ml; }).ThenByDescending(x => x.BasePoints)
                    .FirstOrDefault();
                var e = (suffix != "" ? best(suffix) : null) ?? best("");
                string id = e != null ? e.ID : "i_" + "sacb"[i] + "_000020";
                d[cats[i]] = id; d[cats[i] + "#2"] = id;
            }
            return d;
        }

        // Visual do carro de um bot (XiVisualItem): lugar (deslocamento em hex) -> numero do item; cores e placa.
        public class BotLook
        {
            public Dictionary<string, int> Slots = new Dictionary<string, int>();
            public uint NeonColor, DecalColor, Paint, Tint;
            public string Plate = "";
        }

        // o jogo guarda as cores ao contrario (azul, verde, vermelho): aqui as cores sao escritas em RGB e viram BGR
        public static uint Bgr(uint c) { return (c & 0xff00ff00u) | ((c & 0xffu) << 16) | ((c >> 16) & 0xffu); }

        static BotLook MakeLook(uint paint, uint tint, uint neon, uint decal, string plate, params int[] slots)
        {
            var l = new BotLook { Paint = paint == 0 ? 0 : Bgr(paint), Tint = tint == 0 ? 0 : Bgr(tint), NeonColor = neon == 0 ? 0 : Bgr(neon),
                                  DecalColor = decal == 0 ? 0 : Bgr(decal), Plate = plate ?? "" };
            for (int i = 0; i + 1 < slots.Length; i += 2) l.Slots[slots[i].ToString("x")] = slots[i + 1];
            return l;
        }

        // Visuais pedidos pelo dono do projeto (numeros da tabela visual do cliente; rodas/aerofolio pelo TireList/SpoilerList)
        static Dictionary<string, BotLook> PresetLooks()
        {
            return new Dictionary<string, BotLook>(StringComparer.OrdinalIgnoreCase)
            {
                // Dragoon: pintura (30,30,30), vidro S15, neon S15 e Dragoon Decal (251,216,0), placa amarela "GOLD"
                { "Joaopvbr", MakeLook(0x831E1E1Eu, 0x82FBD800u, 0xFBD800u, 0xFFFBD800u, "GOLD", 0x00, 13, 0x02, 7, 0x04, 183) },
                // Mento MINI: aerofolio GT wing (25), aero do proprio carro (Mento MINI Type C, 455), chama vermelha (3),
                // rodas HYKAN (78); vidro (255,1,1), carro preto, neon vermelho
                { "Brunizin.X", MakeLook(0x83050505u, 0x82FF0101u, 0xFF0000u, 0, "", 0x18, 25, 0x12, 455, 0x14, 3, 0x16, 78, 0x00, 13) },
                // PLUCK ST: rodas Dragoon (74), vidro (1,1,1), neon branco, aerofolio PLUCK (53)
                { "Marsteh", MakeLook(0, 0x82010101u, 0xFFFFFFu, 0, "", 0x16, 74, 0x18, 53, 0x00, 13) },
            };
        }

        // Crew fixa: RiceAdmin (lider, o perfil upado) e o bot amPerl. Antes: LegendsDC com Joaopvbr, Marsteh e Brunizin.X.
        public const string EliteCrew = "LegendsDC";
        const string OldEliteCrew = "LendasDC";
        public const string MostWantedCrew = "MostWanted";

        // Most Wanted (Blacklist do Need for Speed Most Wanted): #15 Sonny ate #1 Razor. Carro do jogo mais parecido com o
        // da Blacklist, sempre V9; cidade do mais fraco (Moon Palace) para o mais forte (Oros).
        //   rank, nome, carro, nivel, cidade
        static readonly object[][] MostWantedDefs =
        {
            new object[] { 15, "Sonny", 80, 85, 1 },    // VW Golf GTI -> Birdie RTI
            new object[] { 14, "Taz", 35, 86, 1 },      // Lexus IS300 -> Dreamer (IS 250)
            new object[] { 13, "Vic", 30, 87, 1 },      // Toyota Supra -> Above
            new object[] { 12, "Izzy", 13, 88, 1 },     // Mazda RX-8 -> Nova
            new object[] { 11, "Big Lou", 39, 89, 2 },  // Lancer Evolution VIII -> Fondo
            new object[] { 10, "Baron", 91, 90, 2 },    // Porsche Cayman S -> Lightning
            new object[] { 9, "Earl", 11, 91, 2 },      // Subaru Impreza WRX STi -> Impact
            new object[] { 8, "Jewels", 435, 92, 2 },   // Ford Mustang -> BULLITT
            new object[] { 7, "Kaze", 25, 93, 3 },      // Mercedes CLK 500 -> Arena (CLK GTR)
            new object[] { 6, "Ming", 404, 94, 3 },     // Lamborghini Gallardo -> Luna
            new object[] { 5, "Webster", 19, 95, 3 },   // Chevrolet Corvette C6 -> Indigo
            new object[] { 4, "JV", 18, 96, 3 },        // Dodge Viper SRT-10 -> Viper
            new object[] { 3, "Ronnie", 23, 97, 4 },    // Aston Martin DB9 -> Cross
            new object[] { 2, "Bull", 16, 98, 4 },      // Mercedes SL65 AMG -> Aria (SL55)
            new object[] { 1, "Razor", 41, 99, 4 },     // Ford Mustang GT (Shelby GT500) -> Snake
        };

        // ---- bots da Neo City ---------------------------------------------------------------------------------
        // NeoBotsCount (20) bots que moram so na Neo City (HomeCity 6): ficam parados pela base/lobby da Neo City, nao
        // aparecem nas outras cidades e podem entrar na sua party para ajudar nas missoes (atiram nos HUVs perto deles).
        static readonly string[] NeoBotNames =
        {
            "NeoRaven", "CyberKai", "Plasmatica", "RailRita", "OrosGhost", "HuvSlayer", "NeoBlitz", "Voltix", "TurretKill", "Quantum",
            "NeoNinja", "ChronoX", "Synthwave", "NeoLuna", "Overclock", "Neutrino", "ByteRider", "Ciborgue", "HexaCore", "NeoFalcao"
        };
        static readonly string[] NeoPersonalities = { "competitivo", "amigavel", "calado", "amigavel", "competitivo" };
        static bool EnsureNeoBots()
        {
            if (!RiceServer.Config.NeoBots) return false;
            bool changed = false;
            int n = Math.Max(0, Math.Min(NeoBotNames.Length, RiceServer.Config.NeoBotsCount));
            for (int i = 0; i < n; i++)
            {
                string name = NeoBotNames[i];
                long cid = CidBase + 9200 + i;
                var b = bots.FirstOrDefault(x => x.Cid == cid);
                if (b == null)
                {
                    if (bots.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) continue;   // nome ja usado
                    b = new BotProfile { Cid = cid, Name = name, Personality = NeoPersonalities[i % NeoPersonalities.Length],
                                         Level = 70 + (i * 29) / Math.Max(1, n - 1), Car = 1,
                                         Joined = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400 * (100 + i * 11),
                                         Skill = 1.0 + (i % 5) * 0.1 };
                    bots.Add(b);
                    changed = true;
                }
                if (!b.NeoBot || b.HomeCity != 6) { b.NeoBot = true; b.HomeCity = 6; changed = true; }
                if (b.Parts == null || b.Parts.Count == 0) { b.Parts = MaxParts(""); changed = true; }
                if (NeoGear(b)) changed = true;
                // crew: 6 em cada uma das 3 crews da Neo City, os 2 ultimos sem crew (quem entrou numa crew sua fica nela)
                var cur = Crew.ById(b.CrewTid);
                if (cur == null || cur.IsBot)
                {
                    string want = i < 18 ? NeoCrewSeeds[i / 6].Name : "";
                    if (b.CrewName != want) { b.CrewName = want; if (want == "") b.CrewTid = 0; changed = true; }
                }
            }
            return changed;
        }

        // equipamento da Neo City do nivel do bot (2 armas de tipos diferentes, armadura, Amplifier e Fuse): o melhor que
        // o nivel dele usa. Guardado nas pecas dele (neo_weapon, neo_weapon#2, neo_armor, neo_coil, neo_fuse); a troca troca.
        public static readonly string[] NeoSlots = { "neo_weapon", "neo_weapon#2", "neo_armor", "neo_coil", "neo_fuse" };
        static bool NeoGear(BotProfile b)
        {
            if (b.Parts == null) b.Parts = new Dictionary<string, string>();
            var items = Rice.Server.Structures.Resources.ItemTable.Items.OfType<Rice.Server.Structures.Resources.ItemTableEntry>()
                .Where(x => x.ID != null && x.Category != null && x.Category.StartsWith("neo_") && !x.ID.EndsWith("9999") && !x.ID.EndsWith("99998")
                            && x.Grade != "R").ToList();
            if (items.Count == 0) return false;
            Func<Rice.Server.Structures.Resources.ItemTableEntry, int> lv = x => { int m; return int.TryParse(x.MinLevel, out m) ? m : 999; };
            var r = new Random((int) (b.Cid & 0x7fffffff));
            bool changed = false;
            Func<string, string, string> pick = (cat, notFamily) =>
            {
                var ok = items.Where(x => x.Category == cat && lv(x) <= b.Level && (notFamily == null || !x.ID.StartsWith(notFamily))).ToList();
                if (ok.Count == 0) return null;
                int best = ok.Max(lv);
                var top = ok.Where(x => lv(x) == best).ToList();
                return top[r.Next(top.Count)].ID;
            };
            foreach (var slot in NeoSlots)
            {
                if (b.Parts.ContainsKey(slot)) continue;
                string cat = slot.Split('#')[0], other = null;
                if (slot == "neo_weapon#2") { string w1; if (b.Parts.TryGetValue("neo_weapon", out w1) && w1.Length > 9) other = w1.Substring(0, 9); }   // neo_wp_c_
                var id = pick(cat, other);
                if (id != null) { b.Parts[slot] = id; changed = true; }
            }
            return changed;
        }

        public static List<BotProfile> NeoResidents() { return All().Where(b => b.NeoBot && !b.IsAlt).ToList(); }

        static bool EnsureMostWanted()
        {
            bool changed = false;
            foreach (var d in MostWantedDefs)
            {
                int rank = (int) d[0], car = (int) d[2], lvl = (int) d[3], city = (int) d[4];
                string name = (string) d[1];
                var b = bots.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (b != null && !b.MostWanted && b.Cid != CidBase + 9000 + rank) continue;   // nome ja usado por outro bot
                if (b == null)
                {
                    b = new BotProfile { Cid = CidBase + 9000 + rank, Name = name, Personality = "competitivo", Color = 0x01101010,
                                         Joined = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400 * 300 };
                    bots.Add(b);
                    changed = true;
                }
                if (!b.MostWanted || b.Car != car || b.CarGrade != 9 || b.Level != lvl || b.HomeCity != city || b.CrewName != MostWantedCrew)
                {
                    b.MostWanted = true; b.Car = car; b.CarGrade = 9; b.Level = lvl; b.HomeCity = city; b.CrewName = MostWantedCrew;
                    b.Skill = 1.2 + (15 - rank) * 0.04;
                    if (b.Parts == null || b.Parts.Count == 0) b.Parts = MaxParts(rank == 1 ? "_b" : "");
                    changed = true;
                }
            }
            return changed;
        }

        // pecas no maximo (Ouranos/Tartaros/Kronos/Chaos U, nivel 95): 2 de cada categoria
        static Dictionary<string, string> MaxParts(string suffix)
        {
            var d = new Dictionary<string, string>();
            string[] ids = { "i_s_000020", "i_a_000020", "i_c_000020", "i_b_000020" };
            string[] cats = { "speed", "accel", "crash", "boost" };
            for (int i = 0; i < 4; i++) { d[cats[i]] = ids[i] + suffix; d[cats[i] + "#2"] = ids[i] + suffix; }
            return d;
        }

        // exp do bot: para subir do nivel N precisa de max(100, N x BotExpPerLevel)
        public static long ExpNeed(BotProfile b) { return Math.Max(100L, (long) (b.Level * Math.Max(1, RiceServer.Config.BotExpPerLevel))); }

        // soma exp e sobe de nivel quantas vezes der; devolve quantos niveis subiu
        public static int AddExp(BotProfile b, long exp)
        {
            if (b == null || exp <= 0) return 0;
            int up = 0;
            b.Exp = (int) Math.Min(int.MaxValue, (long) b.Exp + exp);
            while (b.Level < 99 && b.Exp >= ExpNeed(b)) { b.Exp -= (int) ExpNeed(b); b.Level++; up++; }
            return up;
        }

        // ---- moradores de cada cidade ------------------------------------------------------------------------
        // CityBotsPerCity (30) por cidade, do nivel mais baixo para o mais alto: Moon Palace 1-16 (V1-V2), Koinonia 15-30,
        // Cras 30-50, Oros 50-99 (CityLevelBands). Feito uma vez (bot sem HomeCity); quem esta fora da faixa da cidade
        // dele passa para dentro dela (espalhados pela faixa) e pega o carro do V do nivel novo.
        public static bool AssignCities()
        {
            var cfg = RiceServer.Config;
            var normal = bots.Where(b => !b.Special && !IsAce(b) && !b.IsAlt && !b.MostWanted && !b.NeoBot).ToList();
            if (normal.Count == 0 || normal.All(b => b.HomeCity > 0)) return false;
            var bands = cfg.CityLevelBands ?? new[] { 1, 16, 15, 30, 30, 50, 50, 99 };
            int per = Math.Max(1, cfg.CityBotsPerCity);
            var order = normal.OrderBy(b => b.Level).ThenBy(b => b.Cid).ToList();
            for (int i = 0; i < order.Count; i++) order[i].HomeCity = Math.Min(4, i / per + 1);
            int moved = 0;
            for (int city = 1; city <= 4; city++)
            {
                int lo = bands[(city - 1) * 2], hi = bands[(city - 1) * 2 + 1];
                var outside = order.Where(b => b.HomeCity == city && (b.Level < lo || b.Level > hi)).ToList();
                for (int k = 0; k < outside.Count; k++)
                {
                    var b = outside[k];
                    b.Level = lo + (int) Math.Round((hi - lo) * (outside.Count == 1 ? 0.5 : k / (double) (outside.Count - 1)));
                    b.Exp = 0; b.CarGrade = 0;
                    TierCarFor(b);
                    moved++;
                }
            }
            Log.WriteLine("Bots por cidade: {0} em Moon Palace, {1} em Koinonia, {2} em Cras, {3} em Oros ({4} mudaram de nivel para a faixa da cidade).",
                          order.Count(b => b.HomeCity == 1), order.Count(b => b.HomeCity == 2), order.Count(b => b.HomeCity == 3), order.Count(b => b.HomeCity == 4), moved);
            return true;
        }

        public static List<BotProfile> Residents(int city)
        {
            return All().Where(b => b.HomeCity == city && !b.Special && !IsAce(b) && !b.IsAlt && !b.MostWanted).ToList();
        }

        public static List<BotProfile> MostWantedIn(int city)
        {
            return All().Where(b => b.MostWanted && b.HomeCity == city).ToList();
        }

        public static bool IsAce(BotProfile b) { return b != null && AceBots.Any(a => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)); }

        static bool EnsureSpecialBots()
        {
            bool changed = false;
            foreach (var sp in SpecialBots.Concat(AceBots))
            {
                var b = bots.FirstOrDefault(x => string.Equals(x.Name, sp.Name, StringComparison.OrdinalIgnoreCase));
                if (b == null)
                {
                    b = new BotProfile { Cid = bots.Count == 0 ? CidBase + 1 : bots.Max(x => x.Cid) + 1, Name = sp.Name,
                                         Joined = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400 * 600,
                                         Reg = 120, Bought = 40, Sold = 110, Color = 0x01101010 };
                    bots.Add(b);
                }
                b.Level = sp.Level; b.Personality = sp.Personality; b.Special = sp.Special;
                if (!b.CustomLook) b.Car = sp.Car;           // visual salvo pelo jogador: fica o carro dele
                b.CarsForSale = new List<int>(sp.CarsForSale);
                bool ace = !sp.Special;
                if (ace) { if (!b.CustomLook) b.CarGrade = sp.CarGrade; b.AiIdx = sp.AiIdx; b.Avatar = sp.Avatar; b.Skill = sp.Skill; }
                // pecas de corrida no maximo (Joaopvbr com as Sorched, os outros com as normais); o equipamento da Neo City
                // e os kits que voce deu na troca ficam (antes eram apagados toda vez que o servidor abria)
                var keepNeo = b.Parts != null ? b.Parts.Where(kv => kv.Key.StartsWith("neo_")).ToList() : new List<KeyValuePair<string, string>>();
                b.Parts = LevelParts(sp.Level, ace ? "_b" : "");
                foreach (var kv in keepNeo) b.Parts[kv.Key] = kv.Value;
                // crew: amPerl na LegendsDC; os outros sem crew (Joaopvbr, Marsteh e Brunizin.X sairam). Crew de jogador fica.
                var curCrew = Crew.ById(b.CrewTid);
                if (curCrew == null || curCrew.IsBot)
                {
                    string want = FixedCrew(b.Name);
                    b.CrewName = want;
                    if (want == "") b.CrewTid = 0;
                }
                BotLook pl;
                if (!b.CustomLook && PresetLooks().TryGetValue(b.Name, out pl)) b.Look = pl;
                changed = true;
            }
            return changed;
        }

        static List<BotProfile> bots;

        static readonly Dictionary<string, string> Renames = new Dictionary<string, string>
        {
            { "CtrlAltDerrota", "CtrlAltPerde" },
            { "PãoDeQueijoOP", "PãoDeQueijo" },
            { "AvestruzQueTeSeduz", "Avestruzinho" },
            { "MonALisaSemSobrancelha", "MonaSemSobra" },
            { "CapivaraRaivosa", "CapiRaivosa" },
            { "JulioCesarDoGrau", "JulioDoGrau" },
            { "Padeiro Templário", "PadeiroTempl" },
            { "Sabonete Francis", "SaboFrancis" },
            { "Betoneira Furada", "BetoFurada" },
            { "Canivete Comgalo", "CaniveteGalo" },
            { "MestreDoCuscuz", "MestreCuscuz" },
            { "GeladeiraInversa", "GeladeiraInv" },
            { "LiquidificadorLigado", "LiquidLigado" },
            { "PinguimDeGeladeira", "PinguimFrio" },
            { "ChineloDeGanso", "ChineloGanso" },
            { "Ladeira Abaixo", "LadeiraBaixo" },
            { "ErreiFuiMoleque", "ErreiMoleque" },
            { "ProcrastinadorProfissional", "ProcrastiPro" },
            { "MestreDoRespawn", "ReiDoRespawn" },
            { "SacoDePancada", "SacoPancada" },
            { "VoltaProLobby", "VoltaPLobby" }
        };

        static readonly string[] Names =
        {
            "DriftKing", "NightOwl", "Kazuya", "TurboTina", "MitoHunter", "Ryuji", "SpeedFreak", "Luna", "Brakeless",
            "Shiro", "Kaito", "RedLine", "Nitrous", "Haru", "Akira", "Viper", "GhostCar", "Rin", "Bolt", "Sakura",
            "Takumi", "Ronin", "Blaze", "Neko", "Hayate", "Raiden", "Koji", "Mika", "Daisuke", "Yume", "Jinx",
            "Falcon", "Kenta", "Aoi", "Storm", "Rex", "Hikari", "Duke", "Taro", "Nova", "Sora", "Ace", "Kira",
            "Drako", "Mamba", "Yuki", "Pixel", "Zero", "Ichiro", "Kumi", "Orion", "Tetsu", "Riku", "Comet",
            "Hiro", "Lynx", "Nami", "Kenji", "Ash", "Juno", "Shin", "Mei", "Gaku", "Echo", "Ren", "Toru"
        };
        static readonly string[] Suffixes = { "", "", "", "_DC", "77", "99", "Jr", "_X", "07", "GT", "_R", "21" };

        // Carros por faixa de nivel (os mesmos que os pilotos IA usam).
        static readonly int[][] CarTiers =
        {
            new[] { 1, 28, 3, 56, 79, 81 },              // 1-4
            new[] { 2, 31, 4, 32, 82, 92 },              // 5-9
            new[] { 6, 36, 8, 58 },                      // 10-19
            new[] { 5, 7, 59, 61, 9, 83, 90 },           // 20-29
            new[] { 10, 30, 33, 35, 37, 96 },            // 30-39
            new[] { 11, 13, 14, 66, 17, 63, 39, 87 },    // 40-49
            new[] { 42, 41, 47, 69, 46 },                // 50-59
            new[] { 16, 18, 20, 48, 70 },                // 60-69
            new[] { 22, 25, 27, 76, 52 },                // 70-89
            new[] { 12, 55, 29, 80, 34, 84, 86, 38, 78, 15, 45, 50, 19, 24 } // 90+
        };

        public class CrewSeed
        {
            public string Name, Desc;
            public int MinLevel, LevelLo, LevelHi, Size, City;
            public long Mark;
        }

        // Crews dos bots: modo classico (padrao) ou o mod Escada de crews (CrewLadder)
        static CrewSeed[] CrewSeeds { get { return RiceServer.Config.CrewLadder ? LadderSeeds : ClassicSeeds; } }

        // crews dos bots da Neo City (fora da escada de crews): 6 bots em cada, 2 sem crew
        static readonly CrewSeed[] NeoCrewSeeds =
        {
            new CrewSeed { Name = "ROADGuard", Desc = "Neo City: os guardas da base ROAD.", MinLevel = 70, LevelLo = 70, LevelHi = 99, Size = 6, Mark = 10, City = 6 },
            new CrewSeed { Name = "TimeWarp", Desc = "Neo City: viajantes do tunel do tempo.", MinLevel = 70, LevelLo = 70, LevelHi = 99, Size = 6, Mark = 5, City = 6 },
            new CrewSeed { Name = "MitronX", Desc = "Neo City: cacadores de HUV da ilha Mitron.", MinLevel = 70, LevelLo = 70, LevelHi = 99, Size = 6, Mark = 8, City = 6 },
        };
        static IEnumerable<CrewSeed> AllSeeds() { return CrewSeeds.Concat(RiceServer.Config.NeoBots ? NeoCrewSeeds : new CrewSeed[0]); }

        // modo classico: cada crew com o seu requisito de nivel; os bots recrutam e voce entra se tiver o nivel
        static readonly CrewSeed[] ClassicSeeds =
        {
            new CrewSeed { Name = "Rookies", Desc = "Crew para iniciantes. Todos sao bem-vindos!", MinLevel = 1, LevelLo = 3, LevelHi = 20, Size = 9, Mark = 1 },
            new CrewSeed { Name = "KoiFlow", Desc = "Drift e amizade em Koinonia.", MinLevel = 10, LevelLo = 12, LevelHi = 35, Size = 8, Mark = 2 },
            new CrewSeed { Name = "NightRiders", Desc = "Corridas a noite em Moon Palace.", MinLevel = 20, LevelLo = 22, LevelHi = 50, Size = 8, Mark = 3 },
            new CrewSeed { Name = "MitoKings", Desc = "Comerciantes do leilao. Compramos e vendemos de tudo.", MinLevel = 30, LevelLo = 30, LevelHi = 65, Size = 7, Mark = 4 },
            new CrewSeed { Name = "CrasGhosts", Desc = "Cacadores de HUV de Cras.", MinLevel = 45, LevelLo = 45, LevelHi = 80, Size = 7, Mark = 5 },
            new CrewSeed { Name = "OrosLegends", Desc = "So os melhores. Nivel 70+.", MinLevel = 70, LevelLo = 72, LevelHi = 99, Size = 6, Mark = 6 },
            new CrewSeed { Name = EliteCrew, Desc = "Lendas do Drift City: RiceAdmin (lider) e amPerl. So entra quem tem nivel 90 ou mais.", MinLevel = 90, LevelLo = 90, LevelHi = 99, Size = 0, Mark = 7 },
        };

        static readonly CrewSeed[] LadderSeeds =
        {
            // 2 crews por cidade (10 membros cada; chefe = o de nivel mais alto). Ordem = escada de desafios.
            new CrewSeed { Name = "Rookies", Desc = "Moon Palace, lado esquerdo. A primeira crew da escada.", MinLevel = 1, LevelLo = 1, LevelHi = 16, Size = 10, Mark = 1, City = 1 },
            new CrewSeed { Name = "MoonFlow", Desc = "Moon Palace, lado direito.", MinLevel = 1, LevelLo = 1, LevelHi = 16, Size = 10, Mark = 2, City = 1 },
            new CrewSeed { Name = "MitoKings", Desc = "Koinonia, parte sul.", MinLevel = 15, LevelLo = 15, LevelHi = 30, Size = 10, Mark = 4, City = 2 },
            new CrewSeed { Name = "KoiGhost", Desc = "Koinonia, parte norte.", MinLevel = 15, LevelLo = 15, LevelHi = 30, Size = 10, Mark = 5, City = 2 },
            new CrewSeed { Name = "CrasRacers", Desc = "Cras, lado direito (nivel baixo de Cras).", MinLevel = 30, LevelLo = 30, LevelHi = 50, Size = 10, Mark = 3, City = 3 },
            new CrewSeed { Name = "CrasElite", Desc = "Cras, lado esquerdo (nivel alto de Cras).", MinLevel = 30, LevelLo = 30, LevelHi = 50, Size = 10, Mark = 8, City = 3 },
            new CrewSeed { Name = "OrosLegends", Desc = "Oros, parte de cima a direita.", MinLevel = 50, LevelLo = 50, LevelHi = 99, Size = 10, Mark = 6, City = 4 },
            new CrewSeed { Name = MostWantedCrew, Desc = "A Blacklist. So depois da LegendsDC. Chefe: Razor.", MinLevel = 85, LevelLo = 85, LevelHi = 99, Size = 15, Mark = 9, City = -1 },
            new CrewSeed { Name = EliteCrew, Desc = "Lendas do Drift City: RiceAdmin (lider) e amPerl. So entra quem tem nivel 90 ou mais.", MinLevel = 90, LevelLo = 90, LevelHi = 99, Size = 0, Mark = 7 },
        };

        // Cor do carro (XiCarAttr @8). Palpite pelo carro do jogador (0x01000002): o byte de cima e a cor da paleta
        // (VehicleColorGroups: 0 Silver ... 22 Black pearl, 24 Carmine, 28 Slate). BotColorByte = 0 usa o byte de baixo.
        public static uint PaintColor(int idx)
        {
            idx = Math.Max(0, Math.Min(28, idx));
            return RiceServer.Config.BotColorByte == 0 ? (uint) (0x01000000 | idx) : (uint) ((idx << 24) | 0x02);
        }

        public static int CarFor(int level)
        {
            int t = level < 5 ? 0 : level < 10 ? 1 : level < 20 ? 2 : level < 30 ? 3 : level < 40 ? 4
                  : level < 50 ? 5 : level < 60 ? 6 : level < 70 ? 7 : level < 90 ? 8 : 9;
            var tier = CarTiers[t];
            return tier[Rng.Next(tier.Length)];
        }

        public static List<BotProfile> All()
        {
            lock (Sync)
            {
                if (bots == null)
                {
                    try { bots = File.Exists(FilePath) ? JsonConvert.DeserializeObject<List<BotProfile>>(File.ReadAllText(FilePath)) : null; }
                    catch (Exception ex) { Log.WriteError("bots.json invalido: " + ex.Message); }
                    if (bots != null && bots.Count > 0 && !bots.Any(b => b.AiIdx > 0))
                    {
                        // bots.json antigo (sem ligacao com a tabela de IA do cliente): troca pela lista fixa
                        Log.WriteLine("Bots: bots.json antigo, trocando pela lista nova (nomes iguais aos do Init_us.agt).");
                        foreach (var c in Crew.All()) c.Members.RemoveAll(m => m >= CidBase);
                        bots = null;
                    }
                    if (bots == null || bots.Count == 0)
                    {
                        bots = Generate();
                        Save();
                        Log.WriteLine("Bots: {0} perfis criados em {1}.", bots.Count, FilePath);
                    }
                    if (EnsureSpecialBots()) Save();
                    // cada bot na linha da tabela de IA com o nome dele (o cliente mostra o nome pela linha)
                    try
                    {
                        int moved = 0;
                        foreach (var b in bots)
                        {
                            int row = Battle.AiRowFor(b.Name);
                            if (row > 0 && b.AiIdx != row) { b.AiIdx = row; moved++; }
                        }
                        if (moved > 0) { Save(); Log.WriteLine("Bots: {0} bot(s) com a linha nova da tabela de IA (nome na corrida).", moved); }
                    }
                    catch (Exception ex) { Log.WriteError("Bots tabela de IA: " + ex.Message); }
                    try { if (RiceServer.Config.CrewLadder && EnsureMostWanted()) Save(); } catch (Exception ex) { Log.WriteError("Most Wanted: " + ex.Message); }
                    try { if (EnsureNeoBots()) { Save(); Log.WriteLine("Bots: {0} bot(s) da Neo City.", bots.Count(x => x.NeoBot)); } } catch (Exception ex) { Log.WriteError("Bots da Neo City: " + ex.Message); }
                    try { if (AssignCities()) Save(); } catch (Exception ex) { Log.WriteError("Bots por cidade: " + ex.Message); }
                    try { if (NoPolice()) Save(); } catch (Exception ex) { Log.WriteError("Bots (carro de policia): " + ex.Message); }
                    // nomes compridos demais (o jogo corta em 12 letras e nao acha o bot para adicionar de amigo)
                    bool renamed = false;
                    foreach (var b in bots)
                    {
                        string nn;
                        if (b.Name != null && Renames.TryGetValue(b.Name, out nn) && !bots.Any(o => o != b && string.Equals(o.Name, nn, StringComparison.OrdinalIgnoreCase)))
                        { Log.WriteLine("Bots: {0} agora se chama {1}.", b.Name, nn); b.Name = nn; renamed = true; }
                    }
                    if (renamed) Save();
                    // bots novos da lista fixa (ex.: os 20 iniciantes) entram num bots.json antigo
                    try
                    {
                        var defs = Generate();
                        var have = new HashSet<long>(bots.Select(x => x.Cid));
                        var add = defs.Where(x => !have.Contains(x.Cid) && !bots.Any(y => string.Equals(y.Name, x.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                        if (add.Count > 0) { bots.AddRange(add); Save(); Log.WriteLine("Bots: {0} bot(s) novo(s) adicionado(s).", add.Count); }
                    }
                    catch (Exception ex) { Log.WriteError("Bots novos: " + ex.Message); }
                    // pintura: Marsteh vermelho, Brunizin.X preto, os outros uma cor da paleta do jogo (0-28)
                    foreach (var b in bots.Where(x => !x.CustomLook && (x.Look == null || x.Look.Paint == 0)))
                    {
                        int idx = b.Name == "Joaopvbr" ? 22
                                : b.Name == "Marsteh" ? RiceServer.Config.MarstehColor
                                : b.Name == "Brunizin.X" ? RiceServer.Config.BrunizinColor
                                : (int) ((b.Cid * 7 + 3) % 29);
                        b.Color = PaintColor(idx);
                    }
                    foreach (var b in bots.Where(x => x.Look != null && x.Look.Paint != 0 && !x.CustomLook)) b.Color = b.Look.Paint;
                    foreach (var b in bots.Where(x => x.Mito < 0))
                        b.Mito = 5000 + (long) b.Level * b.Level * 250 + Rng.Next(0, Math.Max(1, b.Level) * 4000);   // mito inicial pelo nivel
                    var av = RiceServer.Config.BotAvatarIds;
                    if (av != null && av.Length > 0)
                        foreach (var b in bots) b.Avatar = av[(int) (b.Cid % av.Length)];
                    try { if (ReorganizeCrews()) Save(); } catch (Exception ex) { Log.WriteError("Crews por cidade: " + ex.Message); }
                    try { if (ClassicCrews()) Save(); } catch (Exception ex) { Log.WriteError("Crews classicas: " + ex.Message); }
                    EnsureCrews();
                    int el = 0, tr = 0;
                    foreach (var b in bots) { if (TierCarFor(b)) tr++; if (EliteFor(b)) el++; }
                    if (el + tr > 0) { Save(); Log.WriteLine("Bots: {0} bot(s) com carro do V do nivel, {1} nivel 90+ com carro Xmas/ST/BT/GT.", tr, el); }
                }
                return bots;
            }
        }

        // Carros Xmas / ST / BT / GT: liberados no nivel 90 (Req Level 90 no Init). Bot nivel 90+ usa um deles.
        // Carros que o jogo desenha errado (bot com Monster CT virou um tanque e o jogo fechou). Bots, leilao, troca e
        // !!carro nao usam esses. UnsupportedCars no server.json troca a lista. (Venom e os outros que a loja nao lista
        // funcionam: compre com !!carro.)
        public static bool Unsupported(int car)
        {
            var l = RiceServer.Config.UnsupportedCars;
            return (l != null && Array.IndexOf(l, car) >= 0) || IsPolice(car) || ClientMissing().Contains(car);
        }
        // carros que este cliente do jogo nao tem (sem modelo): o instalador grava em res/carros_cliente.json
        static HashSet<int> clientMissing;
        static HashSet<int> ClientMissing()
        {
            if (clientMissing != null) return clientMissing;
            var set = new HashSet<int>();
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "carros_cliente.json");
                if (File.Exists(path))
                {
                    var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    var arr = j["missing"] as Newtonsoft.Json.Linq.JArray;
                    if (arr != null) foreach (var t in arr) set.Add((int) t);
                    if (set.Count > 0) Log.WriteLine("Carros: {0} carro(s) que este cliente nao tem ficam fora (res/carros_cliente.json).", set.Count);
                }
            }
            catch (Exception ex) { Log.WriteError("carros_cliente.json: " + ex.Message); }
            clientMissing = set;
            return set;
        }
        // carro de policia: so o admin (os bots nao tem, nao compram, nao trocam e nao correm com ele)
        public static bool IsPolice(int car)
        {
            var p = RiceServer.Config.PoliceCars;
            return p != null && Array.IndexOf(p, car) >= 0;
        }
        // bot que ja estava com carro de policia: vai para um carro da loja do mesmo V
        static bool NoPolice()
        {
            int n = 0;
            foreach (var b in bots.Where(x => IsPolice(x.Car)).ToList())
            {
                int g = Math.Max(1, Math.Min(9, BotSocial.GradeOf(b)));
                var ok = ShopCarsOf(g, b.Level >= 90);
                if (ok.Count == 0) ok = ShopCarsOf(TierForLevel(b.Level), b.Level >= 90);
                if (ok.Count == 0) continue;
                b.Car = ok[(int) (b.Cid % ok.Count)];
                if (b.CarGrade > 0) b.CarGrade = Math.Max(g, ShopTierOf(b.Car));
                if (b.Look != null) b.CustomLook = false;
                n++;
            }
            if (n > 0) Log.WriteLine("Bots: {0} bot(s) estavam com carro de policia e trocaram de carro (so o admin tem).", n);
            return n > 0;
        }

        public static readonly int[] EliteCars = { 141,142,146,147,150,151,154,155,158,159,163,164,166,167,169,170,172,173,187,188,189,192,193,196,197,199,200,202,203,206,207,228,229,236,237,238,239,245,246,249,250,253,254,256,257,259,260,264,265,267,268,271,272,274,275,276,277,278,279,282,283,288,289,292,293,295,296,299,300,306,307,309,310,312,313,315,316,319,320,322,323,325,326,329,330,338,339,341,342,345,346,349,350,354,355,357,358,360,361,364,365,368,369,371,372,375,376,382,383,386,387,389,390,393,394,398,399,402,403,407,408,411,412,415,416,418,419,422,423,426,427,430,431,433,434,441,442 };
        // carros que a loja mostra e o V de cada um, como o jogo mostra (res/ShopCars.json, da ordem da loja)
        class ShopCar { public int pos, id, v; public string name; }
        static List<ShopCar> shopCars;
        static List<ShopCar> ShopList()
        {
            if (shopCars != null) return shopCars;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "ShopCars.json");
                string json = null;
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ShopCars.json"))
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                shopCars = json != null ? JsonConvert.DeserializeObject<List<ShopCar>>(json) : new List<ShopCar>();
            }
            catch (Exception ex) { Log.WriteError("ShopCars.json: " + ex.Message); shopCars = new List<ShopCar>(); }
            return shopCars;
        }

        // V que a loja do jogo mostra para o carro (0 = a loja nao lista)
        public static int ShopGrade(int car)
        {
            var sc = ShopList().FirstOrDefault(x => x.id == car);
            return sc != null ? sc.v : 0;
        }

        public class BotCar { public int Car; public int Grade; public uint Color; }

        // garagem do bot (o carro atual sempre esta nela)
        public static List<BotCar> GarageOf(BotProfile b)
        {
            if (b.Garage == null) b.Garage = new List<BotCar>();
            var cur = b.Garage.FirstOrDefault(g => g.Car == b.Car);
            int grade = b.CarGrade > 0 ? b.CarGrade : TierForLevel(b.Level);
            if (cur == null) b.Garage.Add(new BotCar { Car = b.Car, Grade = grade, Color = b.Color });
            else { cur.Grade = grade; cur.Color = b.Color; }
            return b.Garage;
        }

        public static void UseCar(BotProfile b, BotCar g)
        {
            GarageOf(b);
            b.Car = g.Car; b.CarGrade = g.Grade; if (g.Color != 0) b.Color = g.Color;
            Save();
            CityBots.Respawn(b);
        }

        // o que o bot gosta de fazer: corrida ou bater (HUV/Rush). Decidido uma vez pelo jeito dele.
        public static string FocusOf(BotProfile b)
        {
            if (b == null) return "corrida";
            if (string.IsNullOrEmpty(b.Focus))
            {
                switch (b.Personality)
                {
                    case "competitivo": b.Focus = "corrida"; break;
                    case "calado": b.Focus = "bater"; break;
                    default: b.Focus = b.Cid % 2 == 0 ? "corrida" : "bater"; break;
                }
            }
            return b.Focus;
        }

        // para que o carro e bom: durabilidade maior = bater; velocidade ou aceleracao maior = corrida; empate = os dois
        public static string CarKind(int car)
        {
            Rice.Server.Structures.Resources.VehicleListEntry e;
            if (!Rice.Server.Structures.Resources.VehicleTable.Vehicles.TryGetValue(car, out e) || e == null) return "ambos";
            int run = Math.Max(e.Speed, e.Accel);
            return e.Crash > run ? "bater" : run > e.Crash ? "corrida" : "ambos";
        }

        // carros da loja de um V que combinam com o gosto do bot (os melhores primeiro); sem nenhum, todos do V
        public static List<int> ShopCarsFor(int v, BotProfile b, bool elite)
        {
            var all = ShopCarsOf(v, elite);
            var vt = Rice.Server.Structures.Resources.VehicleTable.Vehicles;
            string f = FocusOf(b);
            var fit = all.Where(c => { string k = CarKind(c); return k == f || k == "ambos"; }).ToList();
            if (fit.Count == 0) return all;
            Func<int, int> score = c => { var e = vt[c]; return f == "bater" ? e.Crash * 2 + Math.Max(e.Speed, e.Accel) : Math.Max(e.Speed, e.Accel) * 2 + Math.Min(e.Speed, e.Accel); };
            return fit.OrderByDescending(score).ToList();
        }

        // carros da loja de um V (sem treino, sem os que o jogo desenha errado e sem Xmas/ST/BT/GT abaixo do 90)
        public static List<int> ShopCarsOf(int v, bool elite)
        {
            var vt = Rice.Server.Structures.Resources.VehicleTable.Vehicles;
            return ShopList().Where(x => x.v == v && x.id != 95 && !Unsupported(x.id) && vt.ContainsKey(x.id)
                                         && (elite || Array.IndexOf(EliteCars, x.id) < 0)).Select(x => x.id).ToList();
        }

        // V em que a loja mostra o carro (0 = nao esta na loja)
        public static int ShopTierOf(int car) { var x = ShopList().FirstOrDefault(s => s.id == car); return x != null ? x.v : 0; }

        // V do carro pelo nivel, igual a loja (CarTierLevels: V1 0, V2 10, V3 20, V4 30, V5 40, V6 50, V7 60, V8 75, V9 80)
        public static int TierForLevel(int level)
        {
            var lv = RiceServer.Config.CarTierLevels ?? new[] { 0, 10, 20, 30, 40, 50, 60, 75, 80 };
            int t = 1;
            for (int i = 0; i < lv.Length && i < 9; i++) if (level >= lv[i]) t = i + 1;
            return t;
        }

        // bot com carro de V diferente do nivel dele -> um carro da loja desse V (os Xmas/ST/BT/GT so no nivel 90+)
        public static bool TierCarFor(BotProfile b)
        {
            if (b != null && b.MostWanted) return false;           // Most Wanted: carro fixo da Blacklist
            if (b == null || b.Special || b.IsAlt || b.CustomLook) return false;
            var v = Rice.Server.Structures.Resources.VehicleTable.Vehicles;
            Rice.Server.Structures.Resources.VehicleListEntry cur;
            // carro ganho na troca/leilao: fica, se o bot tiver nivel para o V do carro; senao volta ao carro do V dele
            bool levelOk = v.TryGetValue(b.Car, out cur) && cur != null && b.Level >= Rice.Game.Character.GetRequiredLevelForTier(cur.Level);
            int st = ShopTierOf(b.Car);
            if (st > 0 && b.CarGrade > 0 && b.CarGrade < st) { b.CarGrade = 0; levelOk = false; }   // carro abaixo do V que a loja mostra (ex.: Allied V1)
            if (b.CarGrade > 0 && !Unsupported(b.Car) && levelOk) return false;
            if (Unsupported(b.Car) || !levelOk) b.CarGrade = 0;
            int want = TierForLevel(b.Level);
            bool elite = Array.IndexOf(EliteCars, b.Car) >= 0;
            if (ShopList().Count == 0 && v.TryGetValue(b.Car, out cur) && cur != null && cur.Level == want && (!elite || b.Level >= 90) && !Unsupported(b.Car)) return false;
            // os carros que a loja mostra nesse V (como no jogo); sem a lista, o jeito antigo
            string kind0 = CarKind(b.Car);
            if (ShopTierOf(b.Car) == want && !Unsupported(b.Car) && (!elite || b.Level >= 90) && (kind0 == FocusOf(b) || kind0 == "ambos")) return false;
            // os melhores do V para o gosto dele (corrida ou bater); um dos 3 primeiros
            var ok = ShopCarsFor(want, b, false);
            if (ok.Count > 3) ok = ok.Take(3).ToList();
            if (ok.Count == 0)
                ok = v.Where(kv => kv.Value != null && kv.Value.Sellable == 1 && kv.Value.Level == want && kv.Key != 95
                                   && (want >= 9 || (kv.Value.UiName ?? "").IndexOf("MINI", StringComparison.OrdinalIgnoreCase) < 0)   // MINI: V9 (80+)
                                   && Array.IndexOf(EliteCars, kv.Key) < 0 && !Unsupported(kv.Key))
                      .Select(kv => kv.Key).OrderBy(x => x).ToList();
            if (ok.Count == 0) return false;
            b.Car = ok[(int) (b.Cid % ok.Count)];
            return true;
        }

        public static bool EliteFor(BotProfile b)
        {
            if (b != null && b.MostWanted) return false;           // Most Wanted: carro fixo da Blacklist
            if (b == null || b.Special || b.IsAlt || b.CarGrade > 0) return false;     // fixos, seus corredores e carro ganho na troca: nao mexe
            bool elite = Array.IndexOf(EliteCars, b.Car) >= 0 && !Unsupported(b.Car);
            if (b.Level >= 90 && !elite)
            {
                var ok = EliteCars.Where(c => Rice.Server.Structures.Resources.VehicleTable.Vehicles.ContainsKey(c) && !Unsupported(c)).ToList();
                if (ok.Count == 0) return false;
                b.Car = ok[(int) (b.Cid % ok.Count)];
                return true;
            }
            return false;
        }

        public static void Save()
        {
            lock (Sync)
            {
                try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(bots, Formatting.Indented)); }
                catch (Exception ex) { Log.WriteError("Nao consegui gravar bots.json: " + ex.Message); }
            }
        }

        public static BotProfile ByCid(long cid)
        {
            if (cid < CidBase)
            {
                lock (alts) { BotProfile a; return alts.TryGetValue(cid, out a) ? a : null; }
            }
            return All().FirstOrDefault(b => b.Cid == cid);
        }

        public static BotProfile ByName(string name)
        {
            string n = (name ?? "").Trim();
            lock (alts)
            {
                var a = alts.Values.FirstOrDefault(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase));
                if (a != null) return a;
            }
            var all = All();
            var exact = all.FirstOrDefault(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase));
            if (exact != null || n.Length < 8) return exact;
            // o jogo cortou o nome (12 letras): acha pelo comeco, se so um bate
            var pre = all.Where(b => b.Name != null && b.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase)).ToList();
            return pre.Count == 1 ? pre[0] : null;
        }

        // ---- outros corredores da conta (alts) -----------------------------------------------------------------
        // Os corredores que voce nao esta usando viram bots: amigos automaticos, podem entrar na party e ganham
        // exp/mito junto com voce (AltRewardShare). Quando voce entrar com eles, as recompensas ja estao la.
        static readonly Dictionary<long, BotProfile> alts = new Dictionary<long, BotProfile>();

        public static void RegisterAlts(Rice.Game.Player p)
        {
            if (p == null || p.ActiveCharacter == null || p.Characters == null) return;
            var me = p.ActiveCharacter;
            lock (alts)
            {
                foreach (var k in alts.Where(kv => kv.Value.AltOwner == (long) me.CID || kv.Key == (long) me.CID).Select(kv => kv.Key).ToList())
                    alts.Remove(k);
                foreach (var c in p.Characters)
                {
                    if (c == null || c.CID == me.CID) continue;
                    var v = c.Garage != null ? c.Garage.FirstOrDefault(g => g.CarID == c.CurrentCarID) : null;
                    alts[(long) c.CID] = new BotProfile
                    {
                        Cid = (long) c.CID, Name = c.Name, Level = c.Level, Car = v != null ? (int) v.CarType : 1,
                        Color = v != null && v.Color != 0 ? v.Color : 0x01010102, Avatar = c.Avatar, Personality = "amigavel",
                        IsAlt = true, AltOwner = (long) me.CID, Joined = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        InTutorial = Bots.InTutorial(c), AiIdx = AltAiRow(c.Name, (long) c.CID)
                    };
                }
                // corredores das OUTRAS contas (outros logins) tambem viram bots seus: amigos, aparecem no lobby, podem
                // entrar na party e ganham junto - mas nao fazem nada sozinhos (AltsAllAccounts)
                if (RiceServer.Config.AltsAllAccounts)
                {
                    try
                    {
                        var mine = new HashSet<long>(p.Characters.Where(x => x != null).Select(x => (long) x.CID));
                        using (var rc = Rice.Database.GetContext())
                        {
                            var others = rc.Characters.Where(x => !mine.Contains(x.ID)).ToList();
                            var ids = others.Select(x => x.ID).ToList();
                            var cars = rc.Vehicles.Where(vh => ids.Contains(vh.CID)).ToList();
                            foreach (var oc in others)
                            {
                                var cv = cars.Where(vh => vh.CID == oc.ID).ToList();
                                var ocar = cv.FirstOrDefault(g => g.CarID == oc.CurrentCarID) ?? cv.FirstOrDefault();
                                alts[oc.ID] = new BotProfile
                                {
                                    Cid = oc.ID, Name = oc.Name, Level = oc.Level, Car = ocar != null ? ocar.CarType : 1,
                                    Color = ocar != null && ocar.Color != 0 ? (uint) ocar.Color : 0x01010102, Avatar = oc.Avatar, Personality = "amigavel",
                                    IsAlt = true, AltOwner = (long) me.CID, Joined = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                    InTutorial = cv.Count > 0 && cv.All(g => Rice.Game.Vehicle.IsTrainingType(g.CarType)),
                                    AiIdx = AltAiRow(oc.Name, oc.ID)
                                };
                            }
                        }
                    }
                    catch (Exception ex) { Log.WriteError("Corredores das outras contas: " + ex.Message); }
                }
            }
        }

        // linha da tabela de IA para o seu corredor correr na Battle Zone (a dele, se tiver; senao uma livre)
        static int AltAiRow(string name, long cid)
        {
            try { int r = Battle.AiRowFor(name); return r > 0 ? r : Battle.SpareAiRow(cid); }
            catch (Exception) { return 0; }
        }

        // corredor que ainda nao terminou o tutorial (so tem o carro de treino ou esta usando ele)
        public static bool InTutorial(Rice.Game.Character c)
        {
            if (c == null || c.Garage == null) return false;
            if (c.Vehicle != null && c.Vehicle.IsTraining) return true;
            return c.Garage.Count > 0 && c.Garage.All(g => g.IsTraining);
        }

        public static List<long> AltCids() { lock (alts) return alts.Keys.ToList(); }

        public static List<BotProfile> AltsOf(Rice.Game.Character me)
        {
            if (me == null) return new List<BotProfile>();
            lock (alts) return alts.Values.Where(a => a.AltOwner == (long) me.CID).ToList();
        }

        public static bool IsBot(long cid) { return cid >= CidBase && ByCid(cid) != null; }

        public static BotProfile Pick(Func<BotProfile, bool> filter = null)
        {
            bool mw = RiceServer.Config.CrewLadder;     // Most Wanted so existe com o mod Escada de crews
            var l = All().Where(b => (mw || !b.MostWanted) && !b.NeoBot && (filter == null || filter(b))).ToList();
            return l.Count == 0 ? null : l[Rng.Next(l.Count)];
        }

        static List<BotProfile> Generate()
        {
            // Lista fixa embutida (res/bots.default.json): os nomes batem com a tabela de IA do Init_us.agt
            // modificado, para o nome do bot aparecer na corrida.
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var st = asm.GetManifestResourceStream("bots.default.json"))
                    if (st != null)
                        using (var rd = new StreamReader(st, Encoding.UTF8))
                        {
                            var fixedList = JsonConvert.DeserializeObject<List<BotProfile>>(rd.ReadToEnd());
                            if (fixedList != null && fixedList.Count > 0) return fixedList;
                        }
            }
            catch (Exception ex) { Log.WriteError("bots.default.json: " + ex.Message); }
            return GenerateRandom();
        }

        static List<BotProfile> GenerateRandom()
        {
            int count = Math.Max(CrewSeeds.Sum(s => s.Size) + 5, RiceServer.Config.BotsCount);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<BotProfile>();
            string[] pers = { "amigavel", "competitivo", "comerciante", "calado" };
            int now = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long next = CidBase + 1;

            Func<int, BotProfile> make = level =>
            {
                string name;
                int guard = 0;
                do
                {
                    name = Names[Rng.Next(Names.Length)] + Suffixes[Rng.Next(Suffixes.Length)];
                    if (name.Length > 12) name = name.Substring(0, 12);
                } while (used.Contains(name) && ++guard < 200);
                if (used.Contains(name)) name = "Bot" + (next - CidBase);
                used.Add(name);
                return new BotProfile
                {
                    Cid = next++, Name = name, Level = level, Car = CarFor(level),
                    Color = (uint) (0x01000000 | Rng.Next(0x10000, 0xFFFFFF)),
                    Personality = pers[Rng.Next(pers.Length)],
                    Joined = now - Rng.Next(86400, 86400 * 400),
                    Reg = (uint) Rng.Next(0, 60), Bought = (uint) Rng.Next(0, 80), Sold = (uint) Rng.Next(0, 50)
                };
            };

            foreach (var s in CrewSeeds)
                for (int i = 0; i < s.Size; i++)
                {
                    var b = make(Rng.Next(s.LevelLo, s.LevelHi + 1));
                    b.CrewName = s.Name;
                    list.Add(b);
                }
            while (list.Count < count)
                list.Add(make(Rng.Next(1, 100)));
            return list;
        }

        // Crews por cidade (uma vez): Rookies/MoonFlow (Moon Palace), MitoKings/KoiGhost (Koinonia), CrasRacers/CrasElite
        // (Cras), OrosLegends (Oros) e LegendsDC. Em cada cidade, dos moradores em ordem de nivel: os 10 mais baixos na 1a
        // crew, os 10 do meio sem crew e os 10 mais altos na 2a (Oros: os 10 mais altos na OrosLegends). KoiFlow vira
        // MoonFlow, CrasGhosts vira KoiGhost e a NightRiders acaba.
        // mod desligado: volta as crews do modo classico (uma vez, se estavam no formato da escada)
        static readonly string[] LadderOnly = { "MoonFlow", "KoiGhost", "CrasRacers", "CrasElite", MostWantedCrew };
        static bool ClassicCrews()
        {
            if (RiceServer.Config.CrewLadder || !bots.Any(b => !b.MostWanted && LadderOnly.Contains(b.CrewName))) return false;
            var crews = Crew.All();
            foreach (var c in crews.Where(c => c.IsBot).ToList())
            {
                if (c.Name == "MoonFlow") c.Name = "KoiFlow";
                else if (c.Name == "KoiGhost") c.Name = "CrasGhosts";
                if (c.Name == EliteCrew) continue;
                if (NeoCrewSeeds.Any(s => s.Name == c.Name)) continue;
                if (!ClassicSeeds.Any(s => s.Name == c.Name)) { crews.Remove(c); continue; }
                c.Members.Clear();
                var seed = ClassicSeeds.First(s => s.Name == c.Name);
                c.Desc = seed.Desc; c.MinLevel = seed.MinLevel; c.Mark = seed.Mark;
            }
            foreach (var b in bots.Where(b => !b.Special && !IsAce(b) && !b.IsAlt && !b.NeoBot))
            {
                var cur = Crew.ById(b.CrewTid);
                if (cur != null && !cur.IsBot && !b.MostWanted) continue;      // ja esta numa crew de jogador: fica
                b.CrewName = ""; b.CrewTid = 0;
            }
            foreach (var s in ClassicSeeds.Where(x => x.Size > 0))
                foreach (var b in bots.Where(b => !b.Special && !IsAce(b) && !b.IsAlt && !b.MostWanted && !b.NeoBot && string.IsNullOrEmpty(b.CrewName)
                                                  && b.Level >= s.LevelLo && b.Level <= s.LevelHi).OrderBy(b => (b.Cid * 7919) % 1000).Take(s.Size).ToList())
                    b.CrewName = s.Name;
            Crew.SaveAll();
            Log.WriteLine("Crews: modo classico ({0}).", string.Join(", ", ClassicSeeds.Select(s => s.Name + " " + bots.Count(b => b.CrewName == s.Name))));
            return true;
        }

        static bool ReorganizeCrews()
        {
            if (!RiceServer.Config.CrewLadder || bots.Any(b => b.CrewName == "MoonFlow" || b.CrewName == "CrasRacers")) return false;
            var crews = Crew.All();
            foreach (var c in crews.Where(c => c.IsBot).ToList())
            {
                if (c.Name == "KoiFlow") c.Name = "MoonFlow";
                else if (c.Name == "CrasGhosts") c.Name = "KoiGhost";
                if (c.Name == EliteCrew) continue;
                if (NeoCrewSeeds.Any(s => s.Name == c.Name)) continue;
                if (!CrewSeeds.Any(s => s.Name == c.Name)) { crews.Remove(c); continue; }
                c.Members.Clear();
                var seed = CrewSeeds.First(s => s.Name == c.Name);
                c.Desc = seed.Desc; c.MinLevel = seed.MinLevel; c.Mark = seed.Mark;
            }
            foreach (var b in bots.Where(b => !b.Special && !IsAce(b) && !b.IsAlt && !b.MostWanted && !b.NeoBot))
            {
                var cur = Crew.ById(b.CrewTid);
                if (cur != null && !cur.IsBot) continue;          // ja esta numa crew de jogador: fica
                b.CrewName = ""; b.CrewTid = 0;
            }
            for (int city = 1; city <= 4; city++)
            {
                var res = bots.Where(b => b.HomeCity == city && !b.Special && !IsAce(b) && !b.IsAlt && !b.MostWanted && string.IsNullOrEmpty(b.CrewName))
                              .OrderBy(b => b.Level).ThenBy(b => b.Cid).ToList();
                var seeds = CrewSeeds.Where(s => s.City == city).ToList();
                if (seeds.Count == 0 || res.Count == 0) continue;
                int per = seeds[0].Size;
                var low = res.Take(per).ToList();
                var high = res.Skip(Math.Max(per, res.Count - per)).ToList();
                if (seeds.Count == 1) { foreach (var b in high) b.CrewName = seeds[0].Name; }
                else { foreach (var b in low) b.CrewName = seeds[0].Name; foreach (var b in high) b.CrewName = seeds[1].Name; }
            }
            Crew.SaveAll();
            Log.WriteLine("Crews por cidade montadas: {0}.", string.Join(", ", CrewSeeds.Select(s => s.Name + " " + bots.Count(b => b.CrewName == s.Name))));
            return true;
        }

        public static string[] LadderCrews() { return CrewSeeds.Where(s => s.City > 0).Select(s => s.Name).Concat(new[] { EliteCrew }).ToArray(); }
        public static int CityOfCrew(string name) { var s = CrewSeeds.FirstOrDefault(x => x.Name == name); return s != null ? (s.City > 0 ? s.City : 4) : 0; }

        // Cria (ou recria) as crews dos bots em crews.json.
        static void EnsureCrews()
        {
            var crews = Crew.All();
            bool changed = false;
            long tid = 50000;
            // LegendsDC virou LegendsDC
            foreach (var c in crews.Where(c => c.IsBot && c.Name == OldEliteCrew)) { c.Name = EliteCrew; changed = true; }
            foreach (var b in bots.Where(b => b.CrewName == OldEliteCrew)) b.CrewName = EliteCrew;
            // bot que mudou de crew (ex.: Joaopvbr, Marsteh e Brunizin.X sairam da LegendsDC) sai da crew antiga
            foreach (var c in crews.Where(c => c.IsBot))
            {
                int n = c.Members.RemoveAll(m => { var b = bots.FirstOrDefault(x => x.Cid == m); return b != null && b.CrewName != c.Name; });
                if (n > 0) changed = true;
            }
            foreach (var s in AllSeeds())
            {
                tid++;
                var members = bots.Where(b => b.CrewName == s.Name).OrderByDescending(b => b.Level).ThenByDescending(b => IsAce(b)).ToList();
                if (members.Count == 0) continue;
                var t = crews.FirstOrDefault(c => c.IsBot && c.Name == s.Name);
                if (t == null)
                {
                    if (crews.Any(c => c.Tid == tid)) tid = crews.Max(c => c.Tid) + 1;
                    t = new Crew.CrewData
                    {
                        Tid = tid, Mark = s.Mark, Name = s.Name, Desc = s.Desc, IsBot = true, MinLevel = s.MinLevel,
                        Master = members[0].Cid, MasterName = members[0].Name,
                        Created = (int) DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400 * 500
                    };
                    crews.Add(t);
                    changed = true;
                }
                foreach (var b in members)
                {
                    if (!t.Members.Contains(b.Cid)) { t.Members.Add(b.Cid); changed = true; }
                    b.CrewTid = t.Tid;
                }
                if (t.IsBot && t.Master != members[0].Cid && !(t.Master > 0 && t.Master < CidBase))
                {
                    t.Master = members[0].Cid; t.MasterName = members[0].Name; changed = true;
                }
            }
            // LegendsDC: o RiceAdmin (perfil upado) e o lider; sai de outra crew de bots em que estivesse
            try
            {
                var leg = crews.FirstOrDefault(c => c.IsBot && c.Name == EliteCrew);
                string upped = RiceServer.Config.UppedProfileName;
                if (leg != null && !string.IsNullOrEmpty(upped))
                {
                    long adminCid = 0; string adminName = upped;
                    using (var rc = Rice.Database.GetContext())
                    {
                        var ach = rc.Characters.FirstOrDefault(x => x.Name == upped);
                        if (ach != null) { adminCid = ach.ID; adminName = ach.Name; }
                    }
                    if (adminCid > 0)
                    {
                        foreach (var c in crews.Where(c => c != leg && c.IsBot && c.Members.Contains(adminCid))) { c.Members.Remove(adminCid); changed = true; }
                        if (!leg.Members.Contains(adminCid)) { leg.Members.Insert(0, adminCid); changed = true; }
                        if (leg.Master != adminCid) { leg.Master = adminCid; leg.MasterName = adminName; changed = true; }
                    }
                }
            }
            catch (Exception ex) { Log.WriteError("LegendsDC: " + ex.Message); }
            if (changed)
            {
                Crew.SaveAll();
                Log.WriteLine("Bots: crews de bots gravadas em crews.json.");
            }
        }

        // ---- bots andando juntos (aceitaram a party de outro bot) e encontro de troca (um vai ate o outro) ----------
        public class Link { public long Leader; public bool Meet; public DateTime Until; public int Slot; }
        static readonly Dictionary<long, Link> links = new Dictionary<long, Link>();

        public static void Group(BotProfile leader, IEnumerable<BotProfile> members, double seconds)
        {
            if (leader == null) return;
            lock (links)
            {
                int slot = links.Values.Count(l => l.Leader == leader.Cid && !l.Meet);
                foreach (var m in members)
                    if (m != null && m != leader && !m.IsAlt)
                        links[m.Cid] = new Link { Leader = leader.Cid, Until = DateTime.UtcNow.AddSeconds(seconds), Slot = slot++ };
            }
        }

        public static void Meet(BotProfile mover, BotProfile target, double seconds)
        {
            if (mover == null || target == null) return;
            lock (links) links[mover.Cid] = new Link { Leader = target.Cid, Meet = true, Until = DateTime.UtcNow.AddSeconds(seconds) };
        }

        // o bot vai ate voce e para de frente (troca com o jogador)
        public static void MeetPlayer(BotProfile mover, double seconds)
        {
            if (mover == null || mover.IsAlt) return;
            lock (links) links[mover.Cid] = new Link { Leader = 0, Meet = true, Until = DateTime.UtcNow.AddSeconds(seconds) };
        }

        public static void Unlink(long cid)
        {
            lock (links) links.Remove(cid);
        }

        public static void Ungroup(long leaderCid)
        {
            lock (links) foreach (var k in links.Where(kv => kv.Value.Leader == leaderCid || kv.Key == leaderCid).Select(kv => kv.Key).ToList()) links.Remove(k);
        }

        public static Link LinkOf(long cid)
        {
            lock (links)
            {
                Link l;
                if (!links.TryGetValue(cid, out l)) return null;
                if (DateTime.UtcNow > l.Until) { links.Remove(cid); return null; }
                return l;
            }
        }

        public static List<KeyValuePair<long, Link>> Links()
        {
            lock (links) return links.Where(kv => DateTime.UtcNow <= kv.Value.Until).ToList();
        }

        // ---- mensagens ----------------------------------------------------------------------------------------

        // Mensagem de chat em nome de alguem (type: "channel", "team", "room", "party").
        public static void Say(RiceClient c, string type, string sender, string message)
        {
            if (c == null) return;
            var p = new RicePacket(147);
            p.Writer.WriteUnicodeStatic(type, 10);
            p.Writer.WriteUnicodeStatic(sender, 18);
            p.Writer.WriteUnicode(Lang.T(message));
            c.Send(p);
        }

        public static void SayLater(RiceClient c, int ms, string type, string sender, string message)
        {
            Task.Delay(ms).ContinueWith(_ =>
            {
                try { Say(c, type, sender, message); }
                catch (Exception ex) { Log.WriteError("Bots.SayLater: " + ex.Message); }
            });
        }

        // Janela de erro do cliente (pacote 1: texto).
        public static void Popup(RiceClient c, string text)
        {
            // a janela de erro (pacote 1) derrubava o cliente: aviso no chat
            if (c != null) Chat.SendSystemMessage(c, text ?? "");
        }

        public static RiceClient ClientOf(long cid)
        {
            foreach (var c in RiceServer.Game.GetClients())
            {
                var ch = c.Player != null ? c.Player.ActiveCharacter : null;
                if (ch != null && (long) ch.CID == cid) return c;
            }
            return null;
        }

        static readonly string[] Greetings =
        {
            "Bem-vindo(a) a crew, {0}!", "Opa {0}, chegou reforco!", "Seja bem-vindo(a) {0}. Bora correr?",
            "Eae {0}! Qualquer coisa chama no chat da crew.", "{0} entrou! Agora ninguem segura a gente."
        };
        static readonly string[] Replies =
        {
            "kkk", "Boa!", "Bora pro Battle Zone?", "To farmando mito aqui em Moon Palace.", "Alguem tem Enhancer pra vender?",
            "Acabei de pegar uma peca H no leilao!", "Hoje eu bato meu recorde.", "Cuidado com os HUV em Cras!",
            "Concordo.", "Partiu Lap Time?", "Valeu!", "Ja ja eu entro na corrida.", "Meu carro ta sem combustivel kkk",
            "Quem quer apostar uma corrida?", "Vou upar mais um pouco e ja volto.", "To no leilao, ta cheio de coisa boa hoje."
        };

        public static void WelcomeToCrew(RiceClient c, Crew.CrewData t, string playerName)
        {
            if (c == null || t == null) return;
            var mates = t.Members.Select(ByCid).Where(b => b != null).ToList();
            if (mates.Count == 0) return;
            var a = mates[Rng.Next(mates.Count)];
            SayLater(c, 1500, "team", a.Name, string.Format(Greetings[Rng.Next(Greetings.Length)], playerName));
            if (mates.Count > 1)
            {
                var b = mates.Where(x => x != a).ElementAt(Rng.Next(mates.Count - 1));
                SayLater(c, 4000, "team", b.Name, Replies[Rng.Next(Replies.Length)]);
            }
        }

        // O jogador escreveu no chat da crew: um bot da crew responde (as vezes).
        public static void OnPlayerTeamChat(RiceClient c, string message)
        {
            var cfg = RiceServer.Config;
            if (!cfg.BotsEnabled || !cfg.BotsCrewChat || c == null || c.Player == null || c.Player.ActiveCharacter == null) return;
            var t = Crew.Of((long) c.Player.ActiveCharacter.CID);
            if (t == null) return;
            var mates = t.Members.Select(ByCid).Where(b => b != null && b.Personality != "calado").ToList();
            if (mates.Count == 0 || Rng.NextDouble() > 0.7) return;
            var who = mates[Rng.Next(mates.Count)];
            string m = message.ToLowerInvariant();
            string reply;
            if (m.Contains("oi") || m.Contains("ola") || m.Contains("eae") || m.Contains("hello") || m.Contains("hi"))
                reply = "Oi " + c.Player.ActiveCharacter.Name + "!";
            else if (m.Contains("corr") || m.Contains("race") || m.Contains("battle"))
                reply = who.Personality == "competitivo" ? "Bora! Cria a sala que eu entro e ganho de voce kkk" : "Partiu! Cria a sala no Battle Zone.";
            else if (m.Contains("leil") || m.Contains("vend") || m.Contains("compr"))
                reply = who.Personality == "comerciante" ? "Da uma olhada nos meus anuncios no leilao, preco bom!" : "O leilao ta movimentado hoje.";
            else
                reply = Replies[Rng.Next(Replies.Length)];
            SayLater(c, 1200 + Rng.Next(2500), "team", who.Name, reply);
        }

        // ---- comandos de chat -----------------------------------------------------------------------------------

        // Retorna true se o comando era de bots.
        public static bool HandleCommand(RiceClient c, string lower, string body)
        {
            if (!RiceServer.Config.BotsEnabled) return false;
            var me = c.Player.ActiveCharacter;
            if (lower.StartsWith("visualbot"))
            {
                // !!visualbot <bot> : o bot fica com o seu carro atual (modelo, grau, cor e pecas visuais do drift shop)
                // !!visualbot <bot> limpar : volta ao normal
                string arg = body.Length > 9 ? body.Substring(9).Trim() : "";
                bool clear = arg.EndsWith(" limpar", StringComparison.OrdinalIgnoreCase);
                if (clear) arg = arg.Substring(0, arg.Length - 7).Trim();
                var b = ByName(arg);
                if (b == null || b.IsAlt) { Chat.SendSystemMessage(c, "Uso: !!visualbot <nome do bot>  (ex.: !!visualbot Joaopvbr). Para desfazer: !!visualbot <nome> limpar"); return true; }
                if (clear)
                {
                    b.CustomLook = false; b.Visuals = new Dictionary<string, int>();
                    BotLook pl2;
                    b.Look = PresetLooks().TryGetValue(b.Name, out pl2) ? pl2 : null;
                    if (b.Look != null && b.Look.Paint != 0) b.Color = b.Look.Paint;
                    Save();
                    Chat.SendSystemMessage(c, b.Name + " voltou ao visual normal (o carro dele volta quando o servidor reiniciar).");
                    return true;
                }
                var car = me.Vehicle;
                if (car == null) { Chat.SendSystemMessage(c, "Voce precisa estar com um carro."); return true; }
                if (car.IsTraining || Unsupported((int) car.CarType)) { Chat.SendSystemMessage(c, "Esse carro nao pode ir para um bot."); return true; }
                var look = VisualShop.LookOf(me, car);
                b.Car = (int) car.CarType; b.CarGrade = (int) Math.Max(1, Math.Min(9, car.Grade)); b.Color = car.Color;
                if (look.Paint != 0) b.Color = look.Paint;
                b.Look = look; b.CustomLook = true;
                Save();
                Log.WriteLine("Bots: visual de {0} copiado do carro {1} de {2} ({3} peca(s) visuais).", b.Name, car.CarType, me.Name, look.Slots.Count);
                Chat.SendSystemMessage(c, "Visual salvo em " + b.Name + ": " + CarName(b.Car) + " V" + b.CarGrade + ", sua pintura" + (look.Tint != 0 ? ", o vidro" : "") + " e " + look.Slots.Count
                    + " peca(s) visuais. Fica salvo para sempre. Saia e entre na cidade para ver.");
                return true;
            }
            if (lower == "crews")
            {
                Chat.SendSystemMessage(c, "Crews de bots (entre pelo Crew Center > Join, digitando o nome):");
                foreach (var t in Crew.All().Where(x => x.IsBot).OrderBy(x => x.MinLevel))
                    Chat.SendSystemMessage(c, string.Format("  {0} - nivel minimo {1} - {2} membros - lider {3}",
                        t.Name, t.MinLevel, t.Members.Count, t.MasterName));
                return true;
            }
            if (lower.StartsWith("perfil"))
            {
                string name = body.Length > 6 ? body.Substring(6).Trim() : "";
                var b = ByName(name);
                if (b == null) { Chat.SendSystemMessage(c, "Bot '" + name + "' nao encontrado. Use !bots para ver alguns."); return true; }
                var t = Crew.Of(b.Cid);
                Chat.SendSystemMessage(c, string.Format("{0}: nivel {1}, carro {2}, crew {3}, estilo {4}. Leilao: {5} vendas, {6} compras.",
                    b.Name, b.Level, CarName(b.Car), t != null ? t.Name : "nenhuma", b.Personality, b.Sold, b.Bought));
                Chat.SendSystemMessage(c, "Licencas de " + b.Name + ": " + Licenses.BotLicenseNames(b) + ".");
                return true;
            }
            if (lower == "help" || lower == "ajuda" || lower == "comandos")
            {
                string[] help =
                {
                    "Comandos (digite com !! na frente, ex.: !!crews):",
                    "  !!help - esta lista",
                    "  !!carro <id ou nome> - compra um carro (inclusive evento/gacha)",
                    "  !!kit / !!tirar / !!tirar todos - tira pecas Sorched (H/U) do carro",
                    "  !!crews - crews de bots   |   !!entrar <crew> - entra numa crew de bots",
                    "  !!membros - membros da sua crew   |   !!sair crew - sai (lider: apaga a crew)",
                    "  !!recrutar [nome] - (lider) um bot (ou o bot que voce disser) entra na sua crew",
                    "  !!bots - alguns bots   |   !!perfil <nome> - perfil de um bot",
                    "  !!amigo <nome> - adiciona um bot como amigo   |   !!amigos - sua lista",
                    "  !!caixa - guarda um carro da garagem numa caixa (para trocar com bot)",
                    "  !!visualbot <bot> - o bot fica com o seu carro atual (modelo, cor e pecas visuais); !!visualbot <bot> limpar desfaz",
                    "  !!visual - guarda uma peca do drift shop numa caixa (para trocar com bot)",
                    "  !!party <nome> - chama um bot para a party   |   !!party - quem esta   |   !!party sair",
                    "  !!<nome do bot da party> - ele mostra a garagem   |   !!<nome> <numero> - ele troca para esse carro",
                    "  (bots da party entram sozinhos na sala que voce cria no Battle Zone e dao bonus no HUV)",
                    "  !!leilao - resumo dos seus anuncios no leilao",
                    "  !!licencas - progresso das licencas que faltam",
                    "  !!durabilidade - durabilidade das pecas da Neo City montadas (repare com os Repair Kits da Drift Shop)",
                    "  !!producao - nivel e receitas de fabricacao | !!livro [nome] - compra Skill Book (50.000) | !!kit <1|2|3> [qtd]",
                    "  !!clima 1-4 - testa um ceu (limpo, fim de tarde, chuva, neblina)   |   !!clima 0 - ciclo automatico",
                };
                foreach (var h in help) Chat.SendSystemMessage(c, h);
                return true;
            }
            // teste: !!stat <0-18> <valor> (inteiro ou numero com f, ex.: 120.5f) muda um campo depois dos totais do
            // StatUpdate e manda de novo; !!stat limpar zera. Para descobrir o que mostra mph/tempo/assist no inventario.
            if (lower == "stat" || lower.StartsWith("stat "))
            {
                var parts = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1].ToLowerInvariant() == "limpar") me.StatProbe = new int[19];
                // !!stat teste: os 19 campos com 1.0, 2.0 ... 19.0 (numero com virgula); !!stat testeint: 1, 2 ... 19 (inteiro).
                // Tire print do "Assist Option" e da performance no inventario para ver qual campo e qual.
                else if (parts.Length >= 2 && parts[1].ToLowerInvariant() == "teste")
                    for (int i = 0; i < 19; i++) me.StatProbe[i] = BitConverter.ToInt32(BitConverter.GetBytes((float) (i + 1)), 0);
                else if (parts.Length >= 2 && parts[1].ToLowerInvariant() == "testeint")
                    for (int i = 0; i < 19; i++) me.StatProbe[i] = i + 1;
                else if (parts.Length >= 3)
                {
                    int idx; int iv; float fv;
                    string v = parts[2];
                    if (!int.TryParse(parts[1], out idx) || idx < 0 || idx > 18) { Chat.SendSystemMessage(c, "Uso: !!stat <0-18> <valor>  (ou !!stat limpar)"); return true; }
                    if (v.EndsWith("f") && float.TryParse(v.TrimEnd('f'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv))
                        me.StatProbe[idx] = BitConverter.ToInt32(BitConverter.GetBytes(fv), 0);
                    else if (int.TryParse(v, out iv)) me.StatProbe[idx] = iv;
                    else { Chat.SendSystemMessage(c, "Valor invalido."); return true; }
                }
                var pk = new RicePacket(760);
                pk.Writer.Write(me.GetStatUpdate());
                c.Send(pk);
                Chat.SendSystemMessage(c, "StatUpdate extra: " + string.Join(" ", me.StatProbe.Select((x, i) => i + "=" + x)));
                return true;
            }
            // teste da Neo City: !!neocap <n> faz o servidor mandar o capitulo n no 3801 (em vez do item escolhido na
            // lista), para descobrir qual numero abre cada capitulo/operacao; !!neocap sem numero desliga
            if (lower == "neocap" || lower.StartsWith("neocap "))
            {
                var np = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int nv;
                if (np.Length >= 2 && int.TryParse(np[1], out nv)) { NeoCity.ChapterOverride[me.CID] = nv; Chat.SendSystemMessage(c, "Neo City: proxima entrada usa o capitulo " + nv + " no 3801."); }
                else { NeoCity.ChapterOverride.Remove(me.CID); Chat.SendSystemMessage(c, "Neo City: capitulo do 3801 = o da lista."); }
                return true;
            }
            // teste da Neo City: !!neoquest <n> faz o servidor responder a quest aceita (3805) com a quest n da lista interna
            // do jogo (em vez da 0 que o jogo manda); !!neoquest sem numero desliga
            if (lower == "neoquest" || lower.StartsWith("neoquest "))
            {
                var np = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int nv;
                if (np.Length >= 2 && int.TryParse(np[1], out nv)) { NeoCity.QuestOverride[me.CID] = nv; Chat.SendSystemMessage(c, "Neo City: ao aceitar no \"!\", o servidor inicia a quest " + nv + "."); }
                else { NeoCity.QuestOverride.Remove(me.CID); Chat.SendSystemMessage(c, "Neo City: quest = a que o jogo pedir."); }
                return true;
            }
            // teste do HP da Neo City (barra "HP 0 / 0"): !!ext <a> <b> manda o 762 ExtendUpdate (dois u32 que o jogo guarda
            // no corredor) para ver se a barra muda
            if (lower.StartsWith("ext "))
            {
                var ep = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                uint ea, eb;
                if (ep.Length < 3 || !uint.TryParse(ep[1], out ea) || !uint.TryParse(ep[2], out eb)) { Chat.SendSystemMessage(c, "Uso: !!ext <numero> <numero>"); return true; }
                var epk = new RicePacket(762);
                epk.Writer.Write(ea); epk.Writer.Write(eb);
                c.Send(epk);
                Chat.SendSystemMessage(c, "ExtendUpdate (762) enviado: " + ea + ", " + eb + ".");
                return true;
            }
            if (lower == "producao" || lower == "fabricacao")
            {
                Chat.SendSystemMessage(c, Alchemy.Status(me));
                Chat.SendSystemMessage(c, "Como usar: compre um Skill Book (!!livro), clique duas vezes nele no inventario, junte os materiais e o kit (!!kit) e fabrique na janela de producao.");
                return true;
            }
            if (lower == "livro" || lower.StartsWith("livro "))
            {
                Chat.SendSystemMessage(c, Alchemy.BuyBook(me, body.Length > 5 ? body.Substring(5) : ""));
                me.FlushModInfo(c);
                return true;
            }
            if (lower == "kit" || lower.StartsWith("kit "))
            {
                Chat.SendSystemMessage(c, Alchemy.BuyKit(me, body.Length > 3 ? body.Substring(3) : ""));
                me.FlushModInfo(c);
                return true;
            }
            if (lower == "licencas" || lower == "licenca")
            {
                Chat.SendSystemMessage(c, "Licencas (mais perto primeiro): " + Licenses.Progress(me));
                return true;
            }
            if (lower == "clima" || lower.StartsWith("clima "))
            {
                Chat.SendSystemMessage(c, Weather.Command(body.Length > 5 ? body.Substring(5) : ""));
                return true;
            }
            if (lower.StartsWith("entrar "))
            {
                string name = body.Substring(7).Trim();
                Crew.Apply(c, Crew.ByName(name), name);
                return true;
            }
            if (lower.StartsWith("membros"))
            {
                var t = Crew.Of((long) me.CID);
                if (t == null) { Chat.SendSystemMessage(c, "Voce nao esta em nenhuma crew."); return true; }
                var names = t.Members.Select(m => { var b = ByCid(m); return b != null ? b.Name + " (" + b.Level + ")" : (m == (long) me.CID ? me.Name : "?"); });
                Chat.SendSystemMessage(c, "Crew " + t.Name + ": " + string.Join(", ", names));
                return true;
            }
            if (lower == "sair crew" || lower == "apagar crew")
            {
                Chat.SendSystemMessage(c, Crew.LeaveOrClose(c));
                return true;
            }
            if (lower == "bots")
            {
                var sample = All().OrderBy(x => Rng.Next()).Take(10).OrderByDescending(x => x.Level);
                Chat.SendSystemMessage(c, "Alguns bots: " + string.Join(", ", sample.Select(x => x.Name + " (" + x.Level + ")")));
                return true;
            }
            // !!upar <bot>  (ou !!<bot> upar): usa um "V Car Upgrade" seu no carro de um bot da sua crew. O item tem que ser
            // o do proximo V do carro dele (V1 -> so o V2 Car Upgrade; V2 -> o V3; ...).
            {
                string lw = lower.TrimStart('!').Trim(), bw = body.TrimStart('!').Trim();
                string target = lw.StartsWith("upar ") ? bw.Substring(5).Trim() : lw.EndsWith(" upar") ? bw.Substring(0, bw.Length - 5).Trim() : null;
                if (target != null)
                {
                    var b = ByName(target);
                    var t = Crew.Of((long) me.CID);
                    if (b == null || b.IsAlt) { Chat.SendSystemMessage(c, "Bot '" + target + "' nao encontrado. Uso: !!upar <nome do bot>"); return true; }
                    var bc = Crew.Of(b.Cid);
                    if (t == null || bc == null || bc.Tid != t.Tid) { Chat.SendSystemMessage(c, b.Name + " nao e da sua crew."); return true; }
                    int grade = Math.Max(1, BotSocial.GradeOf(b)), next = grade + 1;
                    List<Rice.Server.Structures.Resources.VehicleUpgradeEntry> ups;
                    if (next > 9 || !Rice.Server.Structures.Resources.VehicleTable.VehicleUpgrades.TryGetValue(b.Car, out ups) || ups.Count < next)
                    { Chat.SendSystemMessage(c, "O " + CarName(b.Car) + " do " + b.Name + " ja esta no V maximo (V" + grade + ")."); return true; }
                    string prefix = "i_d_mu_v" + next + "_";
                    var item = me.Inventory.FirstOrDefault(i => i.itemEntry != null && i.itemEntry.ID != null && i.itemEntry.ID.StartsWith(prefix) && i.state == 0 && i.stackNum > 0);
                    if (item == null) { Chat.SendSystemMessage(c, "O carro do " + b.Name + " esta no V" + grade + ": precisa de um V" + next + " Car Upgrade no inventario."); return true; }
                    Rice.Game.Item used;
                    if (!me.DropItem((int) item.invIdx, out used, 1, true)) { Chat.SendSystemMessage(c, "Nao consegui usar o item."); return true; }
                    b.CarGrade = next;
                    Save();
                    me.FlushModInfo(c);
                    CityBots.Respawn(b);
                    Chat.SendSystemMessage(c, "Voce usou " + item.itemEntry.Name + " no carro do " + b.Name + ": " + CarName(b.Car) + " V" + grade + " -> V" + next + ".");
                    SayLater(c, 1200, "team", b.Name, "Valeu, " + me.Name + "! O meu " + CarName(b.Car) + " agora e V" + next + ".");
                    Log.WriteLine("Crew: {0} upou o carro de {1} para V{2} ({3}).", me.Name, b.Name, next, item.itemEntry.ID);
                    return true;
                }
            }
            if (lower == "recrutar" || lower.StartsWith("recrutar "))
            {
                var t = Crew.Of((long) me.CID);
                if (t == null || t.Master != (long) me.CID) { Chat.SendSystemMessage(c, "Voce precisa ser lider de uma crew para recrutar."); return true; }
                string who = body.Length > 8 ? body.Substring(8).Trim() : "";
                BotProfile b;
                if (who.Length > 0)
                {
                    b = ByName(who);
                    if (b == null) { Chat.SendSystemMessage(c, "Bot '" + who + "' nao encontrado."); return true; }
                    var old = Crew.Of(b.Cid);
                    if (old != null && old.Tid == t.Tid) { Chat.SendSystemMessage(c, b.Name + " ja esta na sua crew."); return true; }
                    // escada de crews: quem e de crew de bot so vem vencendo no mano a mano
                    if (old != null && old.IsBot && RiceServer.Config.CrewLadder) { SayLater(c, 800, "channel", b.Name, "Sou da " + old.Name + ". Se quiser me levar, me vence no mano a mano."); return true; }
                    // escada de crews: bot de nivel maior que o seu recusa
                    if (RiceServer.Config.CrewLadder && b.Level > me.Level) { SayLater(c, 800, "channel", b.Name, "Valeu o convite, " + me.Name + ", mas eu sou nivel " + b.Level + ". Sobe mais um pouco que eu penso."); return true; }
                    if (old != null) Crew.RemoveMember(old, b.Cid);   // sai da crew antiga e vem para a sua
                }
                else
                    b = RiceServer.Config.CrewLadder
                        ? (Pick(x => Crew.Of(x.Cid) == null && !x.MostWanted && x.Level <= me.Level && me.Level - x.Level <= 20)
                           ?? Pick(x => Crew.Of(x.Cid) == null && !x.MostWanted && x.Level <= me.Level))
                        : (Pick(x => Crew.Of(x.Cid) == null && Math.Abs(x.Level - me.Level) <= 20) ?? Pick(x => Crew.Of(x.Cid) == null));
                if (b == null) { Chat.SendSystemMessage(c, "Nenhum bot sem crew disponivel agora."); return true; }
                string noSlot;
                if (!CrewLadder.CanRecruit(me, out noSlot)) { Chat.SendSystemMessage(c, noSlot); return true; }
                CrewLadder.NoteRecruit(me, b.Cid);
                Crew.AddMember(t, b.Cid);
                b.CrewTid = t.Tid; b.CrewName = t.Name;
                Save();
                Chat.SendSystemMessage(c, b.Name + " (nivel " + b.Level + ") entrou na sua crew!");
                SayLater(c, 1200, "team", b.Name, "Valeu pelo convite, " + me.Name + "!");
                return true;
            }
            return false;
        }

        static readonly HashSet<ulong> greeted = new HashSet<ulong>();

        // Lembrete ao entrar no jogo (uma vez por personagem enquanto o servidor estiver aberto).
        public static void GreetOnJoin(RiceClient c)
        {
            var ch = c != null && c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null) return;
            try { RegisterAlts(c.Player); } catch (Exception ex) { Log.WriteError("Alts: " + ex.Message); }
            try { BotSocial.StarterFriendsGrow(ch); } catch (Exception ex) { Log.WriteError("Amigos iniciais: " + ex.Message); }
            lock (greeted)
            {
                if (greeted.Contains(ch.CID)) return;
                greeted.Add(ch.CID);
            }
            Task.Delay(5000).ContinueWith(_ =>
            {
                try
                {
                    Chat.SendSystemMessage(c, "Bem-vindo ao Offline aprimorado, " + ch.Name + "! Digite !!help no chat para ver todos os comandos.");
                    // corredor novo com o carro de treino: o tutorial do jogo comeca entrando no Battle Zone
                    if (InTutorial(ch))
                    {
                        Chat.SendSystemMessage(c, "[Tutorial] Va com o carro de treino (TEST DRIVER) ate o portao do Battle Zone aqui no Driver Dome, entre e clique em 4 Lap Time: e a corrida de treino guiada.");
                        Chat.SendSystemMessage(c, "[Tutorial] No fim da corrida voce escolhe o seu carro inicial e o carro de treino sai da garagem.");
                    }
                }
                catch (Exception ex) { Log.WriteError("GreetOnJoin: " + ex.Message); }
            });
        }

        public static string CarName(int carType)
        {
            try
            {
                Rice.Server.Structures.Resources.VehicleListEntry v;
                if (Rice.Server.Structures.Resources.VehicleTable.Vehicles.TryGetValue(carType, out v)
                    && v != null && !string.IsNullOrEmpty(v.UiName)) return v.UiName;
            }
            catch (Exception) { }
            return "#" + carType;
        }
    }
}
