using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Entregas (Delivery Service / "Arbeit") e missoes instantaneas (Instant Mission, patrulhas).
    // Layouts da documentacao do emulador openDC (implementado aqui do zero). Tabelas do cliente 2016
    // (World.agt: ArbeitClient.tdf, Instant_Client.tdf, Icon_*_Client.tdf) em res/Jobs.json, Instants.json, Icons.json.
    //
    // Entregas: 321 ArbeitList (u32 indice do posto) -> 322 (posto, n, n x XiStrArbeitInfo 284)
    //   323 Start (TableIdx) -> 324 (serial, TableIdx, lugares) ; 335 Ready -> 336 ; 337 Go -> 337
    //   331 GoalPlace (serial, lugar, tempo) -> 332 (serial, lugar, u16 completo)
    //   325 Reward (serial) -> 326 (serial, exp, mito, itens, item, ExpInfo, u16 nivel, melhor, atual, medalha)
    //   329 GiveUp -> 330 + 787 (posicao do posto) ; 339 RankList -> 340 ; 807 SetMyDeliveryItem -> 808
    // Instantaneas: 990 Request -> 991 (oferta) ; 992 Start -> 993 ; 1001 GoalPlace -> 1002 ;
    //   994 Complete -> 997 Reward, 995 ; 999 GiveUp -> 1000 ; 998 Fail -> 998
    // Bots da party: nas patrulhas e entregas cada bot vai ao posto mais perto que falta e "pega" ele.
    public static class Missions
    {
        const int Places = 5, AverageMs = 120000;
        static readonly int[][] Medals = { new[] { 28, 29, 30 }, new[] { 31, 32, 33 }, new[] { 34, 35, 36 }, new[] { 37, 38, -1 } };
        static readonly int[] MedalResult = { 1, 2, 3, 0 };
        static readonly HashSet<string> PatrolTypes = new HashSet<string> { "move", "party_move", "move_ROO", "party_move_ROO", "move_event" };
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();

        static List<string[]> jobs, instants;
        static Dictionary<string, float[]> icons;     // posto -> [indice, mapa, x, y, z]

        class Job { public uint Serial; public int Idx; public DateTime? Start; public HashSet<int> Done = new HashSet<int>(); public int Medal = -1; public int Ms; }
        class Inst
        {
            public int Idx; public HashSet<int> Places = new HashSet<int>(); public bool Patrol; public string[] Row;
            // patrulha em party (party_move / party_move_ROO): tempo limite, pontos de cada um, pontos de emergencia
            public bool Party, Over; public DateTime Start; public double Limit; public long Exp;
            public Dictionary<long, int> Posts = new Dictionary<long, int>();
            public int Quick; public int QuickPost = -1; public DateTime QuickAt;
        }
        class St { public Job Job; public Inst Inst; public uint JobSerial; public int Offer = -1; public int Day = -1; public int LastPost = -1; }
        static readonly Dictionary<ulong, St> states = new Dictionary<ulong, St>();
        static Dictionary<string, Dictionary<string, int>> records;   // cid -> (TableIdx -> melhor tempo ms)

        static T Load<T>(string name) where T : class
        {
            try
            {
                string json = null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", name);
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex) { Log.WriteError(name + ": " + ex.Message); return null; }
        }

        static List<string[]> Jobs() { return jobs ?? (jobs = Load<List<string[]>>("Jobs.json") ?? new List<string[]>()); }
        static List<string[]> Instants() { return instants ?? (instants = Load<List<string[]>>("Instants.json") ?? new List<string[]>()); }
        static Dictionary<string, float[]> Icons() { return icons ?? (icons = Load<Dictionary<string, float[]>>("Icons.json") ?? new Dictionary<string, float[]>()); }

        static St StateOf(Rice.Game.Character ch)
        {
            St s;
            if (!states.TryGetValue(ch.CID, out s)) { s = new St(); states[ch.CID] = s; }
            return s;
        }

        static float Fv(string[] r, int i) { float v; return i >= 0 && i < r.Length && float.TryParse(r[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f; }
        static int Iv(string[] r, int i) { return (int) Fv(r, i); }

        static List<string> JobPosts(string[] row)
        {
            var l = new List<string>();
            for (int i = 8; i < 28 && i < row.Length; i++) if (row[i] != "0" && row[i] != "" && row[i] != "n/a") l.Add(row[i]);
            return l;
        }

        static int PostMap(string[] row)
        {
            foreach (var p in PatrolPosts(row)) { float[] ic; if (Icons().TryGetValue(p, out ic)) return (int) ic[1]; }
            return -1;
        }

        static string[] RowOf(Inst m) { return m.Row ?? Instants()[m.Idx]; }

        static List<string> PatrolPosts(string[] row)
        {
            var l = new List<string>();
            for (int i = 18; i < row.Length; i++) if (row[i].StartsWith("post_")) l.Add(row[i]);
            return l;
        }

        static byte[] WStr(string s, int bytes)
        {
            var b = new byte[bytes];
            var e = Encoding.Unicode.GetBytes(s ?? "");
            Array.Copy(e, b, Math.Min(e.Length, bytes - 2));
            return b;
        }

        static void WriteExp(PacketWriter w, Rice.Game.Character ch)
        {
            var ei = ch.GetExpInfo();
            w.Write((ulong) ei.CurExp); w.Write((ulong) ei.NextExp); w.Write((ulong) ei.BaseExp);
        }

        static Dictionary<string, int> RecordsOf(Rice.Game.Character ch)
        {
            if (records == null)
            {
                try { records = CharFiles.Exists("arbeit.json") ? JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, int>>>(CharFiles.ReadAllText("arbeit.json")) : null; }
                catch (Exception) { records = null; }
                if (records == null) records = new Dictionary<string, Dictionary<string, int>>();
            }
            Dictionary<string, int> d;
            if (!records.TryGetValue(ch.CID.ToString(), out d)) { d = new Dictionary<string, int>(); records[ch.CID.ToString()] = d; }
            return d;
        }

        static void SaveRecords()
        {
            try { CharFiles.WriteAllText("arbeit.json", JsonConvert.SerializeObject(records)); } catch (Exception) { }
        }

        // ================================ entregas ================================================================
        [RicePacket(321, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobList(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int post = BitConverter.ToInt32(buf, 0);
            var ch = packet.Sender.Player.ActiveCharacter;
            lock (Sync) StateOf(ch).LastPost = post;           // "tentar de novo" volta para este posto
            var list = new List<int>();
            var t = Jobs();
            for (int k = 0; k < t.Count; k++)
            {
                float[] ic;
                if (t[k].Length > 3 && Icons().TryGetValue(t[k][3], out ic) && (int) ic[0] == post) list.Add(k);
            }
            var pk = new RicePacket(322);
            pk.Writer.Write(post);
            pk.Writer.Write(list.Count);
            Dictionary<string, int> rec;
            lock (Sync) rec = RecordsOf(ch);
            foreach (var k in list)
            {
                int best;
                rec.TryGetValue(k.ToString(), out best);
                // Rank[3]: o seu melhor tempo em 1o (o resto vazio)
                if (best > 0) { pk.Writer.Write(WStr(ch.Name, 0x2a)); pk.Writer.Write(WStr("", 0x1a)); pk.Writer.Write((uint) best); pk.Writer.Write(1u); }
                else pk.Writer.Write(new byte[76]);
                pk.Writer.Write(new byte[76 * 2]);
                for (int d = 0; d < 6; d++) { pk.Writer.Write((uint) best); pk.Writer.Write(best > 0 ? 1 : 0); }
                pk.Writer.Write((uint) k);
                pk.Writer.Write((uint) (best > 0 ? best : AverageMs));
            }
            packet.Sender.Send(pk);
            Log.WriteLine("Entregas no posto {0}: {1}.", post, string.Join(", ", list.Select(k => Jobs()[k][4].Trim())));
        }

        [RicePacket(339, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobRankList(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            uint idx = BitConverter.ToUInt32(buf, 0), page = BitConverter.ToUInt32(buf, 4);
            var ch = packet.Sender.Player.ActiveCharacter;
            int best;
            lock (Sync) RecordsOf(ch).TryGetValue(idx.ToString(), out best);
            var pk = new RicePacket(340);
            pk.Writer.Write(idx); pk.Writer.Write(page);
            if (best > 0 && page == 0)
            {
                pk.Writer.Write(1u);
                pk.Writer.Write(WStr(ch.Name, 0x2a)); pk.Writer.Write(WStr("", 0x1a)); pk.Writer.Write((uint) best); pk.Writer.Write(1u);
            }
            else pk.Writer.Write(0u);
            packet.Sender.Send(pk);
        }

        [RicePacket(323, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobStart(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int idx = BitConverter.ToInt32(buf, 0);
            if (idx < 0 || idx >= Jobs().Count) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            uint serial;
            lock (Sync)
            {
                var s = StateOf(ch);
                serial = ++s.JobSerial;
                s.Job = new Job { Serial = serial, Idx = idx };
            }
            var row = Jobs()[idx];
            var pk = new RicePacket(324);
            pk.Writer.Write(serial); pk.Writer.Write((uint) idx); pk.Writer.Write((uint) Math.Min(Places, JobPosts(row).Count));
            packet.Sender.Send(pk);
            Log.WriteLine("Entrega: {0} comecou {1} ({2}).", ch.Name, row[0], row[4].Trim());
        }

        static Job JobOf(Rice.Game.Character ch, uint serial)
        {
            St s;
            return states.TryGetValue(ch.CID, out s) && s.Job != null && s.Job.Serial == serial ? s.Job : null;
        }

        [RicePacket(335, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobReady(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            var pk = new RicePacket(336);
            pk.Writer.Write(buf, 0, 8);
            packet.Sender.Send(pk);
        }

        [RicePacket(337, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobGo(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            uint serial = BitConverter.ToUInt32(buf, 0);
            lock (Sync) { var j = JobOf(packet.Sender.Player.ActiveCharacter, serial); if (j != null) j.Start = DateTime.UtcNow; }
            var pk = new RicePacket(337);
            pk.Writer.Write(serial);
            packet.Sender.Send(pk);
        }

        [RicePacket(331, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobGoalPlace(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            uint serial = BitConverter.ToUInt32(buf, 0); int place = BitConverter.ToInt32(buf, 4);
            JobPlaceDone(packet.Sender, packet.Sender.Player.ActiveCharacter, serial, place, null);
        }

        static void JobPlaceDone(RiceClient c, Rice.Game.Character ch, uint serial, int place, string by)
        {
            bool complete;
            lock (Sync)
            {
                var j = JobOf(ch, serial);
                if (j == null) return;
                var row = Jobs()[j.Idx];
                int need = Math.Min(Places, JobPosts(row).Count);
                if (place < 0 || place >= need || j.Done.Contains(place)) return;
                j.Done.Add(place);
                complete = j.Done.Count >= need;
                if (complete)
                {
                    double took = (DateTime.UtcNow - (j.Start ?? DateTime.UtcNow)).TotalSeconds;
                    j.Ms = (int) (took * 1000);
                    j.Medal = 3;
                    for (int m = 0; m < 3; m++) if (took <= Fv(row, Medals[m][2])) { j.Medal = m; break; }
                }
            }
            var pk = new RicePacket(332);
            pk.Writer.Write(serial); pk.Writer.Write((uint) place); pk.Writer.Write((ushort) (complete ? 1 : 0));
            c.Send(pk);
            if (by != null) Chat.SendSystemMessage(c, by + " entregou no ponto " + (place + 1) + ".");
        }

        [RicePacket(325, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobReward(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            uint serial = BitConverter.ToUInt32(buf, 0);
            var c = packet.Sender;
            var ch = c.Player.ActiveCharacter;
            Job j;
            int best;
            lock (Sync)
            {
                j = JobOf(ch, serial);
                if (j == null || j.Medal < 0) return;
                StateOf(ch).Job = null;
                var rec = RecordsOf(ch);
                rec.TryGetValue(j.Idx.ToString(), out best);
                if (j.Ms > 0 && (best == 0 || j.Ms < best)) { rec[j.Idx.ToString()] = j.Ms; best = j.Ms; SaveRecords(); }
            }
            var row = Jobs()[j.Idx];
            double mult = RiceServer.Config.DeliveryRewardMultiplier * BotSocial.RewardBonus(ch);
            int exp, mito;
            string partText = null;
            if (RiceServer.Config.DeliveryTierRewards) DeliveryReward(ch, row, j.Medal, mult, out exp, out mito, out partText);
            else { exp = (int) Math.Round(Fv(row, Medals[j.Medal][0]) * mult); mito = (int) Math.Round(Fv(row, Medals[j.Medal][1]) * mult); }
            if (exp > 0) ch.GrantExperience(exp);
            if (mito > 0) ch.GrantMito((ulong) mito);
            BotSocial.ShareRewards(ch, exp, mito);
            var pk = new RicePacket(326);
            pk.Writer.Write(serial); pk.Writer.Write((uint) exp); pk.Writer.Write((uint) mito);
            pk.Writer.Write(0u); pk.Writer.Write(0u);
            WriteExp(pk.Writer, ch);
            pk.Writer.Write((ushort) ch.Level);
            pk.Writer.Write((uint) best); pk.Writer.Write(best > 0 ? 1 : 0);
            pk.Writer.Write((uint) j.Ms); pk.Writer.Write(1);
            pk.Writer.Write((uint) MedalResult[j.Medal]);
            c.Send(pk);
            ch.FlushModInfo(c);
            if (partText != null) Chat.SendSystemMessage(c, partText);
            Log.WriteLine("Entrega {0} concluida em {1:0.0} s: medalha {2}, +{3} exp +{4} mito.", row[0], j.Ms / 1000.0, MedalResult[j.Medal], exp, mito);
            try { Licenses.CountDelivery(ch, row[0], MedalResult[j.Medal]); } catch (Exception lex) { Log.WriteError("Licencas (entrega): " + lex.Message); }
            PartyTalk.Praise(ch, "delivery");
        }

        // ---- recompensa das entregas por dificuldade ------------------------------------------------------------
        // facil 50 exp + 1.000 mito + peca N fraca; normal 75 + 2.500 + N ou S do seu nivel; dificil 85 + 3.000 + N, S ou H;
        // especial 100 + 5.000 + N, S ou H (ouro: 50% de vir upada; prata: 20%); evento (coelho/colheita) 80 + 5.500 +
        // caixa misteriosa (peca N/S/H que pode vir upada). Em cada posto: as comuns facil/normal/dificil, EX especial,
        // Pascoa e colheita evento. Ouro = 100%; prata = 2/3 e a peca um grau abaixo;
        // bronze = 1/3 e a peca do comeco da linha; sem medalha = 1/3 sem peca. Cada cidade seguinte +2% (DeliveryCityBonus).
        public static string DeliveryTier(string[] row)
        {
            var cfg = RiceServer.Config;
            string id = row[0], name = (row.Length > 4 ? row[4] : "").Trim(), post = row.Length > 3 ? row[3] : "";
            string ov;
            if (cfg.DeliveryTierOverride != null && cfg.DeliveryTierOverride.TryGetValue(id, out ov)) return ov;
            if (id.StartsWith("e_v_") || id.StartsWith("g_event")) return "evento";
            if (name.EndsWith(" EX")) return "especial";
            // Oros: so dificil e especial (as Time Attack da Olivia sao especiais, as outras dificeis)
            if (post.StartsWith("post_4rd")) return name.StartsWith("Olivia") ? "especial" : "dificil";
            // em cada posto: as entregas comuns (nao EX, nao evento) na ordem da tabela = facil, normal, dificil
            // (posto com mais de 3: divididas em tres partes)
            var same = Jobs().Where(r => r.Length > 4 && r[3] == post && !r[0].StartsWith("e_v_") && !r[0].StartsWith("g_event")
                                         && !r[4].Trim().EndsWith(" EX")).Select(r => r[0]).OrderBy(x => x).ToList();
            int k = same.IndexOf(id), n = Math.Max(1, same.Count);
            int part = k < 0 ? 0 : Math.Min(2, k * 3 / n);
            // Cras: so o 1o posto tem facil; nos outros as comuns vao de normal para cima (normal, normal, dificil)
            if (post.StartsWith("post_3rd"))
            {
                var cras = Jobs().Where(r => r.Length > 4 && r[3].StartsWith("post_3rd")).Select(r => r[3]).Distinct().OrderBy(x => x).ToList();
                if (cras.IndexOf(post) > 0) part = k < 0 ? 1 : 1 + Math.Min(1, k * 2 / n);
            }
            return part == 0 ? "facil" : part == 1 ? "normal" : "dificil";
        }

        static int CityIndex(string post)
        {
            if (post == null) return 0;
            if (post.StartsWith("post_2nd")) return 1;
            if (post.StartsWith("post_3rd")) return 2;
            if (post.StartsWith("post_4rd")) return 3;
            return 0;
        }

        static void DeliveryReward(Rice.Game.Character ch, string[] row, int medal, double mult, out int exp, out int mito, out string partText)
        {
            var cfg = RiceServer.Config;
            string tier = DeliveryTier(row);
            int bexp, bmito;
            switch (tier)
            {
                case "normal": bexp = 75; bmito = 2500; break;
                case "dificil": bexp = 85; bmito = 3000; break;
                case "especial": bexp = 100; bmito = 5000; break;
                case "evento": bexp = 80; bmito = 5500; break;
                default: bexp = 50; bmito = 1000; break;
            }
            double share = medal == 0 ? 1.0 : medal == 1 ? 2.0 / 3.0 : 1.0 / 3.0;
            double city = 1.0 + Math.Max(0, cfg.DeliveryCityBonus) * CityIndex(row.Length > 3 ? row[3] : "");
            exp = (int) Math.Round(bexp * share * city * mult);
            mito = (int) Math.Round(bmito * share * city * mult);
            partText = null;
            if (medal > 2 || !cfg.DeliveryPartRewards) return;           // sem medalha: sem peca
            string[] tiers;
            switch (tier)
            {
                case "facil": tiers = new[] { "N" }; break;
                case "normal": tiers = new[] { "N", "S" }; break;
                default: tiers = new[] { "N", "S", "H" }; break;
            }
            int maxLv = tier == "facil" ? Math.Max(1, ch.Level / 2) : ch.Level;
            int mode = tier == "facil" ? 0 : 1;
            if (medal == 1)
            {
                // prata: a peca cai um grau (H -> S, S -> N; N fica N, mas mais fraca)
                if (tiers.Length == 1) maxLv = Math.Max(1, maxLv / 2);
                else tiers = tiers.Take(tiers.Length - 1).ToArray();
            }
            else if (medal == 2) mode = 2;                               // bronze: a peca do comeco da linha
            double up = 0;
            if (tier == "especial" || tier == "evento") up = medal == 0 ? cfg.DeliveryUppedGold : medal == 1 ? cfg.DeliveryUppedSilver : 0;
            string t;
            var it = Rice.Game.PartCrafting.RandomPart(ch, tiers, maxLv, mode, up, out t);
            if (it != null) partText = (tier == "evento" ? "Caixa misteriosa: " : "Entrega: ") + t;
        }

        [RicePacket(329, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void JobGiveUp(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            uint serial = BitConverter.ToUInt32(buf, 0);
            var ch = packet.Sender.Player.ActiveCharacter;
            Job j;
            lock (Sync) { j = JobOf(ch, serial); if (j != null) StateOf(ch).Job = null; }
            var pk = new RicePacket(330);
            pk.Writer.Write(serial);
            packet.Sender.Send(pk);
            Log.WriteLine("Entrega: {0} desistiu{1}.", ch.Name, j != null ? " de " + Jobs()[j.Idx][4].Trim() : "");
            if (j == null) return;
            float[] ic;
            if (Icons().TryGetValue(Jobs()[j.Idx][3], out ic))
            {
                // 787 leva o carro de volta ao posto da entrega. Mas o jogo tambem desiste da entrega quando voce volta para o
                // lobby (Driver Dome): ai o carro ia para a posicao da cidade dentro do lobby e ficava no vazio. Espera a troca de
                // area e so manda se voce continua na cidade.
                var c = packet.Sender;
                var p2 = new RicePacket(787);
                p2.Writer.Write((uint) ic[1]); p2.Writer.Write(ic[2]); p2.Writer.Write(ic[3]); p2.Writer.Write(ic[4]); p2.Writer.Write(0f);
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    try
                    {
                        var now = c.Player != null ? c.Player.ActiveCharacter : null;
                        if (now == null || now != ch || now.Area == null || now.Area.ID == 0)
                        {
                            Log.WriteLine("Entrega: {0} voltou para o lobby; o carro nao vai para o posto.", ch.Name);
                            return;
                        }
                        c.Send(p2);
                    }
                    catch (Exception) { }
                });
            }
        }

        // 786 QuestPosition: depois de uma entrega (ou missao que falhou) o jogo pergunta "tentar de novo?"; no Sim manda
        // 786 e espera 787 com a posicao do posto (o mesmo que a desistencia usa) para levar o carro de volta.
        [RicePacket(786, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestPosition(RicePacket packet)
        {
            var buf = packet.Buffer;
            var ch = packet.Sender.Player.ActiveCharacter;
            int post;
            lock (Sync) post = StateOf(ch).LastPost;
            float[] ic = null;
            if (post >= 0)
            {
                foreach (var row in Jobs())
                {
                    float[] x;
                    if (row.Length > 3 && Icons().TryGetValue(row[3], out x) && (int) x[0] == post) { ic = x; break; }
                }
                if (ic == null) ic = Icons().Values.FirstOrDefault(x => x.Length >= 5 && (int) x[0] == post);
            }
            Log.WriteLine("QuestPosition (tentar de novo): posto {0}{1}. Pacote: {2}", post, ic == null ? " (sem posicao)" : "",
                          BitConverter.ToString(buf, 0, Math.Min(buf.Length, 32)));
            if (ic == null || ic.Length < 5) return;
            var p2 = new RicePacket(787);
            p2.Writer.Write((uint) ic[1]); p2.Writer.Write(ic[2]); p2.Writer.Write(ic[3]); p2.Writer.Write(ic[4]); p2.Writer.Write(0f);
            packet.Sender.Send(p2);
        }

        [RicePacket(807, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void SetDeliveryItem(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 1) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            var pk = new RicePacket(808);
            pk.Writer.Write(ch.Serial); pk.Writer.Write((ushort) Rng.Next(2, 30000)); pk.Writer.Write(buf[0]);
            packet.Sender.Send(pk);
        }

        // ================================ instantaneas ============================================================
        static bool Eligible(string[] r, int level)
        {
            if (r == null || r.Length < 18 || r[0] != "instant" || (r[17] != "n/a" && r[17] != "")) return false;
            if (r[3] + r[4] != "00") return false;
            int max = Iv(r, 2); return Iv(r, 1) <= level && level <= (max == 0 ? 1000 : max);
        }

        [RicePacket(990, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InstantRequest(RicePacket packet)
        {
            var buf = packet.Buffer;
            uint post = buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0, flag = buf.Length >= 8 ? BitConverter.ToUInt32(buf, 4) : 0;
            var ch = packet.Sender.Player.ActiveCharacter;
            var rows = Instants();
            Log.WriteLine("Missao instantanea: pedido no posto {0} (flag {1}).", post, flag);
            lock (Sync) StateOf(ch).LastPost = (int) post;
            // posto de patrulha: a patrulha que sai desse posto (coluna 17), na sua faixa de nivel
            var patrols = Enumerable.Range(0, rows.Count).Where(i =>
            {
                var r = rows[i];
                float[] ic;
                if (r.Length < 19 || r[0] != "patrol" || !Icons().TryGetValue(r[17], out ic) || (uint) ic[0] != post) return false;
                // patrulha em party: o jogo nao mostra os pontos neste servidor; oferece a patrulha normal (os bots da party ajudam igual)
                if (!RiceServer.Config.PartyPatrolOffer && r[10].StartsWith("party_")) return false;
                int max = Iv(r, 2); return Iv(r, 1) <= ch.Level && ch.Level <= (max == 0 ? 1000 : max);
            }).ToList();
            var el = patrols.Count > 0 ? patrols
                   : Enumerable.Range(0, rows.Count).Where(i => Eligible(rows[i], ch.Level) && !rows[i][10].StartsWith("Mission_Battle")).ToList();
            if (el.Count == 0) { Chat.SendSystemMessage(packet.Sender, "Nenhuma missao para o seu nivel aqui."); return; }
            int band = el.Max(i => Iv(rows[i], 1));
            el = el.Where(i => Iv(rows[i], 1) == band).ToList();
            int roll = Rng.Next(100); double total = 0; int pick = el[el.Count - 1];
            foreach (var i in el) { total += Fv(rows[i], 8); if (roll < total) { pick = i; break; } }
            lock (Sync) StateOf(ch).Offer = pick;
            Log.WriteLine("Missao instantanea oferecida: {0} ({1}: {2}).", pick, rows[pick][10], rows[pick][5]);
            var pk = new RicePacket(991);
            pk.Writer.Write((uint) pick); pk.Writer.Write(flag); pk.Writer.Write(post);
            packet.Sender.Send(pk);
        }

        [RicePacket(992, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InstantStart(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int idx = BitConverter.ToInt32(buf, 0);
            var rows = Instants();
            var ch = packet.Sender.Player.ActiveCharacter;
            if (idx < 0 || idx >= rows.Count) { Log.WriteLine("Missao instantanea {0}: nao existe.", idx); return; }
            var row = rows[idx];
            // O cliente 2016 manda um numero que nem sempre e a linha da nossa tabela (ex.: 123, uma patrulha de Oros, estando
            // em Moon Palace): os pontos ficavam em outra cidade e os bots saiam do mapa. Patrulha com pontos fora da sua
            // cidade -> a patrulha do mesmo tipo (party ou solo) da sua cidade, na sua faixa de nivel.
            if (row[0] == "patrol")
            {
                int map = PostMap(row), mine = ch.City >= 1 ? ch.City - 1 : 0;
                if (map >= 0 && map != mine)
                {
                    bool party = row[10].StartsWith("party_");
                    int best = -1, bestLv = -1;
                    for (int k = 0; k < rows.Count; k++)
                    {
                        var r = rows[k];
                        if (r[0] != "patrol" || r.Length < 19 || r[10].StartsWith("party_") != party || PostMap(r) != mine) continue;
                        int lo = Iv(r, 1), hi = Iv(r, 2); if (hi == 0) hi = 1000;
                        if (lo <= ch.Level && ch.Level <= hi && lo > bestLv) { best = k; bestLv = lo; }
                    }
                    if (best >= 0)
                    {
                        Log.WriteLine("Missao instantanea {0}: pontos em outra cidade; usando a linha {1} ({2}, {3} pontos) da sua cidade.", idx, best, rows[best][10], rows[best][16]);
                        row = rows[best];
                    }
                }
            }
            lock (Sync)
            {
                var st = StateOf(ch);
                if (row[0] == "day_mission") st.Day = idx;     // missao do dia: fica a parte da instantanea
                else st.Inst = new Inst { Idx = idx, Row = row, Patrol = row.Length > 10 && PatrolTypes.Contains(row[10]) };
                st.Offer = -1;
                if (st.Inst != null && row.Length > 10 && row[10].StartsWith("party_move"))
                {
                    st.Inst.Party = true; st.Inst.Start = DateTime.UtcNow;
                    st.Inst.Limit = Math.Max(30, RiceServer.Config.PartyPatrolSeconds) + Countdown;
                }
            }
            var pk = new RicePacket(993);
            pk.Writer.Write((uint) idx); pk.Writer.Write(0u); pk.Writer.Write((byte) 0);
            packet.Sender.Send(pk);
            Inst pi; lock (Sync) pi = StateOf(ch).Inst;
            if (pi != null && pi.Party) StartPartyClock(packet.Sender, ch, pi);
            Log.WriteLine("Missao instantanea {0} ({1}): {2}. Pacote: {3}", idx, row.Length > 10 ? row[10] : "?", row.Length > 5 ? row[5] : "",
                          BitConverter.ToString(buf));
        }

        static int RewardExp(string[] row, int level)
        {
            string text = row.Length > 14 ? row[14] : "";
            int n; if (int.TryParse(text, out n)) return n;
            float f; if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) return (int) f;
            // formula: INT((({Level}^1.7)/5+10))
            var m = Regex.Match(text.Replace(" ", ""), @"\{Level\}\^([0-9.]+)\)/([0-9.]+)\+([0-9.]+)");
            if (m.Success)
            {
                double pw = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                double dv = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                double ad = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                return (int) (Math.Pow(level, pw) / dv + ad);
            }
            return 0;
        }

        [RicePacket(1001, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InstantGoalPlace(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            int idx = BitConverter.ToInt32(buf, 0), place = BitConverter.ToInt32(buf, 4);
            Log.WriteLine("Missao instantanea {0}: ponto {1}.", idx, place);
            InstantPlaceDone(packet.Sender, packet.Sender.Player.ActiveCharacter, idx, place, null);
        }

        static void InstantPlaceDone(RiceClient c, Rice.Game.Character ch, int idx, int place, string by)
        {
            Inst m; string[] row; bool last;
            lock (Sync)
            {
                St s;
                if (!states.TryGetValue(ch.CID, out s) || s.Inst == null || s.Inst.Idx != idx) return;
                m = s.Inst; row = RowOf(m);
                int posts = Math.Max(Iv(row, 16), 1);
                if (place < 0 || place >= posts || m.Places.Contains(place)) return;
                m.Places.Add(place);
                last = m.Patrol && m.Places.Count >= Iv(row, 16);
                if (last) { s.Inst = null; m.Over = true; }
                if (m.Party)
                {
                    var who = by != null ? Bots.ByName(by) : null;
                    long cid = who != null ? who.Cid : (long) ch.CID;
                    int n; m.Posts.TryGetValue(cid, out n); m.Posts[cid] = n + 1;
                    if (m.Places.Count % 5 == 0) m.Limit += 4;            // a cada 5 pontos: +4 s
                }
            }
            if (m.Party) SendPartyTime(c, ch, m, by);
            int exp = RewardExp(row, ch.Level);
            if (exp > 0) ch.GrantExperience(exp);
            var pk = new RicePacket(1002);
            pk.Writer.Write((uint) idx); pk.Writer.Write((uint) place); pk.Writer.Write((uint) exp);
            WriteExp(pk.Writer, ch); pk.Writer.Write((ushort) ch.Level); pk.Writer.Write(0u);
            c.Send(pk);
            if (by != null) Chat.SendSystemMessage(c, by + " pegou um ponto da patrulha.");
            if (!last) return;
            int final = (int) Math.Round(Iv(row, 15) * BotSocial.RewardBonus(ch));
            if (final > 0) ch.GrantExperience(final);
            BotSocial.ShareRewards(ch, exp + final, 0);
            SendReward(c, ch, idx, final, 0);
            if (m.Party) SendPartyResult(c, ch, m, exp + final);
            Log.WriteLine("Patrulha {0} concluida: +{1} exp.", idx, final);
            try { Licenses.CountPatrol(ch, m.Party); } catch (Exception lex) { Log.WriteError("Licencas (patrulha): " + lex.Message); }
            PartyTalk.Praise(ch, "patrulha");
        }

        static void SendReward(RiceClient c, Rice.Game.Character ch, int idx, int exp, int mito)
        {
            var stat = new RicePacket(760);
            stat.Writer.Write(ch.GetStatUpdate());
            c.Send(stat);
            var pk = new RicePacket(997);
            pk.Writer.Write((uint) idx); pk.Writer.Write((uint) exp); pk.Writer.Write((uint) mito);
            WriteExp(pk.Writer, ch); pk.Writer.Write((ushort) ch.Level); pk.Writer.Write((ushort) 0);
            pk.Writer.Write(0u); pk.Writer.Write(0u); pk.Writer.Write(0u);
            c.Send(pk);
            var end = new RicePacket(995);
            end.Writer.Write((uint) idx);
            c.Send(end);
        }

        [RicePacket(994, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InstantComplete(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int idx = BitConverter.ToInt32(buf, 0);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Inst m;
            lock (Sync)
            {
                St s;
                if (!states.TryGetValue(ch.CID, out s)) return;
                if (s.Inst != null && s.Inst.Idx == idx) { m = s.Inst; s.Inst = null; }
                else if (s.Day == idx) { m = new Inst { Idx = idx }; s.Day = -1; }
                else return;
            }
            var row = RowOf(m);
            double mult = BotSocial.RewardBonus(ch);
            int exp = (int) Math.Round(RewardExp(row, ch.Level) * mult), mito = (int) Math.Round(Iv(row, 13) * mult);
            if (m.Patrol && m.Places.Count >= Iv(row, 16)) exp += Iv(row, 15);
            if (exp > 0) ch.GrantExperience(exp);
            if (mito > 0) ch.GrantMito((ulong) mito);
            BotSocial.ShareRewards(ch, exp, mito);
            SendReward(c, ch, idx, exp, mito);
            Log.WriteLine("Missao instantanea {0} concluida: +{1} exp +{2} mito.", idx, exp, mito);
            if (m.Patrol && m.Places.Count >= Iv(row, 16)) { try { Licenses.CountPatrol(ch, m.Party); } catch (Exception lex) { Log.WriteError("Licencas (patrulha): " + lex.Message); } }
            PartyTalk.Praise(ch, "missao");
        }

        [RicePacket(999, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InstantGiveUp(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int idx = BitConverter.ToInt32(buf, 0);
            lock (Sync) { St s; if (states.TryGetValue(packet.Sender.Player.ActiveCharacter.CID, out s)) s.Inst = null; }
            var pk = new RicePacket(1000);
            pk.Writer.Write((uint) idx);
            packet.Sender.Send(pk);
        }

        [RicePacket(998, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void InstantFail(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int idx = BitConverter.ToInt32(buf, 0);
            Log.WriteLine("Missao instantanea {0}: o cliente falhou a missao (tempo/condicao).", idx);
            lock (Sync) { St s; if (states.TryGetValue(packet.Sender.Player.ActiveCharacter.CID, out s)) s.Inst = null; }
            var pk = new RicePacket(998);
            pk.Writer.Write((uint) idx);
            packet.Sender.Send(pk);
        }

        [RicePacket(1003, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void MissionBattleResult(RicePacket packet) { }

        // ================================ patrulha em party ======================================================
        // 3111 (0xc27) PartyPatrolTime (i64 cid, u32 pontos dele, u32 tempo limite em s desde o inicio): o HUD do tempo e a
        //   contagem de cada membro. O 1o vai com no maximo 60 s + contagem (o HUD so roda com 60 s ou menos), o
        //   limite inteiro vai 9,5 s depois. 3112 (0xc28) pedido de ponto de emergencia (u32 posto) -> 3108 (0xc24, 4 x i32);
        //   3109 (0xc25) pegou (u32 slot, u32 posto) -> 3110 (0xc26: slot, posto, exp, ExpInfo, u16 nivel, u32 limite).
        //   Fim: 997 + 995 + 3102 (0xc1e resultado). Tempo acabou: 998 InstantFail.
        const double Countdown = 8;

        static void SendTime(RiceClient c, long cid, int posts, double limit)
        {
            var pk = new RicePacket(3111);
            pk.Writer.Write(cid); pk.Writer.Write((uint) posts); pk.Writer.Write((uint) limit);
            c.Send(pk);
        }

        static void SendPartyTime(RiceClient c, Rice.Game.Character ch, Inst m, string by)
        {
            var who = by != null ? Bots.ByName(by) : null;
            long cid = who != null ? who.Cid : (long) ch.CID;
            int n; lock (Sync) m.Posts.TryGetValue(cid, out n);
            SendTime(c, cid, n, m.Limit);
        }

        static void StartPartyClock(RiceClient c, Rice.Game.Character ch, Inst m)
        {
            SendTime(c, (long) ch.CID, 0, Math.Min(m.Limit, 60 + Countdown));
            Log.WriteLine("Patrulha em party {0}: {1:0} s de limite.", m.Idx, m.Limit - Countdown);
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(9500);
                    if (!m.Over) SendTime(c, (long) ch.CID, 0, m.Limit);
                    while (!m.Over)
                    {
                        await System.Threading.Tasks.Task.Delay(500);
                        if ((DateTime.UtcNow - m.Start).TotalSeconds > m.Limit + 2)
                        {
                            lock (Sync) { St s; if (states.TryGetValue(ch.CID, out s) && s.Inst == m) s.Inst = null; m.Over = true; }
                            var f = new RicePacket(998);
                            f.Writer.Write((uint) m.Idx);
                            c.Send(f);
                            Log.WriteLine("Patrulha em party {0}: o tempo acabou ({1} pontos).", m.Idx, m.Places.Count);
                        }
                    }
                }
                catch (Exception ex) { Log.WriteError("Patrulha em party: " + ex.Message); }
            });
        }

        static void SendPartyResult(RiceClient c, Rice.Game.Character ch, Inst m, long myExp)
        {
            var units = new List<byte[]>();
            long total = 0;
            lock (Sync)
                foreach (var kv in m.Posts)
                {
                    var b = (long) ch.CID == kv.Key ? null : Bots.ByCid(kv.Key);
                    string name = b != null ? b.Name : ch.Name;
                    long exp = b != null ? 0 : myExp;
                    total += exp;
                    using (var ms = new MemoryStream())
                    using (var w = new BinaryWriter(ms))
                    {
                        w.Write(WStr(name, 0x2a)); w.Write((uint) kv.Value); w.Write((uint) (b == null ? m.Quick : 0));
                        w.Write((ushort) 0); w.Write(0u); w.Write((uint) exp);
                        units.Add(ms.ToArray());
                    }
                }
            var pk = new RicePacket(3102);
            pk.Writer.Write((uint) total); pk.Writer.Write((uint) units.Count);
            foreach (var u in units) pk.Writer.Write(u);
            c.Send(pk);
        }

        [RicePacket(3112, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuickPostRequest(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int post = BitConverter.ToInt32(buf, 0);
            var ch = packet.Sender.Player.ActiveCharacter;
            Inst m;
            lock (Sync) { St s; if (!states.TryGetValue(ch.CID, out s) || s.Inst == null || !s.Inst.Party || s.Inst.Over) return; m = s.Inst; m.QuickPost = post; m.QuickAt = DateTime.UtcNow; }
            var pk = new RicePacket(3108);
            pk.Writer.Write(post); pk.Writer.Write(-1); pk.Writer.Write(-1); pk.Writer.Write(-1);
            packet.Sender.Send(pk);
        }

        [RicePacket(3109, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuickPostTaken(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            uint slot = BitConverter.ToUInt32(buf, 0);
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            Inst m; int post; double waited;
            lock (Sync)
            {
                St s;
                if (!states.TryGetValue(ch.CID, out s) || s.Inst == null || !s.Inst.Party || s.Inst.QuickPost < 0) return;
                m = s.Inst; post = m.QuickPost; m.QuickPost = -1; m.Quick++;
                waited = (DateTime.UtcNow - m.QuickAt).TotalSeconds;
                m.Limit += Math.Max(20 - (int) (waited / 3), 5);
            }
            int exp = RewardExp(RowOf(m), ch.Level) * 10;
            if (exp > 0) ch.GrantExperience(exp);
            var pk = new RicePacket(3110);
            pk.Writer.Write(slot); pk.Writer.Write((uint) post); pk.Writer.Write((uint) exp);
            WriteExp(pk.Writer, ch); pk.Writer.Write((ushort) ch.Level); pk.Writer.Write((uint) m.Limit);
            c.Send(pk);
        }

        // ================================ bots da party ===========================================================
        // Pontos que faltam da patrulha / entrega em andamento: (lugar, x, y). Vazio = nenhuma.
        // voce esta numa missao instantanea/patrulha ou entrega agora?
        public static bool Active(Rice.Game.Character ch)
        {
            if (ch == null) return false;
            lock (Sync) { St s; return states.TryGetValue(ch.CID, out s) && (s.Inst != null || s.Job != null); }
        }

        public static List<float[]> OpenPoints(Rice.Game.Character ch)
        {
            var l = new List<float[]>();
            if (ch == null || !RiceServer.Config.PartyBotsHelpMissions) return l;
            lock (Sync)
            {
                St s;
                if (!states.TryGetValue(ch.CID, out s)) return l;
                List<string> posts = null; HashSet<int> done = null;
                if (s.Inst != null && s.Inst.Patrol) { var ir = RowOf(s.Inst); posts = PatrolPosts(ir); done = s.Inst.Places; posts = posts.Take(Math.Max(Iv(ir, 16), 1)).ToList(); }
                else if (s.Job != null && s.Job.Start != null && RiceServer.Config.PartyBotsHelpDeliveries) { posts = JobPosts(Jobs()[s.Job.Idx]).Take(Places).ToList(); done = s.Job.Done; }
                if (posts == null) return l;
                for (int i = 0; i < posts.Count; i++)
                {
                    float[] ic;
                    if (done.Contains(i) || !Icons().TryGetValue(posts[i], out ic)) continue;
                    l.Add(new[] { i, ic[2], ic[3], ic[4] });
                }
            }
            return l;
        }

        // um bot chegou num ponto
        public static void BotReached(Rice.Game.Player p, string botName, int place)
        {
            var ch = p != null ? p.ActiveCharacter : null;
            var c = p != null ? p.GameClient : null;
            if (ch == null || c == null) return;
            int instIdx = -1; uint jobSerial = 0;
            lock (Sync)
            {
                St s;
                if (!states.TryGetValue(ch.CID, out s)) return;
                if (s.Inst != null && s.Inst.Patrol) instIdx = s.Inst.Idx;
                else if (s.Job != null) jobSerial = s.Job.Serial;
            }
            if (instIdx >= 0) InstantPlaceDone(c, ch, instIdx, place, botName);
            else if (jobSerial != 0) JobPlaceDone(c, ch, jobSerial, place, botName);
        }
    }
}
