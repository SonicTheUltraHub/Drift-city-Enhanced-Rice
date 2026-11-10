using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Bots da sua party evoluem rapido com voce: a cada PartyBotGrowMinutes, ou quando voce ganha exp (no maximo uma
    // vez por PartyBotGrowMinGapSeconds), cada bot da party sobe de nivel, pode equipar uma peca melhor, trocar/comprar
    // o carro do V do nivel dele e avancar as licencas. Fora da party os bots ficam parados (BotsFrozen).
    public static class PartyGrowth
    {
        static Timer timer;
        static readonly Random Rng = new Random();
        static readonly Dictionary<ulong, DateTime> last = new Dictionary<ulong, DateTime>();
        static readonly object Sync = new object();

        public static void Start()
        {
            if (timer != null) return;
            timer = new Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("Evolucao da party: " + ex.Message); } }, null, 30000, 30000);
        }

        static void Tick()
        {
            double mins = Math.Max(1, RiceServer.Config.PartyBotGrowMinutes);
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                if (ch == null) continue;
                DateTime t;
                lock (Sync) if (last.TryGetValue(ch.CID, out t) && (DateTime.UtcNow - t).TotalMinutes < mins) continue;
                Grow(p.GameClient, ch);
            }
        }

        // voce ganhou exp / progresso: os bots da party tambem andam (com intervalo minimo)
        public static void OnPlayerProgress(Rice.Game.Character ch)
        {
            if (ch == null) return;
            DateTime t;
            lock (Sync) if (last.TryGetValue(ch.CID, out t) && (DateTime.UtcNow - t).TotalSeconds < Math.Max(10, RiceServer.Config.PartyBotGrowMinGapSeconds)) return;
            var p = RiceServer.GetPlayers().FirstOrDefault(x => x != null && x.ActiveCharacter == ch);
            if (p == null) return;
            ThreadPool.QueueUserWorkItem(_ => { try { Grow(p.GameClient, ch); } catch (Exception ex) { Log.WriteError("Evolucao da party: " + ex.Message); } });
        }

        static void Grow(RiceClient c, Rice.Game.Character ch)
        {
            lock (Sync) last[ch.CID] = DateTime.UtcNow;
            var cfg = RiceServer.Config;
            var bots = BotSocial.PartyBots(ch).Where(b => !b.IsAlt && !b.Special && !Bots.IsAce(b)).ToList();
            if (bots.Count == 0) return;
            bool changed = false;
            int said = 0;
            foreach (var b in bots)
            {
                var news = new List<string>();
                // nivel: sobe ate PartyBotGrowLevels por vez, sem passar do seu nivel + PartyBotLevelAbove
                int cap = Math.Min(99, Math.Max(b.Level, ch.Level + Math.Max(0, cfg.PartyBotLevelAbove)));
                int up = Math.Min(Math.Max(1, cfg.PartyBotGrowLevels), cap - b.Level);
                if (up > 0)
                {
                    b.Level += up; b.Exp = 0;
                    news.Add("subi pro nivel " + b.Level);
                    changed = true;
                }
                // peca melhor do nivel dele
                if (Rng.NextDouble() < cfg.PartyBotPartChance)
                {
                    string part = BetterPart(b);
                    if (part != null) { news.Add("equipei " + part); changed = true; }
                }
                // carro do V do nivel (troca na garagem ou compra)
                int before = b.Car;
                if (Bots.TierCarFor(b) && b.Car != before) { news.Add("agora corro de " + Bots.CarName(b.Car)); changed = true; }
                // licencas
                string lic = Licenses.Activity(b, cfg.BotLicenseActivity * Math.Max(1, cfg.PartyBotLicenseBoost));
                if (lic.Length > 0) changed = true;
                if (news.Count > 0 && c != null)
                    Bots.SayLater(c, 1500 + 900 * (bots.Count + said++), "party", b.Name, "Correndo com voce eu evoluo rapido: " + string.Join(", ", news) + "!"
                                                                     + (lic.Length > 0 ? " Licencas: " + lic : ""));
            }
            if (changed) { Bots.Save(); Log.WriteLine("Party de {0}: bots evoluiram ({1}).", ch.Name, string.Join(", ", bots.Select(b => b.Name + " nv " + b.Level))); }
        }

        static double Points(string id)
        {
            var e = id != null ? ItemTable.Items.FirstOrDefault(x => x.ID == id) as ItemTableEntry : null;
            return e != null ? e.BasePoints : 0;
        }

        // procura uma peca da loja do nivel do bot melhor que a pior das duas dessa categoria; equipa e devolve o nome
        static string BetterPart(Bots.BotProfile b)
        {
            for (int t = 0; t < 10; t++)
            {
                var e = Auction.RewardItem(Math.Max(1, b.Level)) as ItemTableEntry;
                if (e == null || !BotSocial.PartCats.Contains(e.Category) || b.Level < Auction.ItemLevel(e.ID)) continue;
                string k1 = e.Category, k2 = e.Category + "#2", s1, s2;
                b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
                string sl = s1 == null ? k1 : s2 == null ? k2 : (Points(s1) <= Points(s2) ? k1 : k2);
                string o; b.Parts.TryGetValue(sl, out o);
                if (o != null && e.BasePoints <= Points(o)) continue;
                b.Parts[sl] = e.ID;
                return e.Name;
            }
            return null;
        }
    }
}
