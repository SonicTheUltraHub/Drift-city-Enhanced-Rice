using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Licencas (layouts da documentacao do emulador openDC):
    //   811 GetLicenseInfo (u32 versao) -> 812 (u32 versao, u32 n, n x (u16 id, u16 0, u16 0))
    //   813 GetLicenseCond (u32 versao) -> 814 (u32 versao, u16 n, u16 0, u32 total, n x (u16 cond, u32 valor)); o
    //       cliente manda sozinho a cada ~2 min
    //   815 SelectLicense (3 x u16 montadas) -> 816 (o mesmo)
    // Recompensa (soma do "Grade" das licencas que voce tem): todo dia as LicenseRewardHour horas no relogio do PC,
    // paga com 138 MoneyMessage (i32 1, i32 1, i64 valor, i64 total, 2 bytes).
    // Perfil upado: todas as licencas. Os outros: Rookie (o jogo da a todos) + as que ganharem.
    public static partial class Licenses
    {
        class Lic { public int id; public string name; public bool support; public int grade; public string cat; }
        class Rec { public List<int> Held = new List<int>(); public int[] Mounted = new int[3]; public DateTime LastPaid = DateTime.MinValue;
                    public Dictionary<string, long> Stats = new Dictionary<string, long>(); }

        const string FileName = "licenses.json";
        static readonly object Sync = new object();
        static List<Lic> table;
        static Dictionary<string, Rec> store;

        static List<Lic> Table()
        {
            if (table != null) return table;
            try
            {
                string json = null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "License.json");
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("License.json"))
                    using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                table = JsonConvert.DeserializeObject<List<Lic>>(json) ?? new List<Lic>();
            }
            catch (Exception ex) { Log.WriteError("Licencas: " + ex.Message); table = new List<Lic>(); }
            return table;
        }

        static Rec RecOf(Rice.Game.Character ch)
        {
            if (store == null)
            {
                try { store = CharFiles.Exists(FileName) ? JsonConvert.DeserializeObject<Dictionary<string, Rec>>(CharFiles.ReadAllText(FileName)) : null; }
                catch (Exception) { store = null; }
                if (store == null) store = new Dictionary<string, Rec>();
            }
            Rec r;
            string key = ch.CID.ToString();
            if (!store.TryGetValue(key, out r))
            {
                r = new Rec();
                // perfil upado: ja entra com a Mittron Island Guardian montada (a mais alta)
                if (ch.IsUpped && RiceServer.Config.UppedAllLicenses) r.Mounted = new[] { 7011, 0, 0 };
                store[key] = r;
            }
            // conta upada salva antes sem nada montado: monta a Mittron Island Guardian
            if (ch.IsUpped && RiceServer.Config.UppedAllLicenses && r.Mounted != null && r.Mounted.All(m => m == 0)) r.Mounted = new[] { 7011, 0, 0 };
            return r;
        }

        static void Save()
        {
            try { CharFiles.WriteAllText(FileName, JsonConvert.SerializeObject(store, Formatting.Indented)); }
            catch (Exception ex) { Log.WriteError("licenses.json: " + ex.Message); }
        }

        // licencas que o corredor tem
        static List<Lic> Held(Rice.Game.Character ch, Rec r)
        {
            var t = Table().Where(l => l.support).ToList();
            if (ch.IsUpped && RiceServer.Config.UppedAllLicenses) return t;
            var ids = new HashSet<int>(r.Held) { 7000 };          // Rookie: o jogo da a todos
            return t.Where(l => ids.Contains(l.id)).ToList();
        }

        [RicePacket(811, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void GetInfo(RicePacket packet)
        {
            var ch = packet.Sender.Player.ActiveCharacter;
            var buf = packet.Buffer;
            uint version = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0;
            lock (Sync) infoVersion[ch.CID] = version + 1;
            List<Lic> held;
            lock (Sync) held = Held(ch, RecOf(ch));
            var pk = new RicePacket(812);
            pk.Writer.Write(version + 1);
            pk.Writer.Write((uint) held.Count);
            foreach (var l in held) { pk.Writer.Write((ushort) l.id); pk.Writer.Write((ushort) 0); pk.Writer.Write((ushort) 0); }
            packet.Sender.Send(pk);
            // as montadas (ex.: a Mittron Island Guardian do perfil upado)
            int[] mounted;
            lock (Sync) { Save(); mounted = (int[]) RecOf(ch).Mounted.Clone(); }
            mounted = WithRookie(mounted);
            var sel = new RicePacket(816);
            foreach (var m in mounted) sel.Writer.Write((ushort) m);
            packet.Sender.Send(sel);
            SendOwnMounted(packet.Sender, ch, mounted);
            CheckReward(packet.Sender);
        }

        [RicePacket(813, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void GetCond(RicePacket packet)
        {
            var buf = packet.Buffer;
            uint version = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0;
            // total = soma do "grade" das licencas que voce tem (o "Amount of LicenseReward" da janela)
            var ch = packet.Sender.Player.ActiveCharacter;
            FieldTick(ch);                                   // tempo dirigindo na cidade (GC_FieldTime)
            long sum;
            List<KeyValuePair<int, long>> conds;
            lock (Sync) { var r = RecOf(ch); sum = Held(ch, r).Sum(l => (long) l.grade); conds = CondValues(ch, r); }
            var pk = new RicePacket(814);
            pk.Writer.Write(version);
            pk.Writer.Write((ushort) conds.Count);
            pk.Writer.Write((ushort) 0);
            pk.Writer.Write((uint) Math.Min(uint.MaxValue, sum));
            foreach (var kv in conds) { pk.Writer.Write((ushort) kv.Key); pk.Writer.Write((uint) Math.Min(uint.MaxValue, Math.Max(0, kv.Value))); }
            packet.Sender.Send(pk);
            CheckGain(ch);
            CheckReward(packet.Sender);
        }

        [RicePacket(815, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Select(RicePacket packet)
        {
            var ch = packet.Sender.Player.ActiveCharacter;
            var buf = packet.Buffer;
            var want = new int[3];
            for (int i = 0; i < 3 && 2 * i + 2 <= buf.Length; i++) want[i] = BitConverter.ToUInt16(buf, 2 * i);
            lock (Sync)
            {
                var r = RecOf(ch);
                var held = new HashSet<int>(Held(ch, r).Select(l => l.id));
                for (int i = 0; i < 3; i++) r.Mounted[i] = held.Contains(want[i]) ? want[i] : 0;
                Save();
                var shown = WithRookie(r.Mounted);
                var pk = new RicePacket(816);
                foreach (var m in shown) pk.Writer.Write((ushort) m);
                packet.Sender.Send(pk);
                SendOwnMounted(packet.Sender, ch, shown);
            }
        }

        // Na entrada do jogo: 816 com as suas licencas montadas. O seu carro le a 1a licenca de um espaco que so o 816
        // preenche; sem ele ficava 0 ate abrir a janela de licencas (minimapa: "DrawVehicleNameNonTeam idx error -7000").
        public static void SendLogin(RiceClient c, Rice.Game.Character ch)
        {
            if (c == null || ch == null) return;
            int[] mounted;
            lock (Sync) mounted = WithRookie(RecOf(ch).Mounted);
            var sel = new RicePacket(816);
            foreach (var m in mounted) sel.Writer.Write((ushort) m);
            c.Send(sel);
            SendOwnMounted(c, ch, mounted);
        }

        // 1o espaco nunca vazio: o jogo (minimapa, placa) le a 1a licenca; nada montado = Rookie (7000)
        static int[] WithRookie(int[] m)
        {
            var r = new int[3];
            for (int i = 0; i < 3 && m != null && i < m.Length; i++) r[i] = m[i];
            if (r[0] == 0) { int k = Array.FindIndex(r, x => x != 0); if (k > 0) { r[0] = r[k]; r[k] = 0; } else r[0] = 7000; }
            return r;
        }

        // 806 com o seu serial: o registro do seu carro na lista de jogadores tambem recebe as licencas
        static void SendOwnMounted(RiceClient c, Rice.Game.Character ch, int[] m)
        {
            var pk = new RicePacket(806);
            pk.Writer.Write((ushort) ch.Serial);
            foreach (var x in m) pk.Writer.Write((ushort) x);
            c.Send(pk);
        }

        // ---- licencas dos bots -----------------------------------------------------------------------------------
        // Cada bot equipa 3: a de corrida (Rookie ... No.9, pelo nivel) e duas pelo estilo dele. Nivel baixo = as
        // faceis, nivel alto = as mais dificeis. O que equipa muda o que ele faz bem:
        //   corrida (Class E..No.9, Team/Ind. Battle): mais velocidade seguindo/perseguindo
        //   HUV Hunter: mais dano no HUV ; Undercity Guardian: mais dano e HUBs na Undercity
        //   entrega/patrulha/cidades: mais bonus de recompensa para a party
        static readonly int[] Ladder = { 7000, 7001, 7002, 7003, 7004, 7005, 7006, 7007, 7010 };
        static readonly Dictionary<long, int[]> botCache = new Dictionary<long, int[]>();

        static int MaxGrade(int level)
        {
            return level < 10 ? 3000 : level < 20 ? 5000 : level < 30 ? 10000 : level < 45 ? 20000
                 : level < 60 ? 30000 : level < 80 ? 50000 : level < 100 ? 70000 : 100000;
        }

        // ---- licencas que evoluem (bots comuns) ---------------------------------------------------------------
        // Todo bot comeca no Rookie. A cada minuto ganha progresso no que esta montado; ao bater a meta (BotLicenseNeed x
        // grade da proxima / 1000) passa para a proxima: Rookie -> Class E -> D -> ... (limitado pelo nivel, MaxGrade).
        // Nivel 16+: tira o nivel 1 (a mais baixa) de uma licenca que combina com ele (personalidade) e vai upando.
        // Nivel 31+: ganha um terceiro espaco e escolhe qualquer licenca (e as vezes troca). Muito abaixo do nivel dele
        // o progresso vai 3x mais rapido. Marsteh, Brunizin.X e os bots fixos (Joaopvbr) continuam com as delas.
        public class BotLic
        {
            public List<int> Held = new List<int> { 7000 };
            public int[] Mount = { 7000, 0, 0 };
            public Dictionary<string, double> Prog = new Dictionary<string, double>();
            public int Ver;
        }
        const int LicVer = 2;   // mudou a regra: todo mundo volta para o Rookie uma vez

        static readonly Random LicRng = new Random();
        static DateTime nextLicTick = DateTime.UtcNow.AddSeconds(30);

        static bool Evolves(Bots.BotProfile b) { return b != null && !b.IsAlt && !b.Special && !Bots.IsAce(b); }

        static BotLic LicOf(Bots.BotProfile b)
        {
            if (b.Lic == null || b.Lic.Ver < LicVer) b.Lic = new BotLic { Ver = LicVer };
            if (b.Lic.Mount == null || b.Lic.Mount.Length < 3) b.Lic.Mount = new[] { 7000, 0, 0 };
            if (b.Lic.Held == null) b.Lic.Held = new List<int> { 7000 };
            if (b.Lic.Prog == null) b.Lic.Prog = new Dictionary<string, double>();
            return b.Lic;
        }

        static string[] Prefs(Bots.BotProfile b)
        {
            switch (b.Personality)
            {
                case "competitivo": return new[] { "l_team", "l_personal", "l_huv" };
                case "comerciante": return new[] { "l_auction", "l_tuning", "l_quick" };
                case "calado": return new[] { "l_under", "l_patrol", "l_huv" };
                default: return new[] { "l_huv", "l_quick", "l_moon", "l_under", "l_patrol" };
            }
        }

        static readonly HashSet<string> NoPick = new HashSet<string> { "l_ladder", "l_mguardian", "l_example", "l_mita", "l_ghost", "l_thirdanni",
                                                                        "l_200_thousand", "l_service_move", "l_reputation", "l_dcbadge", "l_dcgpwin" };

        // primeira (mais baixa) licenca de uma categoria que o nivel permite
        static Lic FirstOf(string cat, int max)
        {
            return Table().Where(l => l.support && l.cat == cat && l.grade <= max).OrderBy(l => l.grade).ThenBy(l => l.id).FirstOrDefault();
        }

        static Lic NextOf(Lic cur, int max)
        {
            if (cur == null) return null;
            if (cur.cat == "l_ladder")
            {
                int i = Array.IndexOf(Ladder, cur.id);
                if (i < 0 || i + 1 >= Ladder.Length) return null;
                var n = Table().FirstOrDefault(l => l.id == Ladder[i + 1]);
                return n != null && n.grade <= max ? n : null;
            }
            return Table().Where(l => l.support && l.cat == cur.cat && l.grade > cur.grade && l.grade <= max).OrderBy(l => l.grade).ThenBy(l => l.id).FirstOrDefault();
        }

        public static void BotTick()
        {
            if (DateTime.UtcNow < nextLicTick) return;
            nextLicTick = DateTime.UtcNow.AddSeconds(60);
            var cfg = RiceServer.Config;
            if (!cfg.BotLicenses || cfg.BotsFrozen) return;      // bots parados nao pegam licenca sozinhos
            // bots de party de algum jogador nao evoluem sozinhos: so com o que fazem com voce (Activity)
            var inParty = new HashSet<long>();
            foreach (var pl in RiceServer.GetPlayers())
                if (pl != null && pl.ActiveCharacter != null) foreach (var pb in BotSocial.PartyBots(pl.ActiveCharacter)) inParty.Add(pb.Cid);
            bool changed = false;
            foreach (var b in Bots.All().Where(Evolves).ToList())
            {
                if (inParty.Contains(b.Cid)) continue;
                double gain = Math.Max(0, cfg.BotLicenseSpeed) * (0.5 + LicRng.NextDouble()) * Math.Max(0.5, Math.Min(2.0, b.Skill))
                              * (BotSocial.Online(b) ? 1.5 : 1.0);
                Advance(b, gain);
                changed = true;
            }
            if (changed) Bots.Save();
        }

        // bot da sua party ganhou algo junto com voce (missao, entrega, HUV, Rush...): avanca as licencas montadas e
        // devolve o progresso ("Class E 4/6, HUV Hunter Grade C 2/10")
        public static string Activity(Bots.BotProfile b, double points)
        {
            if (b == null || !RiceServer.Config.BotLicenses || !Evolves(b) || BotSocial.GrowthOf(b) < 2) return "";
            return string.Join(", ", Advance(b, Math.Max(0, points)));
        }

        static List<string> Advance(Bots.BotProfile b, double gain)
        {
            var cfg = RiceServer.Config;
            var t = Table().ToDictionary(l => l.id);
            var prog = new List<string>();
            lock (Sync)
            {
                var L = LicOf(b);
                int max = MaxGrade(b.Level);
                // espacos: 1 (so a de corrida) ate o 15, 2 do 16 ao 30, 3 do 31 em diante
                int slots = b.Level > 30 ? 3 : b.Level > 15 ? 2 : 1;
                for (int s = slots; s < 3; s++) if (L.Mount[s] != 0) L.Mount[s] = 0;
                for (int s = 1; s < slots; s++)
                {
                    if (L.Mount[s] != 0) continue;
                    // tira o nivel 1 de uma licenca que ele quer (3o espaco, 31+: qualquer uma, aleatoria)
                    var mounted = new HashSet<string>(L.Mount.Where(id => id != 0 && t.ContainsKey(id)).Select(id => t[id].cat));
                    var cats = s == 1 ? Prefs(b).ToList()
                             : Table().Where(l => l.support && !NoPick.Contains(l.cat)).Select(l => l.cat).Distinct().ToList();
                    var choice = cats.Where(c => !mounted.Contains(c) && FirstOf(c, max) != null).OrderBy(x => LicRng.Next()).FirstOrDefault();
                    if (choice == null) continue;
                    // ja tinha uma dessa categoria: volta com a melhor que tem
                    var have = L.Held.Where(id => t.ContainsKey(id) && t[id].cat == choice).OrderByDescending(id => t[id].grade).FirstOrDefault();
                    var first = have != 0 ? t[have] : FirstOf(choice, max);
                    L.Mount[s] = first.id;
                    if (!L.Held.Contains(first.id)) L.Held.Add(first.id);
                    BotChatter.Tell(b, BotChatter.PickLine("Tirei a licenca " + first.name + "!", "Licenca nova: " + first.name + ". Agora e treinar."));
                }
                // nivel 31+: de vez em quando troca o terceiro espaco por outra (aleatoria)
                if (b.Level > 30 && L.Mount[2] != 0 && LicRng.NextDouble() < cfg.BotLicenseSwapChance) L.Mount[2] = 0;
                // progresso no que esta montado e upgrade quando bate a meta
                for (int s = 0; s < 3; s++)
                {
                    int id = L.Mount[s];
                    Lic cur;
                    if (id == 0 || !t.TryGetValue(id, out cur)) continue;
                    var next = NextOf(cur, max);
                    if (next == null) { prog.Add(cur.name + " (maximo do nivel)"); continue; }
                    double catchUp = max >= next.grade * 2 ? Math.Max(1.0, cfg.BotLicenseCatchUp) : 1.0;
                    double p;
                    L.Prog.TryGetValue(cur.cat, out p);
                    p += gain * catchUp;
                    double need = Math.Max(1, cfg.BotLicenseNeed * next.grade / 1000.0);
                    if (p >= need)
                    {
                        p = 0;
                        L.Mount[s] = next.id;
                        if (!L.Held.Contains(next.id)) L.Held.Add(next.id);
                        BotChatter.Tell(b, BotChatter.PickLine("Bati a meta! Agora tenho a " + next.name + ".", "Subi de licenca: " + next.name + "!",
                                                               "Consegui a " + next.name + "!"));
                        Log.WriteLine("Licencas: {0} (nivel {1}) pegou {2}.", b.Name, b.Level, next.name);
                        var nn = NextOf(next, max);
                        prog.Add(next.name + (nn != null ? " 0/" + (int) Math.Ceiling(Math.Max(1, cfg.BotLicenseNeed * nn.grade / 1000.0)) + " p/ " + nn.name : " (maximo do nivel)"));
                    }
                    else prog.Add(cur.name + " " + (int) Math.Floor(p) + "/" + (int) Math.Ceiling(need) + " p/ " + next.name);
                    L.Prog[cur.cat] = p;
                }
            }
            return prog;
        }

        public static int[] BotMounted(Bots.BotProfile b)
        {
            if (b == null) return new int[3];
            if (RiceServer.Config.BotLicenses && Evolves(b)) lock (Sync) return (int[]) LicOf(b).Mount.Clone();
            lock (Sync)
            {
                int[] got;
                if (botCache.TryGetValue(b.Cid, out got)) return got;
                var t = Table().Where(l => l.support).ToDictionary(l => l.id);
                var rng = new Random((int) (b.Cid % int.MaxValue));
                int max = MaxGrade(b.Level);
                var ladder = Ladder.Where(id => t.ContainsKey(id) && t[id].grade <= max).ToList();
                int lad = ladder.Count == 0 ? 7000 : ladder[Math.Max(0, ladder.Count - 1 - rng.Next(2))];
                string[] pref;
                switch (b.Personality)
                {
                    case "competitivo": pref = new[] { "l_team", "l_personal", "l_huv" }; break;
                    case "comerciante": pref = new[] { "l_auction", "l_tuning", "l_quick" }; break;
                    case "calado": pref = new[] { "l_under", "l_patrol", "l_huv" }; break;
                    default: pref = new[] { "l_huv", "l_quick", "l_moon", "l_under" }; break;
                }
                var picks = new List<int>();
                foreach (var cat in pref.OrderBy(x => rng.Next()))
                {
                    var best = t.Values.Where(l => l.cat == cat && l.grade <= max).OrderByDescending(l => l.grade).FirstOrDefault();
                    if (best != null && !picks.Contains(best.id)) picks.Add(best.id);
                    if (picks.Count == 2) break;
                }
                if (picks.Count < 2)
                    picks.AddRange(t.Values.Where(l => l.grade <= max && !Ladder.Contains(l.id) && !picks.Contains(l.id))
                                         .OrderBy(x => rng.Next()).Take(2 - picks.Count).Select(l => l.id));
                if (b.Name == "Marsteh" || b.Name == "Brunizin.X")
                {
                    // os dois sao cacadores de HUV
                    picks.Remove(7038); picks.Insert(0, 7038);
                    if (picks.Count > 2) picks.RemoveRange(2, picks.Count - 2);
                }
                // metade dos bots tem a especialidade como licenca principal (a primeira), a outra metade a de corrida
                bool racer = b.Name != "Marsteh" && b.Name != "Brunizin.X" && b.Cid % 2 == 0;
                int p0 = picks.Count > 0 ? picks[0] : 0, p1 = picks.Count > 1 ? picks[1] : 0;
                got = racer ? new[] { lad, p0, p1 } : new[] { p0 != 0 ? p0 : lad, lad, p1 };
                botCache[b.Cid] = got;
                return got;
            }
        }

        static IEnumerable<Lic> BotLics(Bots.BotProfile b)
        {
            var t = Table();
            return BotMounted(b).Select(id => t.FirstOrDefault(l => l.id == id)).Where(l => l != null);
        }

        public static string BotLicenseNames(Bots.BotProfile b) { return string.Join(", ", BotLics(b).Select(l => l.name)); }

        // velocidade extra (m/s) pela licenca de corrida
        public static float BotSpeedBonus(Bots.BotProfile b)
        {
            if (b == null) return 0f;
            int lad = BotMounted(b).Select(id => Array.IndexOf(Ladder, id)).Max();
            float s = Math.Max(0, lad) * 1.5f;
            foreach (var l in BotLics(b)) if (l.cat == "l_team" || l.cat == "l_personal") s += l.grade / 10000f;
            return s;
        }

        public static double BotHuvFactor(Bots.BotProfile b)
        {
            double f = 1.0;
            foreach (var l in BotLics(b)) if (l.cat == "l_huv") f += 0.1 + l.grade / 100000.0 * 3;   // Master ~ +1.0
            return f;
        }

        public static double BotUnderFactor(Bots.BotProfile b)
        {
            return BotLics(b).Any(l => l.cat == "l_under") ? 1.5 : 1.0;
        }

        public static double BotRewardBonus(Bots.BotProfile b)
        {
            double f = 0;
            foreach (var l in BotLics(b))
                if (l.cat == "l_quick" || l.cat == "l_patrol" || l.cat == "l_moon" || l.cat == "l_koi" || l.cat == "l_cras" || l.cat == "l_oros")
                    f += 0.02 + l.grade / 1000000.0;
            return f;
        }

        // 666 LicenseStatus (u32 0, u32 serial): ver as licencas de outro corredor (bot)
        //   -> 667 (u32 0, u32 serial, u32 n, nome 42, 3 x u16 montadas, n x (u16 id, u16 0, u16 0))
        //   -> 668 (u32 0, u32 serial, u16 0, u16 0, u32 0)
        [RicePacket(666, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Status(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            uint serial = BitConverter.ToUInt32(buf, 4);
            var b = BotSocial.BySerial(serial);
            if (b == null) { Log.WriteLine("LicenseStatus: serial {0} (nao e bot).", serial); return; }
            var mounted = BotMounted(b);
            var held = new List<int> { 7000 };
            if (RiceServer.Config.BotLicenses && Evolves(b)) lock (Sync) foreach (var id in LicOf(b).Held) if (!held.Contains(id)) held.Add(id);
            foreach (var id in mounted) if (id != 0 && !held.Contains(id)) held.Add(id);
            var pk = new RicePacket(667);
            pk.Writer.Write(0u);
            pk.Writer.Write(serial);
            pk.Writer.Write((uint) held.Count);
            var name = new byte[42];
            var e = Encoding.Unicode.GetBytes(b.Name ?? "");
            Array.Copy(e, name, Math.Min(e.Length, 40));
            pk.Writer.Write(name);
            foreach (var m in mounted) pk.Writer.Write((ushort) m);
            foreach (var id in held) { pk.Writer.Write((ushort) id); pk.Writer.Write((ushort) 0); pk.Writer.Write((ushort) 0); }
            packet.Sender.Send(pk);
            var pc = new RicePacket(668);
            pc.Writer.Write(0u); pc.Writer.Write(serial); pc.Writer.Write((ushort) 0); pc.Writer.Write((ushort) 0); pc.Writer.Write(0u);
            packet.Sender.Send(pc);
        }

        // 806 LicenseInfoRes (u16 serial, 3 x u16): as licencas montadas de um carro que voce ve
        public static void SendBotMounted(RiceClient c, Bots.BotProfile b)
        {
            if (c == null || b == null) return;
            var pk = new RicePacket(806);
            pk.Writer.Write((ushort) BotSocial.SerialOf(b));
            var mm = BotMounted(b).ToList();
            while (mm.Count < 3) mm.Add(0);
            // o 1o espaco nunca vazio (o minimapa usa ele): sem licenca de corrida = Rookie
            if (mm[0] == 0) { int other = mm.FindIndex(x => x != 0); if (other > 0) { mm[0] = mm[other]; mm[other] = 0; } else mm[0] = 7000; }
            foreach (var m in mm.Take(3)) pk.Writer.Write((ushort) m);
            c.Send(pk);
        }

        // Recompensa diaria (relogio do PC).
        public static void CheckReward(RiceClient c)
        {
            var cfg = RiceServer.Config;
            var ch = c != null && c.Player != null ? c.Player.ActiveCharacter : null;
            if (ch == null || cfg.LicenseRewardHour < 0 || cfg.LicenseRewardHour > 23) return;
            long amount;
            lock (Sync)
            {
                var r = RecOf(ch);
                // uma vez por dia, a partir das LicenseRewardHour horas (relogio do PC)
                var now = DateTime.Now;
                var due = now.Date.AddHours(cfg.LicenseRewardHour);
                if (now < due) due = due.AddDays(-1);              // antes das 10h: vale o pagamento de ontem
                if (r.LastPaid >= due) return;
                amount = Held(ch, r).Sum(l => (long) l.grade);
                r.LastPaid = now;
                Save();
            }
            if (amount <= 0) return;
            ch.GrantMito((ulong) amount);
            var pk = new RicePacket(138);
            pk.Writer.Write(1);
            pk.Writer.Write(1);
            pk.Writer.Write(amount);
            pk.Writer.Write(ch.Mito);
            pk.Writer.Write((ushort) 0);
            c.Send(pk);
            Chat.SendSystemMessage(c, string.Format("Recompensa das licencas: +{0:N0} mito. Proxima amanha as {1}h.", amount, cfg.LicenseRewardHour));
            Log.WriteLine("Licencas: {0} recebeu {1} mito.", ch.Name, amount);
        }
    }
}
