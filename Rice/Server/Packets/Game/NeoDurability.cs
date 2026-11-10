using System;
using System.Collections.Generic;
using System.Linq;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Durabilidade dos equipamentos da Neo City (arma, Amplifier, Fuse e armadura).
    // O jogo mostra "Durability : atual/maximo" (GlobalContent 12235) com o campo ItemHealth do item e nao deixa montar
    // item com durabilidade 0 (12233). O maximo vem da tabela de itens: neostats = "<durabilidade maxima> <forca>"
    // (500 / 600 / 700 / 800, sobe com o grau).
    //  - item novo (ItemHealth 1 = valor padrao do servidor): comeca com a durabilidade maxima;
    //  - na Neo City: cada equipamento montado perde NeoDurabilityPerMinute por minuto, a arma perde NeoDurabilityPerShot
    //    por tiro (pacote 555, @0x6c = numero da arma) e a armadura perde NeoDurabilityPerHit por dano tomado (a vida do
    //    carro caiu no 0x21D);
    //  - chegou a 0: o item sai do carro (412, como se voce tirasse) e o jogo nao deixa montar de novo ate reparar;
    //  - reparo: Weapon/Amplifier/Fuse/Armor Repair Kit 10/25/100 (+durabilidade na peca montada desse tipo) e NEO Parts
    //    Repair Kit (todas as pecas da Neo City, ate as quebradas, em 100%). Os kits estao na Drift Shop. Chat:
    //    !!durabilidade mostra a durabilidade das pecas montadas.
    public static class NeoDurability
    {
        static readonly string[] Cats = { "neo_weapon", "neo_coil", "neo_fuse", "neo_armor" };
        const string InitFile = "neo_durability.json";
        static readonly object Sync = new object();
        static HashSet<string> started;          // "cid:invIdx" dos itens que ja receberam a durabilidade maxima
        static System.Threading.Timer timer;
        const int TickMs = 60000;                // desgaste aplicado a cada 1 minuto (tiros e danos do minuto juntos)

        public static bool IsNeo(Rice.Game.Item it)
        {
            return it != null && it.itemEntry != null && Array.IndexOf(Cats, it.itemEntry.Category ?? "") >= 0;
        }

        public static float Max(Rice.Game.Item it)
        {
            var e = it != null ? it.itemEntry as ItemTableEntry : null;
            if (e == null || string.IsNullOrEmpty(e.NeoStats)) return 500f;
            float v;
            var f = e.NeoStats.Trim().Split(' ');
            return float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0 ? v : 500f;
        }

        public static void Start()
        {
            if (timer != null) return;
            timer = new System.Threading.Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("Durabilidade Neo: " + ex.Message); } }, null, TickMs, TickMs);
        }

        static HashSet<string> Started()
        {
            if (started != null) return started;
            started = new HashSet<string>();
            try
            {
                if (CharFiles.Exists(InitFile))
                {
                    var l = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(CharFiles.ReadAllText(InitFile));
                    if (l != null) foreach (var k in l.Keys) started.Add(k);
                }
            }
            catch (Exception ex) { Log.WriteError(InitFile + ": " + ex.Message); }
            return started;
        }

        static void SaveStarted()
        {
            // CharFiles separa por corredor pela chave "cid:..." (save/<conta>/<corredor>/neo_durability.json)
            try { CharFiles.WriteAllText(InitFile, Newtonsoft.Json.JsonConvert.SerializeObject(Started().ToDictionary(k => k, k => 1))); } catch (Exception) { }
        }

        static void SaveHealth(Rice.Game.Character ch, Rice.Game.Item it)
        {
            using (var rc = Rice.Database.GetContext())
            {
                var db = rc.Items.SingleOrDefault(i => i.CID == (long) ch.CID && i.InvIdx == (int) it.invIdx);
                if (db == null) return;
                db.ItemHealth = it.itemHealth;
                rc.SaveChanges();
            }
            ch.NotifyItem(it);
        }

        // itens da Neo City que ainda nao tem durabilidade: comecam cheios (ao entrar no jogo e a cada minuto)
        public static int Prepare(Rice.Game.Character ch)
        {
            if (ch == null || ch.Inventory == null || !RiceServer.Config.NeoDurabilityEnabled) return 0;
            int n = 0;
            bool changed = false;
            lock (Sync)
                foreach (var it in ch.Inventory.Where(IsNeo).ToList())
                {
                    string key = ch.CID + ":" + it.invIdx;
                    if (Started().Contains(key)) continue;
                    Started().Add(key); changed = true;
                    float max = Max(it);
                    if (it.itemHealth <= 1.001f || it.itemHealth > max) { it.itemHealth = max; SaveHealth(ch, it); n++; }
                }
            if (changed) lock (Sync) SaveStarted();
            return n;
        }

        static List<Rice.Game.Item> Mounted(Rice.Game.Character ch)
        {
            if (ch == null || ch.Vehicle == null || ch.Inventory == null) return new List<Rice.Game.Item>();
            uint car = (uint) ch.Vehicle.CarID;
            return ch.Inventory.Where(i => IsNeo(i) && i.state == 1 && i.curCarID == car).ToList();
        }

        // gasta durabilidade; no 0 tira do carro
        static void Wear(RiceClient c, Rice.Game.Character ch, Rice.Game.Item it, double amount)
        {
            if (amount <= 0 || it.itemHealth <= 0) return;
            float before = it.itemHealth;
            it.itemHealth = (float) Math.Max(0, Math.Round(it.itemHealth - amount, 2));
            SaveHealth(ch, it);
            float max = Max(it);
            string name = it.itemEntry != null ? it.itemEntry.Name : it.ID;
            if (it.itemHealth <= 0)
            {
                int carId = (int) it.curCarID;
                if (ch.UnEquipItem((int) it.invIdx, carId) && c != null)
                {
                    var ack = new RicePacket(412);
                    ack.Writer.Write(it.invIdx); ack.Writer.Write((uint) carId);
                    c.Send(ack);
                }
                if (c != null) Chat.SendSystemMessage(c, string.Format("[Neo City] {0} quebrou (durabilidade 0) e saiu do carro. Use um Repair Kit da Neo City (Drift Shop) para consertar.", name));
                Log.WriteLine("Durabilidade Neo: {0} - {1} quebrou.", ch.Name, name);
            }
            else if (c != null && before > max * 0.1f && it.itemHealth <= max * 0.1f)
                Chat.SendSystemMessage(c, string.Format("[Neo City] {0} esta quase quebrando: durabilidade {1:0}/{2:0}.", name, it.itemHealth, max));
        }

        static void Tick()
        {
            var cfg = RiceServer.Config;
            if (!cfg.NeoDurabilityEnabled) return;
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                var gc = p != null ? p.GameClient : null;
                if (ch == null) continue;
                try
                {
                    Prepare(ch);
                    if (ch.Area == null || !NeoCity.IsNeoArea(ch.Area.ID)) continue;
                    var mounted = Mounted(ch);
                    // tiros desde a ultima vez: cada arma perde NeoDurabilityPerShot por tiro
                    Dictionary<uint, int> fired;
                    lock (shots) { shots.TryGetValue(ch.CID, out fired); shots.Remove(ch.CID); }
                    var extra = new Dictionary<Rice.Game.Item, double>();
                    // danos tomados: a armadura perde NeoDurabilityPerHit por dano
                    int took;
                    lock (hits) { hits.TryGetValue(ch.CID, out took); hits.Remove(ch.CID); }
                    if (took > 0 && cfg.NeoDurabilityPerHit > 0)
                        foreach (var arm in mounted.Where(i => i.itemEntry.Category == "neo_armor"))
                        {
                            double v0; extra.TryGetValue(arm, out v0); extra[arm] = v0 + took * cfg.NeoDurabilityPerHit;
                        }
                    if (fired != null && cfg.NeoDurabilityPerShot > 0)
                    {
                        var guns = mounted.Where(i => i.itemEntry.Category == "neo_weapon").ToList();
                        foreach (var kv in fired)
                        {
                            var gun = guns.FirstOrDefault(i => { var e = i.itemEntry as ItemTableEntry; return e != null && (uint) e.BasePoints == kv.Key; }) ?? guns.FirstOrDefault();
                            if (gun == null) continue;
                            double v; extra.TryGetValue(gun, out v); extra[gun] = v + kv.Value * cfg.NeoDurabilityPerShot;
                        }
                    }
                    foreach (var it in mounted)
                    {
                        double add; extra.TryGetValue(it, out add);
                        Wear(gc, ch, it, cfg.NeoDurabilityPerMinute * TickMs / 60000.0 + add);
                    }
                    if (gc != null) ch.FlushModInfo(gc);
                }
                catch (Exception ex) { Log.WriteError("Durabilidade Neo (" + ch.Name + "): " + ex.Message); }
            }
        }

        // tiro da arma do carro (555, @0x6c = numero da arma): conta; o desgaste vai junto no minuto (sem gravar a cada tiro)
        static readonly Dictionary<ulong, Dictionary<uint, int>> shots = new Dictionary<ulong, Dictionary<uint, int>>();
        public static void OnShot(Rice.Game.Character ch, uint weaponId)
        {
            if (ch == null || !RiceServer.Config.NeoDurabilityEnabled) return;
            lock (shots)
            {
                Dictionary<uint, int> d;
                if (!shots.TryGetValue(ch.CID, out d)) shots[ch.CID] = d = new Dictionary<uint, int>();
                int n; d.TryGetValue(weaponId, out n); d[weaponId] = n + 1;
            }
        }

        // vida do seu carro no estado do carro (0x21D MoveVehicle, XiCarState @0x3a: vida atual float @+0x2e): na Neo City,
        // cada vez que ela cai = um dano tomado (a armadura gasta no proximo Tick). Kit de vida / volta da base sobe: so atualiza.
        static readonly Dictionary<ulong, float> lastHp = new Dictionary<ulong, float>();
        static readonly Dictionary<ulong, int> hits = new Dictionary<ulong, int>();
        public static void OnMove(Rice.Game.Character ch, byte[] buf)
        {
            if (ch == null || buf == null || buf.Length < 0x3a + 0x32 || !RiceServer.Config.NeoDurabilityEnabled) return;
            if (ch.Area == null || !NeoCity.IsNeoArea(ch.Area.ID)) { lock (lastHp) lastHp.Remove(ch.CID); return; }
            float hp = BitConverter.ToSingle(buf, 0x3a + 0x2e);
            if (float.IsNaN(hp) || hp < 0 || hp > 10000000f) return;
            lock (lastHp)
            {
                float last;
                if (lastHp.TryGetValue(ch.CID, out last) && hp < last - 0.5f)
                    lock (hits) { int n; hits.TryGetValue(ch.CID, out n); hits[ch.CID] = n + 1; }
                lastHp[ch.CID] = hp;
            }
        }

        // Repair Kits da Neo City (duplo clique no item). true = era um kit (tratado aqui)
        static readonly Dictionary<string, string> KitFor = new Dictionary<string, string>
        {
            { "repair_waepon", "neo_weapon" }, { "repair_weapon", "neo_weapon" }, { "repair_coil", "neo_coil" },
            { "repair_fusion", "neo_fuse" }, { "repair_armor", "neo_armor" }, { "repair_neoparts", "*" }
        };
        public static bool UseKit(RiceClient c, Rice.Game.Character ch, Rice.Game.Item kit, int invIdx)
        {
            string cat = kit.itemEntry != null ? kit.itemEntry.Category ?? "" : "";
            string target;
            if (!KitFor.TryGetValue(cat, out target)) return false;
            Prepare(ch);
            var fixedNames = new List<string>();
            if (target == "*")
            {
                // NEO Parts Repair Kit: todas as pecas da Neo City (montadas ou no inventario, ate as quebradas) em 100%
                foreach (var it in ch.Inventory.Where(IsNeo).ToList())
                {
                    float max = Max(it);
                    if (it.itemHealth >= max) continue;
                    it.itemHealth = max; SaveHealth(ch, it);
                    fixedNames.Add(it.itemEntry.Name);
                }
            }
            else
            {
                var ue = kit.itemEntry as UseItemTableEntry;
                float add;
                if (ue == null || !float.TryParse(ue.StatModifier, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out add) || add <= 0) add = 10;
                var it = Mounted(ch).FirstOrDefault(i => i.itemEntry.Category == target);
                if (it == null)
                {
                    Chat.SendSystemMessage(c, "[Neo City] " + kit.itemEntry.Name + ": monte no carro a peca desse tipo para reparar.");
                    return true;
                }
                float max = Max(it);
                if (it.itemHealth >= max) { Chat.SendSystemMessage(c, "[Neo City] " + it.itemEntry.Name + " ja esta com a durabilidade cheia."); return true; }
                it.itemHealth = Math.Min(max, it.itemHealth + add); SaveHealth(ch, it);
                fixedNames.Add(string.Format("{0} ({1:0}/{2:0})", it.itemEntry.Name, it.itemHealth, max));
            }
            if (fixedNames.Count == 0) { Chat.SendSystemMessage(c, "[Neo City] Nada para reparar: as pecas estao com a durabilidade cheia."); return true; }
            Rice.Game.Item used;
            ch.DropItem(invIdx, out used, 1, true);
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, "[Neo City] Reparado: " + string.Join(", ", fixedNames) + ".");
            Log.WriteLine("Durabilidade Neo: {0} usou {1}: {2}.", ch.Name, kit.ID, string.Join(", ", fixedNames));
            return true;
        }

        // !!durabilidade
        public static string Report(Rice.Game.Character ch)
        {
            Prepare(ch);
            var l = Mounted(ch);
            if (l.Count == 0) return "Nenhuma peca da Neo City montada no carro.";
            return "Durabilidade: " + string.Join(" | ", l.Select(i => string.Format("{0} {1:0}/{2:0}", i.itemEntry.Name, i.itemHealth, Max(i))));
        }
    }
}
