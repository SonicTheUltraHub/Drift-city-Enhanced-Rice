using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Progresso das licencas do jogador. As condicoes de cada licenca (GC_BattlePersonal:10, GC_PatrolMoon:5...)
    // vem de res/LicenseRules.json (tirado do Init_us.agt com Cliente/ferramentas/licencas_regras.py). O servidor conta
    // o que voce faz (corridas tambem contra bots, HUV, Rush, patrulha, entregas e medalhas, Undercity, missoes,
    // leilao, tempo na cidade), manda os numeros no 814 (a janela de licencas mostra "x / meta") e entrega a licenca
    // quando as condicoes batem ("basta uma" ou "todas", como no jogo).
    public static partial class Licenses
    {
        class Rule { public int Id; public string Name; public bool Support, Any; public List<KeyValuePair<string, long>> Conds = new List<KeyValuePair<string, long>>(); }

        static Dictionary<string, int> gcIds;
        static List<Rule> rules;
        static Dictionary<int, int> trackCity;
        static readonly Dictionary<ulong, uint> infoVersion = new Dictionary<ulong, uint>();
        static readonly Dictionary<ulong, DateTime> fieldSeen = new Dictionary<ulong, DateTime>();

        static void LoadRules()
        {
            if (rules != null) return;
            gcIds = new Dictionary<string, int>(); rules = new List<Rule>(); trackCity = new Dictionary<int, int>();
            try
            {
                string json = null;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "LicenseRules.json");
                if (File.Exists(path)) json = File.ReadAllText(path);
                else
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("LicenseRules.json"))
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                if (json == null) { Log.WriteError("Licencas: LicenseRules.json nao encontrado (progresso desligado)."); return; }
                var j = JObject.Parse(json);
                foreach (var p in (JObject) j["gc"]) gcIds[p.Key] = (int) p.Value;
                foreach (var l in j["licenses"])
                {
                    var r = new Rule { Id = (int) l["id"], Name = (string) l["name"], Support = (bool) l["support"], Any = (bool) l["any"] };
                    foreach (var c in l["conds"]) r.Conds.Add(new KeyValuePair<string, long>((string) c[0], (long) c[1]));
                    rules.Add(r);
                }
                foreach (var p in (JObject) j["tracks"]) trackCity[int.Parse(p.Key)] = (int) p.Value;
                Log.WriteLine("Licencas: {0} regras, {1} condicoes.", rules.Count, gcIds.Count);
            }
            catch (Exception ex) { Log.WriteError("LicenseRules.json: " + ex.Message); }
        }

        // ---- contar ------------------------------------------------------------------------------------------
        public static void Count(Rice.Game.Character ch, params string[] gcs)
        {
            if (ch == null || gcs == null || gcs.Length == 0) return;
            lock (Sync)
            {
                var r = RecOf(ch);
                if (r.Stats == null) r.Stats = new Dictionary<string, long>();
                foreach (var g in gcs)
                {
                    if (string.IsNullOrEmpty(g)) continue;
                    long v; r.Stats.TryGetValue(g, out v); r.Stats[g] = v + 1;
                }
                Save();
            }
            CheckGain(ch);
        }

        // condicao "feito uma vez" (medalha de ouro de uma entrega...): fica 1
        public static void Mark(Rice.Game.Character ch, params string[] gcs)
        {
            if (ch == null || gcs == null) return;
            lock (Sync)
            {
                var r = RecOf(ch);
                if (r.Stats == null) r.Stats = new Dictionary<string, long>();
                foreach (var g in gcs) if (!string.IsNullOrEmpty(g)) { long v; r.Stats.TryGetValue(g, out v); r.Stats[g] = Math.Max(1, v); }
                Save();
            }
            CheckGain(ch);
        }

        static string CitySuffix(int city, int style)
        {
            string[][] n = { new[] { "Moon", "Koi", "Cras", "Oros" }, new[] { "_Mo", "_Ko", "_Cr", "_Or" }, new[] { "_MO", "_KO", "_CR", "" } };
            return city >= 1 && city <= 4 ? n[style][city - 1] : "";
        }

        // corrida na Battle Zone (contra bots tambem): individual/time e a pista da cidade
        public static void CountRace(Rice.Game.Character ch, bool team, int map)
        {
            LoadRules();
            var list = new List<string>();
            if (team || RiceServer.Config.LicenseRaceCountsBoth) list.Add("GC_BattleTeam");
            if (!team || RiceServer.Config.LicenseRaceCountsBoth) list.Add("GC_BattlePersonal");
            int city;
            if (trackCity != null && trackCity.TryGetValue(map, out city) && city >= 1 && city <= 4) list.Add("GC_BattleTrack" + CitySuffix(city, 0));
            Count(ch, list.ToArray());
        }

        public static void CountHuv(Rice.Game.Character ch, bool party)
        {
            var list = new List<string> { "GC_ChasingClear" };
            string s = CitySuffix(ch.City, 1);
            if (s != "") list.Add("GC_ChasingClear" + s);
            if (party) { list.Add("GC_PartyChasingClear"); if (s != "") list.Add("GC_PartyChasingClear" + s); }
            Count(ch, list.ToArray());
        }

        public static void CountPatrol(Rice.Game.Character ch, bool party)
        {
            string c = CitySuffix(ch.City, 0);
            var list = new List<string> { "GC_PatrolClear" };
            if (c != "") list.Add("GC_Patrol" + c);
            if (party) { list.Add("GC_PartyPatrolClear"); if (c != "") list.Add("GC_Partypatrol" + c); }
            Count(ch, list.ToArray());
        }

        public static void CountUndercity(Rice.Game.Character ch, int city)
        {
            string s = CitySuffix(city, 2);
            if (s != "") Count(ch, "GC_UndercityClear", "GC_UndercityClear" + s); else Count(ch, "GC_UndercityClear");
        }

        public static void CountQuest(Rice.Game.Character ch, string questId)
        {
            if (string.IsNullOrEmpty(questId) || !questId.StartsWith("rq_")) return;
            string s = CitySuffix(ch.City, 1);
            if (s == "_Mo" || s == "_Ko") Count(ch, "GC_RQuestClear", "GC_RQuestClear" + s); else Count(ch, "GC_RQuestClear");
        }

        // entrega: medalha 1 ouro, 2 prata, 3 bronze, 0 nenhuma; job "a_00001" = entrega numero 1
        public static void CountDelivery(Rice.Game.Character ch, string jobId, int medal)
        {
            LoadRules();
            var list = new List<string> { "GC_QuickService" };
            if (medal == 1) list.Add("GC_QuickGold"); else if (medal == 2) list.Add("GC_QuickSiver"); else if (medal == 3) list.Add("GC_QuickBronze");
            Count(ch, list.ToArray());
            int num;
            if (jobId == null || jobId.Length < 3 || !int.TryParse(jobId.Substring(2), out num) || num <= 0) return;
            string tail = "_" + num.ToString("000");
            var marks = new List<string>();
            if (medal >= 1 && medal <= 3 && gcIds.ContainsKey("GC_QuickMoon" + tail)) marks.Add("GC_QuickMoon" + tail);
            if (medal == 1) marks.AddRange(gcIds.Keys.Where(k => k.StartsWith("GC_QuickG") && k.EndsWith(tail) && k != "GC_QuickGold"));
            if (marks.Count > 0) Mark(ch, marks.ToArray());
        }

        // tempo na cidade: a cada 813 (o cliente manda a cada ~2 min) soma o tempo desde o ultimo, se voce esta na rua
        static void FieldTick(Rice.Game.Character ch)
        {
            if (ch == null) return;
            DateTime last; var now = DateTime.UtcNow;
            bool field = ch.Area != null && ch.Area.ID >= 0 && ch.Area.ID < 100;
            lock (Sync)
            {
                bool had = fieldSeen.TryGetValue(ch.CID, out last);
                fieldSeen[ch.CID] = now;
                if (!had || !field) return;
                long secs = (long) Math.Min(300, Math.Max(0, (now - last).TotalSeconds));
                if (secs <= 0) return;
                var r = RecOf(ch);
                if (r.Stats == null) r.Stats = new Dictionary<string, long>();
                long v; r.Stats.TryGetValue("GC_FieldTime", out v); r.Stats["GC_FieldTime"] = v + secs;
            }
        }

        // ---- valores e entrega -------------------------------------------------------------------------------
        static long Value(Rec r, HashSet<int> held, string gc)
        {
            if (gc.StartsWith("GC_LicenseGain_"))
            {
                foreach (var part in gc.Substring(15).Split('_')) { int id; if (int.TryParse(part, out id) && held.Contains(id)) return 1; }
                return 0;
            }
            long v = 0;
            if (r.Stats != null) r.Stats.TryGetValue(gc, out v);
            return v;
        }

        static List<KeyValuePair<int, long>> CondValues(Rice.Game.Character ch, Rec r)
        {
            LoadRules();
            var held = new HashSet<int>(Held(ch, r).Select(l => l.id));
            var outl = new List<KeyValuePair<int, long>>();
            foreach (var kv in gcIds)
            {
                long v = Value(r, held, kv.Key);
                if (v > 0) outl.Add(new KeyValuePair<int, long>(kv.Value, v));
            }
            return outl.OrderBy(x => x.Key).ToList();
        }

        public static void CheckGain(Rice.Game.Character ch)
        {
            if (ch == null) return;
            LoadRules();
            if (rules == null || rules.Count == 0) return;
            var got = new List<string>();
            lock (Sync)
            {
                var r = RecOf(ch);
                if (ch.IsUpped && RiceServer.Config.UppedAllLicenses) return;
                var supported = new HashSet<int>(Table().Where(l => l.support).Select(l => l.id));
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    var held = new HashSet<int>(Held(ch, r).Select(l => l.id));
                    foreach (var rule in rules)
                    {
                        if (!rule.Support || held.Contains(rule.Id) || !supported.Contains(rule.Id)) continue;
                        var conds = rule.Conds.Where(c => c.Value > 0).ToList();
                        if (conds.Count == 0) continue;
                        bool ok = rule.Any ? conds.Any(c => Value(r, held, c.Key) >= c.Value) : conds.All(c => Value(r, held, c.Key) >= c.Value);
                        if (!ok) continue;
                        r.Held.Add(rule.Id); held.Add(rule.Id); got.Add(rule.Name); changed = true;
                    }
                }
                if (got.Count > 0) Save();
            }
            if (got.Count == 0) return;
            var c0 = Bots.ClientOf((long) ch.CID);
            Log.WriteLine("Licencas: {0} ganhou {1}.", ch.Name, string.Join(", ", got));
            if (c0 == null) return;
            foreach (var n in got) Chat.SendSystemMessage(c0, "Licenca nova: " + n + "! Monte ela na janela de licencas.");
            SendHeld(c0, ch);
        }

        // 812 de novo com a lista atualizada (a janela de licencas mostra a nova)
        static void SendHeld(RiceClient c, Rice.Game.Character ch)
        {
            List<Lic> held; uint ver;
            lock (Sync)
            {
                held = Held(ch, RecOf(ch));
                if (!infoVersion.TryGetValue(ch.CID, out ver)) ver = 1;
                infoVersion[ch.CID] = ver + 1;
            }
            var pk = new RicePacket(812);
            pk.Writer.Write(ver);
            pk.Writer.Write((uint) held.Count);
            foreach (var l in held) { pk.Writer.Write((ushort) l.id); pk.Writer.Write((ushort) 0); pk.Writer.Write((ushort) 0); }
            c.Send(pk);
        }

        // !!licencas: progresso das licencas que faltam (as mais perto primeiro)
        public static string Progress(Rice.Game.Character ch)
        {
            LoadRules();
            var lines = new List<string>();
            lock (Sync)
            {
                var r = RecOf(ch);
                var held = new HashSet<int>(Held(ch, r).Select(l => l.id));
                var supported = new HashSet<int>(Table().Where(l => l.support).Select(l => l.id));
                foreach (var rule in rules.Where(x => x.Support && supported.Contains(x.Id) && !held.Contains(x.Id)))
                {
                    var conds = rule.Conds.Where(c => c.Value > 0).ToList();
                    if (conds.Count == 0) continue;
                    double pct = rule.Any ? conds.Max(c => Math.Min(1.0, Value(r, held, c.Key) / (double) c.Value))
                                          : conds.Average(c => Math.Min(1.0, Value(r, held, c.Key) / (double) c.Value));
                    if (pct <= 0) continue;
                    lines.Add((int) (pct * 100) + "% " + rule.Name);
                }
            }
            return lines.Count == 0 ? "Nenhum progresso ainda." : string.Join(" | ", lines.OrderByDescending(x => int.Parse(x.Split('%')[0])).Take(8));
        }
    }
}
