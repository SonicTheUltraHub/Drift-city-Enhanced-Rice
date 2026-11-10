using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Game
{
    // Logica do Part Shop feita pelo servidor: Combine com Seed (sorteio de dock) e Convert (opcoes aleatorias).
    public static class PartCrafting
    {
        private static readonly Random Rng = new Random();
        private static readonly string[] Tiers = { "N", "S", "H", "U" };

        // ---------------------------------------------------------------- utilidades

        private static bool IsPerformancePart(Item it)
        {
            if (it == null || !(it.itemEntry is ItemTableEntry))
                return false;
            string c = it.itemEntry.Category;
            return c == "speed" || c == "accel" || c == "crash" || c == "boost";
        }

        private static string PartTier(Item it)
        {
            var e = it.itemEntry as ItemTableEntry;
            return e != null ? e.Grade : null;
        }

        // Categoria da peca -> categoria do dock / sufixo das opcoes (AL_xx).
        private static string DockCategory(string partCategory)
        {
            switch (partCategory)
            {
                case "speed": return "dock_S";
                case "accel": return "dock_A";
                case "crash": return "dock_C";
                case "boost": return "dock_B";
            }
            return null;
        }

        private static string OptionPrefix(string partCategory)
        {
            switch (partCategory)
            {
                case "speed": return "AL_SP_";
                case "accel": return "AL_AP_";
                case "crash": return "AL_CP_";
                case "boost": return "AL_BP_";
            }
            return null;
        }

        // Seed Part -> grau (0 N, 1 S, 2 H, 3 U), lido do campo "stat" da tabela de itens de uso.
        private static int SeedTier(Item seed)
        {
            var use = seed.itemEntry as UseItemTableEntry;
            if (use == null || (use.Category != "craft_up" && use.Category != "craft_up_plus"))
                return -1;
            int t;
            return int.TryParse(use.StatModifier, out t) ? t : -1;
        }

        private static double MaxBasePoints(string category, string grade)
        {
            double max = 1;
            foreach (var e in ItemTable.Items)
            {
                var pe = e as ItemTableEntry;
                if (pe == null || pe.Category != category || pe.Grade != grade)
                    continue;
                if (!Regex.IsMatch(pe.ID, @"^i_[sacb]_\d{6}$"))
                    continue;
                if (pe.BasePoints > max) max = pe.BasePoints;
            }
            return max;
        }

        // Escolhe um indice 0..n-1; indices menores (letra A) sao mais comuns. q (0..1) achata a curva.
        private static int WeightedLetter(int n, double q)
        {
            double exponent = 3.0 - 2.2 * q;
            var weights = new double[n];
            double total = 0;
            for (int i = 0; i < n; i++)
            {
                weights[i] = Math.Pow(n - i, exponent);
                total += weights[i];
            }
            double roll = Rng.NextDouble() * total;
            for (int i = 0; i < n; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return i;
            }
            return 0;
        }

        // ---------------------------------------------------------------- Combine com Seed

        // 3 pecas do mesmo tipo e grau + 1 Seed Part do mesmo grau -> 1 dock sorteado.
        public static bool CombineWithSeed(Character c, int[] partIdxs, int seedIdx, out Item result, out string message)
        {
            result = null;
            message = null;
            var cfg = RiceServer.Config;

            if (!cfg.DockDrawEnabled)
            {
                message = "o sorteio de dock esta desligado (DockDrawEnabled)";
                return false;
            }
            if (partIdxs == null || partIdxs.Length != 3 || partIdxs.Distinct().Count() != 3)
            {
                message = "escolha 3 pecas diferentes";
                return false;
            }

            var parts = partIdxs.Select(i => c.Inventory.FirstOrDefault(x => x.invIdx == i)).ToList();
            var seed = c.Inventory.FirstOrDefault(x => x.invIdx == seedIdx);
            if (parts.Any(p => p == null) || seed == null)
            {
                message = "item nao encontrado no inventario";
                return false;
            }
            if (parts.Any(p => !IsPerformancePart(p) || p.state != 0 || p.stackNum < 1))
            {
                message = "use 3 pecas de performance desequipadas";
                return false;
            }

            string category = parts[0].itemEntry.Category;
            string tier = PartTier(parts[0]);
            if (parts.Any(p => p.itemEntry.Category != category || PartTier(p) != tier))
            {
                message = "as 3 pecas precisam ser do mesmo tipo e do mesmo grau";
                return false;
            }

            int tierIdx = Array.IndexOf(Tiers, tier);
            int seedTier = SeedTier(seed);
            if (tierIdx < 0 || seedTier < 0)
            {
                message = "Seed Part invalida ou grau de peca sem dock";
                return false;
            }
            if (seedTier != tierIdx)
            {
                message = "a Seed Part e as pecas precisam ser do mesmo grau (" + tier + ")";
                return false;
            }

            // Lista de docks possiveis (A, B, C...) para esse tipo e grau.
            string dockCat = DockCategory(category);
            var letters = new List<string>();
            for (int n = 1; n < 40; n++)
            {
                string id = string.Format("{0}_{1}{2:000}", dockCat, tier, n);
                if (ItemTable.Items.Any(x => x.ID == id)) letters.Add(id); else break;
            }
            if (letters.Count == 0)
            {
                message = "nao existe dock para essas pecas";
                return false;
            }

            // Qualidade 0..1: pontos base de cada peca (o do meio pesa mais) + nivel de upgrade medio.
            double maxBase = MaxBasePoints(category, tier);
            double b0 = ((ItemTableEntry) parts[0].itemEntry).BasePoints;
            double b1 = ((ItemTableEntry) parts[1].itemEntry).BasePoints;
            double b2 = ((ItemTableEntry) parts[2].itemEntry).BasePoints;
            double white = 0.25 * b0 + 0.5 * b1 + 0.25 * b2;      // pontos brancos
            double avgUpgrade = parts.Average(p => (double) p.upgrade);
            double q = Math.Min(1.0, (white / maxBase) * 0.8 + Math.Min(avgUpgrade, 10) / 10.0 * 0.2);
            string seedName = seed.itemEntry.Name ?? seed.ID;
            if (seed.itemEntry.Category == "craft_up_plus")
                q = Math.Min(1.0, q + 0.15);

            int pick = WeightedLetter(letters.Count, q);
            string dockId = letters[pick];

            // Consome as 3 pecas e a seed.
            foreach (var p in parts)
            {
                Item dropped;
                if (!c.DropItem((int) p.invIdx, out dropped, 1, true))
                {
                    message = "falha ao consumir as pecas";
                    return false;
                }
            }
            Item usedSeed;
            c.DropItem((int) seed.invIdx, out usedSeed, 1, true);

            if (!c.GrantItem(dockId, 1, out result))
            {
                message = "falha ao criar o dock " + dockId;
                return false;
            }

            // Pontos do dock: sorteados dentro da faixa da letra (DockPoint*), somam direto no carro.
            string dockTxt;
            int dockPts = RollDock(c, result, out dockTxt);

            char letter = (char) ('A' + pick);
            message = string.Format("{0}: {1} (letra {2} de {3}), +{4} pontos, pecas: brancos {5:0}, roxos {6:0}, qualidade {7:P0}.",
                seedName, result.itemEntry.Name, letter, (char) ('A' + letters.Count - 1), dockPts, white, avgUpgrade, q);
            Log.WriteLine("Dock sorteado: {0} ({1}) q={2:0.00} brancos={3:0.0} roxos={4:0.0} letras={5}",
                dockId, result.itemEntry.Name, q, white, avgUpgrade, letters.Count);
            return true;
        }

        // Faixas de bonus por posicao da letra (12 posicoes: A, B-D, E-F, S/SS, G-I, J-K).
        private static readonly int[][] DockRanges =
        {
            new[] {10, 18},
            new[] {19, 28}, new[] {19, 28}, new[] {19, 28},
            new[] {29, 36}, new[] {29, 36},
            new[] {37, 48},
            new[] {49, 58}, new[] {49, 58}, new[] {49, 58},
            new[] {59, 72}, new[] {59, 72}
        };

        // letterIdx 0..count-1 (A..ultima) e escalado para as 12 posicoes, assim N (8 letras) e U (19) usam a mesma tabela.
        public static int RollDockPercent(int letterIdx, int count)
        {
            int pos = count <= 1 ? 0 : (int) Math.Round(letterIdx * 11.0 / (count - 1));
            if (pos < 0) pos = 0;
            if (pos > 11) pos = 11;
            return Rng.Next(DockRanges[pos][0], DockRanges[pos][1] + 1);
        }

        // ---------------------------------------------------------------- Modify

        // Procura nos numeros do pacote 1 peca + 1 Mittron Enhancer. Devolve true se reconheceu (mesmo em falha).
        // result: 0x30000 sucesso, 0x30001 falhou (nada muda), 0x30002 peca destruida. fee = mito cobrado.
        public static bool TryModifyFromPacket(Character c, byte[] buf, out string message, out int fee, out int result)
        {
            message = null;
            fee = 0;
            result = 0x30001;
            var cfg = RiceServer.Config;
            if (!cfg.ModifyEnabled)
                return false;

            var candidates = new List<int>();
            for (int off = 0; off + 4 <= buf.Length && off < 80; off += 4)
            {
                int v = BitConverter.ToInt32(buf, off);
                if (v > 0 && v < 100000 && !candidates.Contains(v)) candidates.Add(v);
            }

            Item part = null, enhancer = null;
            foreach (int idx in candidates)
            {
                var it = c.Inventory.FirstOrDefault(x => x.invIdx == (uint) idx);
                if (it == null) continue;
                if (IsPerformancePart(it) && part == null) part = it;
                else if (it.itemEntry.Category == "upgrade" && enhancer == null) enhancer = it;
            }

            if (part == null || enhancer == null)
                return false;

            if (part.state != 0)
            {
                message = "Modify: desequipe a peca antes.";
                return true;
            }

            // Enhancer N/S/H precisa combinar com o grau da peca; o generico (i_n_00003 etc.) serve para todas.
            string tier = PartTier(part);
            string needed = tier == "N" ? "i_n_00043" : tier == "S" ? "i_n_00044" : tier == "H" ? "i_n_00045" : null;
            bool generic = enhancer.ID.StartsWith("i_n_00003");
            if (!generic && (needed == null || !enhancer.ID.StartsWith(needed)))
            {
                message = "Modify: este Enhancer nao serve para pecas " + tier + ".";
                return true;
            }

            int level = part.upgrade;
            long feeL = (long) cfg.ModifyFeePerLevel * (level + 1);
            if (!c.SpendMito((ulong) feeL))
            {
                message = "Modify: mito insuficiente (" + feeL + ").";
                return true;
            }

            Item d;
            c.DropItem((int) enhancer.invIdx, out d, 1, true);

            // Chance: 100% ate +2; depois cai 10% por nivel (minimo 15%).
            int chance = level < 3 ? 100 : Math.Max(15, 100 - 10 * (level - 2));
            bool ok = Rng.Next(100) < chance;
            fee = (int) feeL;
            result = ok ? 0x30000 : 0x30001;
            if (ok)
            {
                int gain = Rng.Next(1, 4);
                part.upgrade = level + 1;
                part.upgradePoint += gain;
                c.SaveItemState(part);
                message = string.Format("Modify: {0} subiu para +{1} (+{2} pontos). Chance era {3}%.", part.itemEntry.Name, part.upgrade, gain, chance);
            }
            else
            {
                message = string.Format("Modify: falhou ({0}% de chance). O Enhancer foi gasto.", chance);
            }
            Log.WriteLine("Modify: part={0} enhancer={1} nivel {2} -> {3} (chance {4}%, taxa {5}).",
                part.ID, enhancer.ID, level, ok ? level + 1 : level, chance, feeL);
            return true;
        }

        // ---------------------------------------------------------------- peca aleatoria (Rush)

        // Uma peca de desempenho (speed/accel/crash/boost) do nivel do corredor para baixo. Com chance upChance ela vem
        // upada (+1 a +9, com os pontos do Modify) e ja com Convert (uma opcao do tipo da peca e o drop rate).
        public static Item RandomPartFor(Character c, double upChance, out string text)
        {
            return RandomPart(c, null, c != null ? c.Level : 1, 0, upChance, out text);
        }

        // nivel da primeira peca de desempenho do grau (S = 31, H = 60...)
        public static int FirstLevelOf(string grade)
        {
            var l = ItemTable.Items.OfType<ItemTableEntry>().Where(e => e.Grade == grade && e.ID != null && StdPart.IsMatch(e.ID)).ToList();
            return l.Count > 0 ? l.Min(e => MinLv(e)) : 999;
        }

        static Item Give(Character c, string id, out string text)
        {
            text = null;
            Item it;
            if (c == null || !c.GrantItem(id, 1, out it) || it == null) return null;
            text = "voce pegou " + (it.itemEntry.Name ?? id) + "!";
            return it;
        }

        // Mittron Enhancer ou Seed Part (N; S e H so a partir do nivel da primeira peca S / H)
        public static Item MittronOrSeed(Character c, out string text)
        {
            text = null;
            if (c == null) return null;
            var g = new List<int> { 0 };
            if (c.Level >= FirstLevelOf("S")) g.Add(1);
            if (c.Level >= FirstLevelOf("H")) g.Add(2);
            int t = g[Rng.Next(g.Count)];
            string[] enh = { "i_n_00043", "i_n_00044", "i_n_00045" }, seed = { "i_n_Seed_N", "i_n_Seed_S", "i_n_Seed_H" };
            return Give(c, Rng.Next(2) == 0 ? enh[t] : seed[t], out text);
        }

        // Batida em HUV: Booster Protector F/D, Speed/Accel/Dura Enhancer, peca do seu nivel; HuvDropMittronChance de vir
        // Mittron Enhancer / Seed (MittronOrSeed)
        public static Item HuvDrop(Character c, out string text)
        {
            text = null;
            if (c == null) return null;
            if (Rng.NextDouble() < RiceServer.Config.HuvDropMittronChance) return MittronOrSeed(c, out text);
            string[] simple = { "i_n_00015", "i_n_00016", "i_n_00021", "i_n_00019", "i_n_00020" };
            int k = Rng.Next(simple.Length + 1);
            if (k < simple.Length) return Give(c, simple[k], out text);
            return RandomPart(c, null, c.Level, 1, 0, out text);   // ja sorteia a qualidade
        }

        // Qualidade da peca de desempenho: na hora que voce ganha/compra, sorteia de 0 ate N pontos a mais sobre o ponto
        // padrao (N: grau N 4, S 10, H 16, U 28; PartQualityRange). Perto do padrao = fraca, longe = boa. Os pontos vao no
        // upgradePoint (somam no carro como os do Modify).
        public static int RollQuality(Character c, Item it, out string text)
        {
            text = null;
            var e = it != null ? it.itemEntry as ItemTableEntry : null;
            if (c == null || e == null || !RiceServer.Config.PartQualityRoll) return 0;
            string cat = e.Category;
            if (cat != "speed" && cat != "accel" && cat != "crash" && cat != "boost") return 0;
            var ranges = RiceServer.Config.PartQualityRange;
            int ti = Array.IndexOf(Tiers, e.Grade);
            if (ti < 0 || ranges == null || ti >= ranges.Length) return 0;
            int n = Math.Max(0, ranges[ti]);
            if (n == 0) return 0;
            // mais facil sair perto do padrao: peso (n + 1 - k)
            int total = 0; for (int k = 0; k <= n; k++) total += n + 1 - k;
            int roll = Rng.Next(total), pick = 0;
            for (int k = 0; k <= n; k++) { roll -= n + 1 - k; if (roll < 0) { pick = k; break; } }
            if (pick > 0) { it.upgradePoint += pick; c.SaveItemState(it); }
            double q = (double) pick / n;
            string nome = q >= 0.85 ? "excelente" : q >= 0.55 ? "boa" : q >= 0.25 ? "media" : "fraca";
            text = e.Name + ": qualidade " + nome + " (" + (int) Math.Round(e.BasePoints + pick) + " pontos; padrao " + (int) Math.Round(e.BasePoints) + ", ate " + (int) Math.Round(e.BasePoints + n) + ")";
            return pick;
        }

        // ---- Qualidade da Option Part: 0 ate OptionQualityRange pontos a mais (mais facil perto do padrao) ----
        public static int RollOptionQuality(Character c, Item it)
        {
            var e = it != null ? it.itemEntry as ItemTableEntry : null;
            if (c == null || e == null || e.Category == null || !e.Category.StartsWith("op_") || e.BasePoints <= 0) return 0;
            int n = Math.Max(0, RiceServer.Config.OptionQualityRange);
            if (n == 0) return 0;
            int total = 0; for (int k = 0; k <= n; k++) total += n + 1 - k;
            int roll = Rng.Next(total), pick = 0;
            for (int k = 0; k <= n; k++) { roll -= n + 1 - k; if (roll < 0) { pick = k; break; } }
            if (pick > 0) { it.upgradePoint += pick; c.SaveItemState(it); }
            return pick;
        }

        // ---- Pontos do Dock ----
        // dock_S_N005: tipo S (speed), grau N, letra 5 (E). -> faixa [min, max] de pontos dessa letra.
        public static bool DockRange(ItemTableEntry e, out int min, out int max)
        {
            min = max = 0;
            if (e == null || e.ID == null || e.Category == null || !e.Category.StartsWith("dock_")) return false;
            var m = Regex.Match(e.ID, @"^(dock_[A-Z])_([NSHU])(\d+)$");
            if (!m.Success) return false;
            int ti = Array.IndexOf(Tiers, m.Groups[2].Value);
            var cfg = RiceServer.Config;
            if (ti < 0 || cfg.DockPointMin == null || cfg.DockPointMax == null || cfg.DockPointSpread == null
                || ti >= cfg.DockPointMin.Length || ti >= cfg.DockPointMax.Length || ti >= cfg.DockPointSpread.Length) return false;
            int letter = int.Parse(m.Groups[3].Value) - 1;
            int count = 0;
            string pre = m.Groups[1].Value + "_" + m.Groups[2].Value;
            for (int n = 1; n < 40; n++)
            {
                string id = string.Format("{0}{1:000}", pre, n);
                if (ItemTable.Items.Any(x => x.ID == id)) count = n; else break;
            }
            if (count <= 0) count = letter + 1;
            double t = count <= 1 ? 0 : Math.Max(0, Math.Min(1, (double) letter / (count - 1)));
            int lo = cfg.DockPointMin[ti], hi = cfg.DockPointMax[ti];
            min = cfg.DockPointStep > 0 ? Math.Max(0, lo + cfg.DockPointStep * Math.Max(0, letter))
                                        : Math.Max(0, (int) Math.Round(lo + (hi - lo) * t));
            max = min + Math.Max(0, cfg.DockPointSpread[ti]);
            return true;
        }

        // Sorteia os pontos do dock (mais facil perto do minimo) e grava no item (upgradePoint).
        public static int RollDock(Character c, Item it, out string text)
        {
            text = null;
            var e = it != null ? it.itemEntry as ItemTableEntry : null;
            int min, max;
            if (c == null || !RiceServer.Config.DockPointRoll || !DockRange(e, out min, out max)) return 0;
            int n = max - min, pick = 0;
            if (n > 0)
            {
                int total = 0; for (int k = 0; k <= n; k++) total += n + 1 - k;
                int roll = Rng.Next(total);
                for (int k = 0; k <= n; k++) { roll -= n + 1 - k; if (roll < 0) { pick = k; break; } }
            }
            int pts = min + pick;
            it.upgradePoint = pts;
            it.upgrade = 0;
            c.SaveItemState(it);
            text = e.Name + ": +" + pts + " pontos (faixa " + min + " a " + max + ")";
            return pts;
        }

        // Dock antigo (gravava % ou sem pontos): sorteia de novo se estiver fora da faixa da letra.
        public static bool FixDock(Character c, Item it)
        {
            var e = it != null ? it.itemEntry as ItemTableEntry : null;
            int min, max;
            if (!RiceServer.Config.DockPointRoll || !DockRange(e, out min, out max)) return false;
            if (it.upgradePoint >= min && it.upgradePoint <= max && it.upgrade == 0) return false;
            string t; RollDock(c, it, out t);
            Log.WriteLine("Dock: {0} ajustado para +{1} pontos.", it.ID, it.upgradePoint);
            return true;
        }

        static int MinLv(ItemTableEntry e) { int lv; return int.TryParse(e.MinLevel, out lv) ? lv : 999; }
        static readonly Regex StdPart = new Regex(@"^i_[a-z]_\d+$");

        // peca de aluguel com o selo RENT (r_type_*), igual para qualquer nivel
        public static Item RandomRent(Character c, out string text)
        {
            text = null;
            if (c == null) return null;
            var pool = ItemTable.Items.OfType<ItemTableEntry>().Where(e => e.ID != null && e.ID.StartsWith("r_type_")).ToList();
            if (pool.Count == 0) return null;
            var pick = pool[Rng.Next(pool.Count)];
            Item it;
            if (!c.GrantItem(pick.ID, 1, out it) || it == null) return null;
            string qt; RollQuality(c, it, out qt);
            text = "voce pegou " + pick.Name + " (RENT)!" + (qt != null ? " " + qt : "");
            Log.WriteLine("Peca: {0} ganhou {1} (RENT).", c.Name, pick.ID);
            return it;
        }

        // Peca de desempenho aleatoria. tiers: graus aceitos (null = N/S/H/U); maxLevel: nivel maximo da peca;
        // mode 0 = qualquer uma ate maxLevel, 1 = perto do nivel (ate 15 abaixo), 2 = a do comeco da linha (menor nivel do grau)
        public static Item RandomPart(Character c, string[] tiers, int maxLevel, int mode, double upChance, out string text)
        {
            text = null;
            if (c == null) return null;
            var all = ItemTable.Items.OfType<ItemTableEntry>().Where(e =>
            {
                string cat = e.Category;
                if (cat != "speed" && cat != "accel" && cat != "crash" && cat != "boost") return false;
                if (Array.IndexOf(Tiers, e.Grade) < 0) return false;
                if (tiers != null && Array.IndexOf(tiers, e.Grade) < 0) return false;
                // so as pecas normais da loja (i_s_000001...): as de aluguel (r_), evento, carbon etc. nao tem nome no jogo
                if (e.ID == null || !StdPart.IsMatch(e.ID)) return false;
                return MinLv(e) <= Math.Max(1, maxLevel);
            }).ToList();
            if (all.Count == 0) return null;
            // primeiro sorteia o grau (para N/S/H terem a mesma chance), depois a peca
            var grades = all.Select(e => e.Grade).Distinct().ToList();
            string g = grades[Rng.Next(grades.Count)];
            var pool = all.Where(e => e.Grade == g).ToList();
            if (mode == 1)
            {
                int top = pool.Max(e => MinLv(e));
                var near = pool.Where(e => MinLv(e) >= top - 15).ToList();
                if (near.Count > 0) pool = near;
            }
            else if (mode == 2)
            {
                int low = pool.Min(e => MinLv(e));
                pool = pool.Where(e => MinLv(e) == low).ToList();
            }
            var pick = pool[Rng.Next(pool.Count)];
            Item it;
            if (!c.GrantItem(pick.ID, 1, out it) || it == null) return null;
            string qual; RollQuality(c, it, out qual);
            bool upped = Rng.NextDouble() < upChance;
            if (upped)
            {
                int lvl = Rng.Next(1, 10);
                int pts = 0;
                for (int k = 0; k < lvl; k++) pts += Rng.Next(1, 4);
                it.upgrade = lvl; it.upgradePoint += pts;
                int tierIdx = Array.IndexOf(Tiers, pick.Grade);
                string prefix = OptionPrefix(pick.Category);
                if (prefix != null && tierIdx >= 0)
                {
                    int grade = RollOptionGrade(false);
                    int statIdx = AssistTable.GetIndex(prefix + StatByTier[tierIdx][grade].ToString("000"));
                    int dropIdx = AssistTable.GetIndex("AL_ID_" + (DropByTier[tierIdx][grade] * 10).ToString("000"));
                    if (statIdx > 0) it.assistA = (uint) statIdx;
                    if (dropIdx > 0) it.assistB = (uint) dropIdx;
                }
                c.SaveItemState(it);
                text = "voce pegou " + pick.Name + " +" + lvl + " (upada e com Convert)!";
            }
            else text = "voce pegou " + pick.Name + ".";
            if (qual != null) text += " " + qual;
            Log.WriteLine("Peca: {0} ganhou {1}{2}.", c.Name, pick.ID, upped ? " +" + it.upgrade : "");
            return it;
        }

        // ---------------------------------------------------------------- Convert

        // Valor do atributo (pontos) e drop rate (%) por grau da opcao (1 = melhor ... 5 = pior) e grau da peca.
        // Hyper vem da tela do jogo (84/81/78/75/72 e 5/4/4/3/3). Os demais graus sao estimativas.
        private static readonly int[][] StatByTier =
        {
            new[] {36, 33, 30, 27, 24},   // N (estimado)
            new[] {60, 57, 54, 51, 48},   // S (estimado)
            new[] {84, 81, 78, 75, 72},   // H (jogo)
            new[] {96, 93, 90, 87, 84}    // U (estimado)
        };
        private static readonly int[][] DropByTier =
        {
            new[] {2, 2, 1, 1, 1},
            new[] {3, 3, 2, 2, 2},
            new[] {5, 4, 4, 3, 3},
            new[] {6, 5, 5, 4, 4}
        };

        private static int RollOptionGrade(bool withFormula)
        {
            int[] w = withFormula ? new[] {15, 30, 30, 15, 10} : new[] {3, 12, 25, 30, 30};
            int roll = Rng.Next(w.Sum());
            for (int i = 0; i < w.Length; i++)
            {
                roll -= w[i];
                if (roll < 0) return i;
            }
            return 4;
        }

        private static string ConvertOptionPrefix(string solutionId, string partCategory)
        {
            switch (solutionId)
            {
                case "i_n_00040": return "AL_SP_";   // Type A: velocidade
                case "i_n_00041": return "AL_AP_";   // Type B: aceleracao
                case "i_n_00042": return "AL_CP_";   // Type C: durabilidade
            }
            // Change Solution (N/S/H): re-sorteia a opcao do tipo da propria peca.
            return OptionPrefix(partCategory);
        }

        // Convert de Option Part: duas opcoes sorteadas entre Speed/Accel/Dura/Boost +N, velocidade maxima (mph) e tempo de
        // booster, mais fortes no grau S/H da opcao (FS/FH) e com a Formula.
        // duas opcoes sorteadas (indices da tabela de assists, 0 = falhou) para uma option/dock desse grau
        public static bool RandomOptionPair(ItemTableEntry e, bool withFormula, out int a1, out int a2)
        {
            string g = e != null && e.Grade != null && e.Grade.Length > 1 ? e.Grade.Substring(1, 1) : "N";
            int ti = g == "H" || g == "U" ? 2 : g == "S" ? 1 : 0;
            int bonus = withFormula ? 2 : 0;
            string[][] speedup = { new[] {"a_00031", "a_00032", "a_00033"}, new[] {"a_00033", "a_00034", "a_00035"}, new[] {"a_00035", "a_00125", "a_00125"} };
            string[][] turbo = { new[] {"a_00048", "a_00049", "a_00050"}, new[] {"a_00074", "a_00075", "a_00093"}, new[] {"a_00094", "a_00095", "a_00097"} };
            int[] lo = { 2, 6, 10 }, hi = { 8, 14, 20 };
            Func<int, string> pick = kind =>
            {
                switch (kind)
                {
                    case 4: { var l = speedup[ti]; return l[Math.Min(l.Length - 1, Rng.Next(l.Length) + (bonus > 0 ? 1 : 0))]; }
                    case 5: { var l = turbo[ti]; return l[Math.Min(l.Length - 1, Rng.Next(l.Length) + (bonus > 0 ? 1 : 0))]; }
                    default:
                        string[] pre = { "AL_SP_", "AL_AP_", "AL_CP_", "AL_BP_" };
                        int v = Math.Min(40, Rng.Next(lo[ti], hi[ti] + 1) + bonus);
                        return pre[kind] + v.ToString("000");
                }
            };
            int k1 = Rng.Next(6), k2 = Rng.Next(5); if (k2 >= k1) k2++;
            a1 = AssistTable.GetIndex(pick(k1)); a2 = AssistTable.GetIndex(pick(k2));
            return a1 > 0 && a2 > 0;
        }

        static void ConvertOption(Character c, Item part, Item solution, Item formula, out string message)
        {
            int a1, a2;
            if (!RandomOptionPair(part.itemEntry as ItemTableEntry, formula != null, out a1, out a2)) { message = "opcao nao encontrada na tabela"; return; }
            Item d;
            c.DropItem((int) solution.invIdx, out d, 1, true);
            if (formula != null) c.DropItem((int) formula.invIdx, out d, 1, true);
            part.assistA = (uint) a1;
            part.assistB = (uint) a2;
            c.SaveItemState(part);
            message = string.Format("Convert: {0} recebeu {1} e {2}{3}.", part.itemEntry.Name,
                AssistTable.Assists[a1 - 1].Name, AssistTable.Assists[a2 - 1].Name, formula != null ? " (com Formula)" : "");
            Log.WriteLine("Convert (option): {0} -> {1} + {2}", part.ID, AssistTable.Assists[a1 - 1].ID, AssistTable.Assists[a2 - 1].ID);
        }

        // Procura nos numeros do pacote (qualquer int32 alinhado) os itens do Convert: 1 peca + 1 solucao (+ formula).
        public static bool TryConvertFromPacket(Character c, byte[] buf, out string message)
        {
            message = null;
            var cfg = RiceServer.Config;
            if (!cfg.ConvertEnabled)
                return false;

            var candidates = new HashSet<int>();
            for (int off = 0; off + 4 <= buf.Length && off < 80; off += 4)
            {
                int v = BitConverter.ToInt32(buf, off);
                if (v > 0 && v < 100000) candidates.Add(v);
            }

            Item part = null, solution = null, formula = null;
            foreach (int idx in candidates)
            {
                var it = c.Inventory.FirstOrDefault(x => x.invIdx == (uint) idx);
                if (it == null) continue;
                string cat = it.itemEntry.Category;
                if ((IsPerformancePart(it) || (cat != null && (cat.StartsWith("op_") || cat.StartsWith("dock_")))) && part == null && it.state == 0) part = it;
                else if (cat == "trans" && solution == null) solution = it;
                else if (cat == "trans_up" && formula == null) formula = it;
            }

            if (part == null || solution == null)
                return false;   // nao parece um Convert; deixa so o log

            if (part.itemEntry.Category.StartsWith("op_") || part.itemEntry.Category.StartsWith("dock_"))
            {
                ConvertOption(c, part, solution, formula, out message);
                return true;
            }

            string prefix = ConvertOptionPrefix(solution.ID, part.itemEntry.Category);
            string tier = PartTier(part);
            int tierIdx = Array.IndexOf(Tiers, tier);
            if (prefix == null || tierIdx < 0)
            {
                message = "essa peca nao pode ser convertida";
                return true;
            }

            int grade = RollOptionGrade(formula != null);
            int statValue = StatByTier[tierIdx][grade];
            int dropValue = DropByTier[tierIdx][grade];

            int statIdx = AssistTable.GetIndex(prefix + statValue.ToString("000"));
            int dropIdx = AssistTable.GetIndex("AL_ID_" + (dropValue * 10).ToString("000"));
            if (statIdx < 0 || dropIdx < 0)
            {
                message = "opcao nao encontrada na tabela (" + prefix + statValue + ")";
                return true;
            }

            Item d;
            c.DropItem((int) solution.invIdx, out d, 1, true);
            if (formula != null)
                c.DropItem((int) formula.invIdx, out d, 1, true);

            part.assistA = (uint) statIdx;
            part.assistB = (uint) dropIdx;
            c.SaveItemState(part);

            var statEntry = AssistTable.Assists[statIdx - 1];
            var dropEntry = AssistTable.Assists[dropIdx - 1];
            message = string.Format("Convert: {0} recebeu {1} e {2} (grau {3}{4}).",
                part.itemEntry.Name, statEntry.Name, dropEntry.Name, grade + 1, formula != null ? ", com Formula" : "");
            Log.WriteLine("Convert: part={0} solution={1} formula={2} -> {3} + {4}",
                part.ID, solution.ID, formula != null ? formula.ID : "-", statEntry.ID, dropEntry.ID);
            return true;
        }
    }
}
