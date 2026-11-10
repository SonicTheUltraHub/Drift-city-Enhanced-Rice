using System;
using System.Collections.Generic;
using System.Linq;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Game
{
    // Recompensas do modo offline: nivel (a cada 10 niveis) e combo (itens).
    // Os avisos ficam em Character.PendingNotices e saem no chat quando o pacote termina (Character.FlushNotices).
    public static class Rewards
    {
        // Tipos de peca de performance: letra da familia / categoria da peca / categoria do dock.
        private static readonly string[] PartPrefix = { "i_s_", "i_a_", "i_c_", "i_b_" };
        private static readonly string[] DockCategory = { "dock_S", "dock_A", "dock_C", "dock_B" };
        private static readonly string[] TypeName = { "velocidade", "aceleracao", "durabilidade", "booster" };

        // Peca "representante" de cada grau (mesmo numero nas 4 familias): N = Riddle, S = Myth, H = Enigma, U = Gaia.
        private static string PartNumber(string tier)
        {
            switch (tier)
            {
                case "N": return "000013";
                case "S": return "000014";
                case "H": return "000015";
                default: return "000016";
            }
        }

        // Grau da recompensa de nivel (null = esse nivel nao da recompensa): o grau que voce ja pode equipar
        // (docks S pedem nivel 31, H 60, U 90): 10-30 N, 40-50 S, 60-80 H, 90+ U.
        public static string LevelTier(int level)
        {
            if (level < 10 || level % 10 != 0)
                return null;
            if (level < 31) return "N";
            if (level < 60) return "S";
            if (level < 90) return "H";
            return "U";
        }

        // a melhor peca de performance dessa familia (i_s_, i_a_, i_c_, i_b_) que voce ja pode equipar no nivel
        // (ex.: nivel 10 = Boxer, 20 = Spector, 30 = Riddle, 40 = Winner, 50 = Ghost, 60 = Hermes ...)
        static string BestPart(string prefix, int level)
        {
            var rx = new System.Text.RegularExpressions.Regex("^" + prefix + @"0000\d\d$");
            ItemTableEntry best = null; int bestLv = -1;
            foreach (var g in ItemTable.Items)
            {
                var e = g as ItemTableEntry;
                if (e == null || e.ID == null || !rx.IsMatch(e.ID)) continue;
                int lv; if (!int.TryParse(e.MinLevel, out lv) || lv > level) continue;
                if (lv > bestLv || (lv == bestLv && best != null && e.BasePoints > best.BasePoints)) { best = e; bestLv = lv; }
            }
            return best != null ? best.ID : null;
        }

        public static long LevelMito(string tier)
        {
            var cfg = RiceServer.Config;
            switch (tier)
            {
                case "N": return cfg.LevelRewardMitoN;
                case "S": return cfg.LevelRewardMitoS;
                default: return cfg.LevelRewardMitoU;
            }
        }

        // Entrega o pacote de nivel: mito + 4 docks (grau A) + 4 pecas do grau.
        public static void GrantLevelPack(Character c, int level)
        {
            string tier = LevelTier(level);
            if (tier == null)
                return;

            // mito: LevelRewardMitoStep a cada 10 niveis (10 = 50.000, 20 = 100.000, 30 = 150.000...)
            long mito = RiceServer.Config.LevelRewardMitoStep > 0 ? RiceServer.Config.LevelRewardMitoStep * (level / 10) : LevelMito(tier);
            c.GrantMito((ulong) mito);

            int given = 0;
            // opcao da fileira deste pacote: Turbo Start ou Drift Booster
            string rowAssist = Rng.Next(2) == 0 ? TurboStart : DriftBooster;
            var lines = new List<string>();
            var parts = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                Item it;
                if (c.GrantItem(DockId(i, tier, 1), 1, out it) && it != null)
                {
                    given++;
                    MarkLevelDock(c, it, rowAssist);
                    lines.Add(it.itemEntry.Name + " +" + it.upgradePoint);
                }
                string partId = BestPart(PartPrefix[i], Math.Max(level, c.Level)) ?? (PartPrefix[i] + PartNumber(tier));
                if (c.GrantItem(partId, 1, out it) && it != null)
                {
                    given++;
                    parts.Add(it.itemEntry.Name);
                    string qt; PartCrafting.RollQuality(c, it, out qt);
                    if (qt != null) lines.Add(qt);
                }
            }

            Log.WriteLine("Recompensa do nivel {0} ({1}): +{2} mito e {3} itens para {4}.", level, tier, mito, given, c.Name);
            c.PendingNotices.Add(string.Format(
                "Nivel {0}! Recompensa: +{1} mito, 4 docks {2} e 4 pecas que voce ja pode equipar: {3}.",
                level, mito, tier, string.Join(", ", parts)));
            // Mittron Enhancers do grau do pacote (N, S; o pacote U da o H)
            int nEnh = Math.Max(0, RiceServer.Config.LevelRewardEnhancers);
            if (nEnh > 0)
            {
                string enh = tier == "N" ? "i_n_00043" : tier == "S" ? "i_n_00044" : "i_n_00045";   // N, S, H (U = H)
                Item eit;
                if (c.GrantItem(enh, (uint) nEnh, out eit) && eit != null)
                    lines.Add(nEnh + "x " + eit.itemEntry.Name);
            }
            foreach (var l in lines) c.PendingNotices.Add(l);
            c.PendingNotices.Add(string.Format("Docks de nivel: os 4 na mesma fileira do carro dao +{0} em cada atributo e {1}.",
                RiceServer.Config.DockRowBonus, rowAssist == TurboStart ? "Turbo Start" : "Drift Booster"));

            if (level == 100 && RiceServer.Config.Level100CarSort > 0)
            {
                Vehicle veh;
                Item key;
                if (!c.Garage.Any(v => v.CarType == RiceServer.Config.Level100CarSort)
                    && c.GrantVehicle(RiceServer.Config.Level100CarSort, out veh, out key, 0, 1))
                {
                    c.PendingNotices.Add("Nivel 100! Voce ganhou o carro Shadow Xmas (Lamborghini Reventon). Reentre no jogo para ve-lo na garagem.");
                    Log.WriteLine("Nivel 100: carro {0} entregue a {1}.", RiceServer.Config.Level100CarSort, c.Name);
                }
            }
        }

        // Pacotes de todos os niveis "redondos" entre oldLevel (exclusivo) e newLevel (inclusivo).
        public static void OnLevelUp(Character c, int oldLevel, int newLevel)
        {
            if (!RiceServer.Config.LevelRewardsEnabled)
                return;
            for (int lv = oldLevel + 1; lv <= newLevel; lv++)
                GrantLevelPack(c, lv);
        }

        // ---- Docks de nivel (fileira) ----
        // Ficam em level_docks.json ("CID:invIdx" -> opcao a_00002 Turbo Start / a_00247 Drift Booster).
        static readonly Random Rng = new Random();
        const string TurboStart = "a_00002", DriftBooster = "a_00247";
        const string LevelDocksFile = "level_docks.json";
        static readonly object DockSync = new object();
        static Dictionary<string, string> levelDocks;
        static readonly int[][] Rows = { new[] { 100, 103, 106, 109 }, new[] { 101, 104, 107, 110 } };

        static Dictionary<string, string> LevelDocks()
        {
            if (levelDocks != null) return levelDocks;
            levelDocks = new Dictionary<string, string>();
            try
            {
                if (CharFiles.Exists(LevelDocksFile))
                    levelDocks = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(
                        CharFiles.ReadAllText(LevelDocksFile)) ?? new Dictionary<string, string>();
            }
            catch (Exception ex) { Log.WriteError("level_docks.json invalido: " + ex.Message); }
            return levelDocks;
        }

        static string Key(Character c, Item it) { return c.CID + ":" + it.invIdx; }

        static void MarkLevelDock(Character c, Item it, string assist)
        {
            lock (DockSync)
            {
                LevelDocks()[Key(c, it)] = assist;
                try { CharFiles.WriteAllText(LevelDocksFile, Newtonsoft.Json.JsonConvert.SerializeObject(levelDocks)); }
                catch (Exception) { }
            }
        }

        // Primeira vez com esse sistema: os docks letra A (001) que o corredor ja tem viram docks de nivel
        // (um sorteio de opcao por grau). So roda uma vez por corredor.
        public static void MigrateLevelDocks(Character c)
        {
            if (c == null || c.Inventory == null) return;
            string init = c.CID + ":init";
            lock (DockSync)
            {
                if (LevelDocks().ContainsKey(init)) return;
                levelDocks[init] = "1";
            }
            var pick = new Dictionary<string, string>();
            int n = 0;
            foreach (var it in c.Inventory.Where(i => i.ID != null && System.Text.RegularExpressions.Regex.IsMatch(i.ID, @"^dock_[SACB]_[NSHU]001$")).ToList())
            {
                string tier = it.ID.Substring(7, 1);
                if (!pick.ContainsKey(tier)) pick[tier] = Rng.Next(2) == 0 ? TurboStart : DriftBooster;
                MarkLevelDock(c, it, pick[tier]);
                n++;
            }
            if (n == 0) lock (DockSync) { try { CharFiles.WriteAllText(LevelDocksFile, Newtonsoft.Json.JsonConvert.SerializeObject(levelDocks)); } catch (Exception) { } }
            if (n > 0) Log.WriteLine("Docks de nivel: {0} dock(s) letra A de {1} marcados.", n, c.Name);
        }

        // Conta admin: as duas fileiras de docks U no carro dele, uma com cada opcao (Turbo Start / Drift Booster).
        // Usa o mesmo sistema dos docks de nivel (a opcao vai no dock de velocidade da fileira completa).
        public static void SetAdminDockRows(long cid, int carId, string[] assists)
        {
            try
            {
                int made = 0;
                for (int r = 0; r < Rows.Length && r < assists.Length; r++)
                {
                    string assist = assists[r];
                    if (string.IsNullOrEmpty(assist) || AssistTable.GetIndex(assist) <= 0) { Log.WriteError("Opcao invalida no UppedCarAssists: " + assist); continue; }
                    for (int t = 0; t < 4; t++)
                    {
                        short slot = (short) Rows[r][t];
                        string id = DockId(t, "U", 1);
                        int invIdx = 0;
                        using (var rc = Database.GetContext())
                        {
                            var have = rc.Items.FirstOrDefault(i => i.CID == cid && i.State == 1 && i.CurCarID == carId && i.Slot == slot && i.ItemID.StartsWith("dock_"));
                            if (have != null) invIdx = have.InvIdx;
                        }
                        if (invIdx == 0)
                        {
                            var it = Item.CreateEntry((ulong) cid, id, 1, carId: carId);
                            if (it == null) continue;
                            invIdx = (int) it.invIdx;
                            using (var rc = Database.GetContext())
                            {
                                var db = rc.Items.SingleOrDefault(i => i.CID == cid && i.InvIdx == invIdx);
                                if (db != null) { db.Slot = slot; db.State = 1; db.CurCarID = carId; rc.SaveChanges(); }
                            }
                            made++;
                        }
                        lock (DockSync) LevelDocks()[cid + ":" + invIdx] = assist;
                    }
                }
                lock (DockSync)
                {
                    levelDocks[cid + ":init"] = "1";   // a migracao dos docks letra A nao sorteia por cima
                    CharFiles.WriteAllText(LevelDocksFile, Newtonsoft.Json.JsonConvert.SerializeObject(levelDocks));
                }
                if (made > 0) Log.WriteLine("Perfil upado: {0} dock(s) U no carro (Turbo Start e Drift Booster).", made);
            }
            catch (Exception ex) { Log.WriteError("Docks do admin: " + ex.Message); }
        }

        // opcao do dock de nivel (null = nao e dock de nivel)
        public static string LevelDockAssist(Character c, Item it)
        {
            if (it == null || it.itemEntry == null || it.itemEntry.Category == null || !it.itemEntry.Category.StartsWith("dock_"))
                return null;
            lock (DockSync)
            {
                string a;
                return LevelDocks().TryGetValue(Key(c, it), out a) ? a : null;
            }
        }

        // fileiras completas de docks de nivel no carro: cada uma = 4 docks (um em cada quadrado da fileira)
        public static List<List<Item>> CompleteDockRows(Character c, int carId)
        {
            var res = new List<List<Item>>();
            if (!RiceServer.Config.DockRowEnabled || c == null || c.Inventory == null) return res;
            var docks = c.Inventory.Where(i => i.state == 1 && i.curCarID == (uint) carId && LevelDockAssist(c, i) != null).ToList();
            foreach (var row in Rows)
            {
                var got = new List<Item>();
                foreach (int s in row)
                {
                    var d = docks.FirstOrDefault(i => i.slot == s);
                    if (d == null) break;
                    got.Add(d);
                }
                if (got.Count == row.Length) res.Add(got);
            }
            return res;
        }

        // poe a opcao (Turbo Start / Drift Booster) no dock de velocidade de cada fileira completa e tira dos outros
        public static void UpdateDockRows(Character c, RiceClient client = null)
        {
            if (c == null || c.Inventory == null) return;
            var want = new Dictionary<Item, uint>();
            var passives = new Dictionary<int, HashSet<int>>();
            foreach (var car in c.Garage)
                foreach (var row in CompleteDockRows(c, car.CarID))
                {
                    var first = row[0];
                    string assist = LevelDockAssist(c, first);
                    want[first] = (uint) Math.Max(0, AssistTable.GetIndex(assist));
                    int puid = Rice.Server.Packets.Game.VisualShop.PassiveUidOf(assist);
                    if (puid > 0)
                    {
                        HashSet<int> set;
                        if (!passives.TryGetValue(car.CarID, out set)) { set = new HashSet<int>(); passives[car.CarID] = set; }
                        set.Add(puid);
                    }
                }
            // O jogo mostra a opcao no dock, mas so LIGA o Turbo Start / Drift Booster pelo item "PassiveAssist" da
            // Drift Shop (categoria 41) no carro: a fileira completa poe esse item no carro (e tira quando desfaz).
            try { Rice.Server.Packets.Game.VisualShop.SyncDockPassives(c, passives, client); }
            catch (Exception ex) { Log.WriteError("Fileira de docks (passiva): " + ex.Message); }
            // A opcao vai no PRIMEIRO espaco de opcao (o jogo liga Turbo Start / Drift Booster pelos espacos normais; no
            // terceiro espaco, usado desde a Atualizacao 79, ela nao funcionava no carro). Se o primeiro espaco ja tiver
            // outra opcao (Convert), vai no terceiro como antes.
            uint ts = (uint) Math.Max(0, AssistTable.GetIndex(TurboStart)), db = (uint) Math.Max(0, AssistTable.GetIndex(DriftBooster));
            foreach (var it in c.Inventory.ToList())
            {
                if (LevelDockAssist(c, it) == null) continue;
                uint v; if (!want.TryGetValue(it, out v)) v = 0;
                bool aFree = it.assistA == 0 || it.assistA == ts || it.assistA == db;
                uint wantA = aFree ? v : it.assistA;
                uint wantC = aFree ? 0 : v;
                if (it.assistC != 0 && it.assistC != ts && it.assistC != db) wantC = it.assistC;   // outra opcao no C: fica
                if (it.assistA != wantA || it.assistC != wantC)
                {
                    it.assistA = wantA; it.assistC = wantC;
                    c.SaveItemState(it);
                }
            }
        }

        // id do dock: dock_S_N001 (tipo = indice 0..3, grau N/S/H/U, letra 1 = A).
        public static string DockId(int typeIndex, string tier, int letter)
        {
            return string.Format("{0}_{1}{2:000}", DockCategory[typeIndex], tier, letter);
        }

        static bool Roll(double chance) { lock (Rng) return chance > 0 && Rng.NextDouble() < chance; }

        // Components (categoria "piece"): com 10 do mesmo tipo no inventario, tira os 10 e da o item do "nextstate"
        // (Crude Speed/Accel./Dura./Boost Part = peca H incompleta; completa com Mittron Enhancer no Modify).
        // Devolve o nome do item novo, ou null.
        public static string CombinePieces(Character c, string id)
        {
            if (c == null || c.Inventory == null || string.IsNullOrEmpty(id)) return null;
            var stacks = c.Inventory.Where(i => i.ID == id && i.state != 1).ToList();
            long total = stacks.Sum(i => (long) Math.Max(1u, i.stackNum));
            if (total < 10 || stacks.Count == 0) return null;
            string next = stacks[0].itemEntry != null ? stacks[0].itemEntry.NextState : null;
            if (string.IsNullOrEmpty(next) || next == "n/a") return null;
            uint need = 10;
            foreach (var st in stacks.OrderBy(i => i.stackNum))
            {
                if (need == 0) break;
                uint take = Math.Min(need, Math.Max(1u, st.stackNum));
                Item gone;
                if (c.DropItem((int) st.invIdx, out gone, take, true)) need -= take;
            }
            Item made;
            if (!c.GrantItem(next, 1, out made) || made == null) return null;
            Log.WriteLine("Components: {0} juntou 10 {1} -> {2}.", c.Name, id, next);
            return made.itemEntry.Name;
        }

        // ---- Combo ----
        // Itens do combo: protetor de booster a cada marco de 20 e enhancer por faixa.
        // blocks = quantos blocos de 20 combos seguidos (combo / 20).
        // devolve o item para a animacao "Get Item" (o extra mais raro que saiu; senao o Booster Protector)
        public static Item GrantComboItems(Character c, int combo, int blocks)
        {
            var cfg = RiceServer.Config;
            Item it;
            Item show = null;

            // Protetor de booster: alterna F (i_n_00015) e D (i_n_00016).
            string protector = (blocks % 2 == 1) ? "i_n_00015" : "i_n_00016";
            if (c.GrantItem(protector, 1, out it) && it != null)
            {
                show = it;
                c.PendingNotices.Add(string.Format("Combo {0}: ganhou 1 {1}.", combo, it.itemEntry.Name));
            }

            // Mittron Enhancer por faixa: N 100+, S 200+, H 300+, "U" 400+ (nao existe U: H DELUXE, ComboEnhancerUItem)
            string enhancer = null;
            if (combo >= cfg.ComboEnhancerUAt) enhancer = string.IsNullOrEmpty(cfg.ComboEnhancerUItem) ? "i_n_00045" : cfg.ComboEnhancerUItem;
            else if (combo >= cfg.ComboEnhancerHAt) enhancer = "i_n_00045";
            else if (combo >= cfg.ComboEnhancerSAt) enhancer = "i_n_00044";
            else if (combo >= cfg.ComboEnhancerNAt) enhancer = "i_n_00043";

            // Cada marco (multiplo de 20) dentro da faixa entrega o enhancer da faixa.
            if (enhancer != null)
            {
                if (c.GrantItem(enhancer, 1, out it))
                    c.PendingNotices.Add(string.Format("Combo {0}: ganhou 1 {1}.", combo, it.itemEntry.Name));
            }

            // Itens extras (cada um com a sua chance, 30% por padrao, em todo marco de 20): Mittron Crystal Powder (Item
            // Exchange das cidades), um Component H (10 = peca H incompleta), Auto-Driver e 3 Damage Booster do seu grau
            // (N ate o 30, S 31-60, H 61+). O Booster Protector (acima) sai sempre.
            if (!string.IsNullOrEmpty(cfg.ComboPowderItem) && Roll(cfg.ComboPowderChance) && c.GrantItem(cfg.ComboPowderItem, 1, out it) && it != null)
            {
                show = it;
                c.PendingNotices.Add(string.Format("Combo {0}: ganhou 1 {1}.", combo, it.itemEntry.Name));
            }
            var comps = cfg.ComboComponentItems ?? new string[0];
            string comp = null;
            lock (Rng) if (comps.Length > 0) comp = comps[Rng.Next(comps.Length)];
            if (comp != null && Roll(cfg.ComboComponentChance) && c.GrantItem(comp, 1, out it) && it != null)
            {
                show = it;
                c.PendingNotices.Add(string.Format("Combo {0}: ganhou 1 {1}.", combo, it.itemEntry.Name));
                string made = CombinePieces(c, comp);       // juntou 10: vira a peca H incompleta sozinho
                if (made != null) c.PendingNotices.Add("10 " + it.itemEntry.Name + " viraram 1 " + made + "!");
            }
            if (!string.IsNullOrEmpty(cfg.ComboAutoDriveItem) && Roll(cfg.ComboAutoDriveChance) && c.GrantItem(cfg.ComboAutoDriveItem, 1, out it) && it != null)
            {
                show = it;
                c.PendingNotices.Add(string.Format("Combo {0}: ganhou 1 {1}.", combo, it.itemEntry.Name));
            }
            if (cfg.ComboDamageCount > 0 && Roll(cfg.ComboDamageChance))
            {
                string dmg = c.Level <= 30 ? "i_n_00093" : c.Level <= 60 ? "i_n_00094" : "i_n_00095";
                if (c.GrantItem(dmg, (uint) cfg.ComboDamageCount, out it) && it != null)
                {
                    show = it;
                    c.PendingNotices.Add(string.Format("Combo {0}: ganhou {1}x {2}.", combo, cfg.ComboDamageCount, it.itemEntry.Name));
                }
            }
            return show;
        }
    }
}
