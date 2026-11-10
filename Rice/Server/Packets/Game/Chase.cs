using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Perseguicao de HUV. Tabela de HUVs (HuvTable.tdf do cliente) em res/HuvTable.json, embutida no Rice.exe.
    // Fluxo (layouts conforme a documentacao do emulador openDC, implementado aqui do zero):
    //   189 ChaseRequest (u8 agora, XiVec4 pos) -> 186 ChasePropose (o jogo mostra o chamado do HUV)
    //   180 ChaseBegin (i32 tipo: 0 chamado / >0 missao, XiVec4, i32 nivel, i32 id, i32 quantidade) -> 181 ChaseBeginConfirm
    //   185 ChaseProgress (eco) ; 187 ChaseHit ; 183 ChaseEnd (um HUV pego/fugiu, ou serial 0 = desistiu) -> 184 ChaseResult
    public static class Chase
    {
        class Huv
        {
            public int Level, Id, Ea, Exp, Money;
            public string Name;
            public double Rate;
            public float Speed, MaxSpeed, NosAccel, NosTime, NosRefresh, Durability, AvoidFrontPlayer, AvoidFrontTraffic, AvoidRearPlayer;
        }

        class State
        {
            public Dictionary<int, Huv> Huvs = new Dictionary<int, Huv>();   // sort -> HUV
            public int Exp, Money, Caught;
            public int Total;                 // quantos HUVs a perseguicao tinha
            public long AllExp, AllMoney;     // recompensa somada de todos os HUVs (100%)
        }

        static readonly int[] FindMinutes = { 2, 3, 4, 5, 5, 5, 5, 5, 5, 5 };
        static List<Huv> table;
        static readonly Dictionary<RiceClient, State> chases = new Dictionary<RiceClient, State>();
        static readonly Random Rng = new Random();

        static List<Huv> Table()
        {
            if (table != null) return table;
            table = new List<Huv>();
            try
            {
                string json = null;
                string[] paths = { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "HuvTable.json"), "res/HuvTable.json" };
                foreach (var p in paths) if (File.Exists(p)) { json = File.ReadAllText(p); break; }
                if (json == null)
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("HuvTable.json"))
                    using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                foreach (var h in JArray.Parse(json))
                    table.Add(new Huv
                    {
                        Level = (int) h["level"], Id = (int) h["id"], Name = (string) h["name"], Rate = (double) h["rate"],
                        Ea = (int) h["ea"], Exp = (int) h["exp"], Money = (int) h["money"],
                        Speed = (float) h["speed"], MaxSpeed = (float) h["maxSpeed"], NosAccel = (float) h["nosAccel"],
                        NosTime = (float) h["nosTime"], NosRefresh = (float) h["nosRefresh"], Durability = (float) h["durability"],
                        AvoidFrontPlayer = (float) h["avoidFrontPlayer"], AvoidFrontTraffic = (float) h["avoidFrontTraffic"],
                        AvoidRearPlayer = (float) h["avoidRearPlayer"]
                    });
                Log.WriteLine("HUV: {0} linhas na tabela.", table.Count);
            }
            catch (Exception ex) { Log.WriteError("HUV: nao consegui ler HuvTable.json: " + ex.Message); }
            return table;
        }

        // HUVs do maior nivel da tabela que nao passa do nivel pedido; so os "normais" (taxas somando 100).
        static Huv Pick(int level)
        {
            var t = Table().Where(h => h.Level > 0).ToList();
            if (t.Count == 0) return null;
            var below = t.Where(h => h.Level <= level).ToList();
            int lv = below.Count > 0 ? below.Max(h => h.Level) : t.Min(h => h.Level);
            var rows = new List<Huv>();
            double total = 0;
            foreach (var h in t.Where(h => h.Level == lv))
                if (h.Rate > 0 && total < 100) { rows.Add(h); total += h.Rate; }
            if (rows.Count == 0) return null;
            double roll = Rng.NextDouble() * rows.Sum(h => h.Rate);
            foreach (var h in rows) { roll -= h.Rate; if (roll <= 0) return h; }
            return rows[rows.Count - 1];
        }

        static Huv Find(int level, int id)
        {
            var same = Table().Where(h => h.Id == id).ToList();
            if (same.Count == 0) return null;
            return same.FirstOrDefault(h => h.Level == level) ?? same.OrderBy(h => Math.Abs(h.Level - level)).First();
        }

        // exp/mito do HUV na tabela (usado pela Undercity)
        public static bool HuvReward(int level, int id, out int exp, out int money)
        {
            var h = Find(level, id);
            exp = h != null ? h.Exp : 0;
            money = h != null ? h.Money : 0;
            return h != null;
        }

        static int HuvLevel(Rice.Game.Character c)
        {
            return Math.Max(1, (int) Math.Round(c.Level * RiceServer.Config.HuvLevelFactor));
        }

        [RicePacket(189, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseRequest(RicePacket packet)
        {
            var p = packet.Buffer;
            var ch = packet.Sender.Player.ActiveCharacter;
            bool now = p.Length > 0 && p[0] != 0;
            lock (chases) if (chases.ContainsKey(packet.Sender)) return;   // ja numa perseguicao
            var h = Pick(HuvLevel(ch));
            if (h == null) return;
            Log.WriteLine("ChaseRequest: chamado do HUV {0} ({1}) nivel {2}.", h.Name, h.Id, h.Level);
            var ack = new RicePacket(186);
            ack.Writer.Write(ch.Serial);
            for (int i = 0; i < 8; i++) ack.Writer.Write(0f);
            ack.Writer.Write(now ? 1 : 2);
            ack.Writer.Write(0);
            ack.Writer.Write(h.Level);
            ack.Writer.Write(h.Id);
            var text = new byte[200];
            var e = Encoding.Unicode.GetBytes("test");
            Array.Copy(e, text, e.Length);
            ack.Writer.Write(text);
            packet.Sender.Send(ack);
        }

        [RicePacket(180, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseBegin(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 32) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            int kind = BitConverter.ToInt32(p, 0);
            float x = BitConverter.ToSingle(p, 4), y = BitConverter.ToSingle(p, 8), z = BitConverter.ToSingle(p, 12), w = BitConverter.ToSingle(p, 16);
            int level = BitConverter.ToInt32(p, 20), id = BitConverter.ToInt32(p, 24), asked = BitConverter.ToInt32(p, 28);

            var h = Find(level, id);
            var ack = new RicePacket(181);
            ack.Writer.Write(x); ack.Writer.Write(y); ack.Writer.Write(z); ack.Writer.Write(w);
            if (h == null)
            {
                Log.WriteError("ChaseBegin: HUV " + id + " nivel " + level + " nao existe na tabela.");
                ack.Writer.Write((byte) 0); ack.Writer.Write(0); ack.Writer.Write(0); ack.Writer.Write((ushort) 0);
                packet.Sender.Send(ack);
                return;
            }
            var cfg = RiceServer.Config;
            bool quest = kind > 0;
            int lo = Math.Max(1, cfg.HuvCountMin), hi = Math.Max(lo, cfg.HuvCountMax);
            int n = quest ? Math.Max(1, Math.Min(asked, 12)) : Rng.Next(lo, hi + 1);
            int timeout = FindMinutes[Math.Min(n, 10) - 1] * 60;
            var st = new State();
            ack.Writer.Write((byte) 1);
            ack.Writer.Write(timeout);
            ack.Writer.Write(timeout);
            ack.Writer.Write((ushort) n);
            for (int k = 0; k < n; k++)
            {
                var row = (quest || k == 0) ? h : (Pick(HuvLevel(ch)) ?? h);
                int sort = k + 1;
                st.Huvs[sort] = row;
                st.Total++; st.AllExp += row.Exp; st.AllMoney += row.Money;
                ack.Writer.Write(ch.Serial);
                ack.Writer.Write((ushort) sort);
                ack.Writer.Write(row.Level);
                ack.Writer.Write(row.Id);
                ack.Writer.Write(row.Speed); ack.Writer.Write(row.MaxSpeed); ack.Writer.Write(row.NosAccel);
                ack.Writer.Write(row.NosTime); ack.Writer.Write(row.NosRefresh); ack.Writer.Write(row.Durability);
                ack.Writer.Write(row.AvoidFrontPlayer); ack.Writer.Write(row.AvoidFrontTraffic); ack.Writer.Write(row.AvoidRearPlayer);
            }
            lock (chases) chases[packet.Sender] = st;
            Log.WriteLine("ChaseBegin: {0} HUV(s) ({1} nivel {2}){3}, tempo {4} s.", n, h.Name, h.Level, quest ? " de missao" : "", timeout);
            packet.Sender.Send(ack);
        }

        [RicePacket(185, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseProgress(RicePacket packet)
        {
            var p = packet.Buffer;
            var ack = new RicePacket(185);
            ack.Writer.Write(packet.Sender.Player.ActiveCharacter.Serial);
            if (p.Length > 2) ack.Writer.Write(p.Skip(2).ToArray());
            packet.Sender.Send(ack);
        }

        [RicePacket(187, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseHit(RicePacket packet)
        {
            // batida no HUV: pode cair item (HuvHitDropChance, no maximo um a cada HuvHitDropSeconds)
            try
            {
                var cfg = RiceServer.Config;
                var ch = packet.Sender.Player.ActiveCharacter;
                bool inChase; lock (chases) inChase = chases.ContainsKey(packet.Sender);
                if (inChase && cfg.HuvHitDropChance > 0)
                {
                    DateTime last; bool ok;
                    lock (lastDrop)
                    {
                        ok = !lastDrop.TryGetValue(ch.CID, out last) || (DateTime.UtcNow - last).TotalSeconds >= Math.Max(0.5, cfg.HuvHitDropSeconds);
                        if (ok) lastDrop[ch.CID] = DateTime.UtcNow;
                    }
                    if (ok && Rng.NextDouble() < cfg.HuvHitDropChance)
                    {
                        int times = VisualShop.DropCount(ch);             // Item Drink da Drift Shop: itens em dobro
                        for (int t = 0; t < times; t++)
                        {
                            string text;
                            var it = Rice.Game.PartCrafting.HuvDrop(ch, out text);
                            if (it != null)
                            {
                                ch.FlushModInfo(packet.Sender);
                                Misc.GetItemFx(packet.Sender, it);
                                Chat.SendSystemMessage(packet.Sender, (t > 0 ? "[Item Drink] " : "") + "HUV: " + text);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.WriteError("ChaseHit drop: " + ex.Message); }
            // Sozinho nao ha nada para repassar. Registra os primeiros golpes (ajuda a calibrar o dano dos bots).
            if (hitLogs++ < 6)
                Log.WriteLine("ChaseHit (seu): " + BitConverter.ToString(packet.Buffer, 0, Math.Min(packet.Buffer.Length, 44)));
        }
        static int hitLogs;
        static readonly Dictionary<ulong, DateTime> lastDrop = new Dictionary<ulong, DateTime>();

        // Dano de um golpe de bot da party no HUV 'sort' da sua perseguicao: durabilidade x CityHuvHitDamage.
        public static float BotHitDamage(RiceClient c, ushort sort)
        {
            State st;
            lock (chases) chases.TryGetValue(c, out st);
            if (st == null || st.Huvs.Count == 0) return 0f;
            Huv h;
            if (!st.Huvs.TryGetValue(sort, out h)) h = st.Huvs.Values.First();
            float baseHp = h.Durability > 0 ? h.Durability : 100f;
            return (float) (baseHp * RiceServer.Config.CityHuvHitDamage * (0.8 + Rng.NextDouble() * 0.4));
        }

        [RicePacket(183, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseEnd(RicePacket packet)
        {
            var p = packet.Buffer;
            if (p.Length < 16) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            ushort serial = BitConverter.ToUInt16(p, 0), sort = BitConverter.ToUInt16(p, 2);
            // cliente 2016 (0x848744): u16 serial @0, u16 numero @2, u16 tipo @4 e o resultado (u32) em @0xe; os bytes
            // 6..0xd nao sao preenchidos (lixo). Ler em @12 dava 65879 em vez de 1 (pego).
            uint result = p.Length >= 0x12 ? BitConverter.ToUInt32(p, 0xe) : BitConverter.ToUInt32(p, 12);
            if (result > 0xff && p.Length >= 16) result = BitConverter.ToUInt32(p, 12);
            int time = p.Length >= 0x34 ? BitConverter.ToInt32(p, 0x30) : 0;

            Log.WriteLine("ChaseEnd: serial {0}, HUV {1}, resultado {2} ({3}).", serial, sort, result,
                BitConverter.ToString(p, 0, Math.Min(p.Length, 0x14)));
            State st;
            lock (chases) chases.TryGetValue(packet.Sender, out st);
            if (st == null)
            {
                st = new State();
                Log.WriteLine("ChaseEnd: nenhuma perseguicao registrada para {0}.", ch.Name);
            }

            if (serial != 0)
            {
                Huv h;
                // o jogo pode numerar o HUV diferente do que mandamos: sem o numero, conta o primeiro que falta
                if (!st.Huvs.ContainsKey(sort) && st.Huvs.Count > 0)
                {
                    Log.WriteLine("ChaseEnd: HUV {0} nao estava na lista ({1}); contando o {2}.", sort,
                        string.Join(",", st.Huvs.Keys), st.Huvs.Keys.First());
                    sort = (ushort) st.Huvs.Keys.First();
                }
                if (st.Total == 0 && result == 1)
                {
                    // perseguicao que o servidor nao registrou: paga 1 HUV do nivel do jogador
                    var hh = Pick(HuvLevel(ch));
                    if (hh != null) { st.Total = 1; st.Caught = 1; st.AllExp = hh.Exp; st.AllMoney = hh.Money; }
                }
                if (st.Huvs.TryGetValue(sort, out h))
                {
                    st.Huvs.Remove(sort);
                    if (result == 1)
                    {
                        // a recompensa sai no fim da perseguicao (pela parte dos HUVs pegos)
                        st.Caught++;
                        Log.WriteLine("HUV {0} pego ({1} restante(s)).", h.Name, st.Huvs.Count);
                    }
                    else Log.WriteLine("HUV {0} terminou (resultado {1}), {2} restante(s).", h.Name, result, st.Huvs.Count);
                }
                if (st.Huvs.Count > 0) return;
            }
            else
            {
                Log.WriteLine("Perseguicao encerrada por {0} (resultado {1}).", ch.Name, result);
                // o jogo diz que deu certo (resultado 1, "Chase Successful") e o servidor nao viu o HUV ser pego:
                // conta os que faltavam como pegos
                if (result == 1 && st.Huvs.Count > 0)
                {
                    st.Caught += st.Huvs.Count;
                    st.Huvs.Clear();
                }
                else if (result == 1 && st.Total == 0)
                {
                    // perseguicao que o servidor nao registrou: paga 1 HUV do nivel do jogador
                    var hh = Pick(HuvLevel(ch));
                    if (hh != null) { st.Total = 1; st.Caught = 1; st.AllExp = hh.Exp; st.AllMoney = hh.Money; }
                }
            }

            lock (chases) chases.Remove(packet.Sender);
            // Resultado da missao: nenhum HUV = falhou (nada); alguns = meio sucesso (parte k/n); todos = concluida (100%).
            // Bonus da party: +5% por membro (maximo 20%, 4 membros). Licencas dos bots continuam somando.
            string outcome = "falhou";
            if (st.Caught > 0 && st.Total > 0)
            {
                var cfg = RiceServer.Config;
                var party = BotSocial.PartyBots(ch);
                double partyPct = Math.Min(cfg.HuvPartyBonusMax, cfg.HuvPartyBonusEach * party.Count);
                double lic = party.Sum(x => Licenses.BotRewardBonus(x));
                double share = Math.Min(1.0, (double) st.Caught / st.Total);
                double mult = cfg.HuvRewardMultiplier * share * (1.0 + partyPct + lic);
                int exp = (int) (st.AllExp * mult), money = (int) (st.AllMoney * mult);
                st.Exp = exp; st.Money = money;
                if (exp > 0) ch.GrantExperience(exp);
                if (money > 0) ch.GrantMito((ulong) money);
                BotSocial.ShareRewards(ch, exp, money);
                try { Licenses.CountHuv(ch, party.Count > 0); } catch (Exception lex) { Log.WriteError("Licencas (HUV): " + lex.Message); }
                outcome = st.Caught >= st.Total ? "concluida" : "meio sucesso";
                Log.WriteLine("HUV: missao {0} ({1}/{2}), +{3} exp, +{4} mito (party +{5:0}%).", outcome, st.Caught, st.Total, exp, money, partyPct * 100);
            }
            else { st.Exp = 0; st.Money = 0; Log.WriteLine("HUV: missao falhou (nenhum HUV pego)."); }
            if (st.Caught > 0) PartyTalk.Praise(ch, "HUV");
            var ei = ch.GetExpInfo();
            var ack = new RicePacket(184);
            ack.Writer.Write(result);
            ack.Writer.Write(time);
            ack.Writer.Write((ushort) 1);
            ack.Writer.Write(new byte[32]);
            var name = new byte[64];
            var nb = Encoding.Unicode.GetBytes(ch.Name ?? "");
            Array.Copy(nb, name, Math.Min(nb.Length, 62));
            ack.Writer.Write(name);
            ack.Writer.Write(ch.Serial);
            ack.Writer.Write((ushort) 0);
            ack.Writer.Write(st.Money);
            ack.Writer.Write(0);
            ack.Writer.Write(ch.Mito);
            ack.Writer.Write((uint) st.Exp);
            ack.Writer.Write(0u);
            ack.Writer.Write((ulong) ei.CurExp);
            ack.Writer.Write((ulong) ei.NextExp);
            ack.Writer.Write((ulong) ei.BaseExp);
            ack.Writer.Write((uint) ch.Level);
            ack.Writer.Write(0f);
            ack.Writer.Write(0u);
            ack.Writer.Write(0u);
            ack.Writer.Write(0u);
            packet.Sender.Send(ack);
            ch.FlushModInfo(packet.Sender);
            ch.FlushNotices(packet.Sender);
            if (st.Caught <= 0)
                Chat.SendSystemMessage(packet.Sender, "Missao falhada: nenhum HUV pego. Sem recompensa.");
            else
                Chat.SendSystemMessage(packet.Sender, string.Format("Missao {0}: {1}/{2} HUV(s) pego(s), +{3} exp, +{4} mito.",
                    outcome == "concluida" ? "concluida" : "meio sucesso", st.Caught, st.Total, st.Exp, st.Money));
        }
    }
}
