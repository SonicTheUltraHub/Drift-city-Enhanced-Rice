using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Rush Time (o boss HUV da cidade). Layouts da documentacao do emulador openDC (rush.py), implementado aqui do zero:
    //   1110 EnterRushZone -> 1111 EnterRushZoneAck (u32 hora de inicio, u32 tipo do HUV, u32 percurso), 1124 RushMemberCnt (u32)
    //       1127 RushUpdate (u32 0, u32 inicio, u32 tipo, u32 percurso, u32 membros, 3 x u32 0)
    //   1112 ExitRushZone -> 1113
    //   inicio: 1117 AssignHuvControl (u16 0, u16 0, u32 tipo, u32 percurso, u32 0): o cliente cria o boss e manda
    //   1118 NotifyRushHuv (u32 indice, u16 serial, u16 carro, u32 tipo, u32 percurso, f32 vida, i32 inicio, u32 tempo ms,
    //       u32 membros, ...) -> o servidor numera e devolve.
    //   1114 RushHitPoint (u32 indice, u16 serial, u16 carro, f32 dano) -> 1115 RushStatus (u32 n, n x (u16, u16, u16 tipo, u16 0, f32 vida))
    //   fim: 1120 RushResult (i64 mito, i64 0, crew 42 bytes, nome 26 bytes).
    // Neste servidor: entrou na zona -> contagem de RushCountdown s; os bots da cidade (e os da party) pulam perto
    // de voce e entram; no inicio o boss aparece e todos vao nele (alguns te seguem ate la). Os bots tambem batem no boss.
    public static class Rush
    {
        static readonly int[] HuvTypes = { 11001, 11003, 11004, 11007, 11008 };
        static readonly int[] CityMoney = { 0, 30000, 50000, 80000, 100000 };
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static Timer timer;

        class Huv { public ushort Serial, Sort; public int Type; public float Health, Max; public byte[] Frame; }
        class Zone
        {
            public RiceClient Client; public Rice.Game.Character Ch;
            public int HuvType, Course, State;             // 0 contagem, 1 rodando, 2 acabou
            public DateTime StartAt, EndAt;
            public readonly List<Huv> Huvs = new List<Huv>();
            public readonly List<Bots.BotProfile> Racers = new List<Bots.BotProfile>();
            public int Uid;
        }
        static readonly Dictionary<ulong, Zone> zones = new Dictionary<ulong, Zone>();

        static uint ClientTime(DateTime t)
        {
            return (uint) (new DateTimeOffset(t).ToUnixTimeSeconds() + RiceServer.Config.RushClockShift);
        }

        static void EnsureTimer()
        {
            if (timer == null) timer = new Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("Rush: " + ex.Message); } }, null, 1000, 1000);
        }

        // bots que estao na rush deste corredor (a CityBots move eles junto com a party)
        public static List<Bots.BotProfile> BotsOf(Rice.Game.Character ch)
        {
            lock (Sync) { Zone z; return ch != null && zones.TryGetValue(ch.CID, out z) && z.State < 2 ? z.Racers.ToList() : new List<Bots.BotProfile>(); }
        }

        public static bool Running(Rice.Game.Character ch)
        {
            lock (Sync) { Zone z; return ch != null && zones.TryGetValue(ch.CID, out z) && z.State == 1; }
        }

        static void Send(Zone z, RicePacket pk) { try { z.Client.Send(pk); } catch (Exception) { } }

        // ---- lugar onde comeca o Rush: aprendido na 1a vez que voce entra (rush_spots.json). Os corredores do Rush
        // ficam esperando la (estacionados); quando voce chega e o Rush comeca, eles te seguem e vao no boss.
        static readonly string SpotsFile = Paths.Map("rush_spots.json");
        static Dictionary<int, float[]> spots;
        static Dictionary<int, float[]> Spots()
        {
            if (spots != null) return spots;
            try { spots = System.IO.File.Exists(SpotsFile) ? Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<int, float[]>>(System.IO.File.ReadAllText(SpotsFile)) : null; }
            catch (Exception) { spots = null; }
            if (spots == null) spots = new Dictionary<int, float[]>();
            return spots;
        }
        public static float[] SpotOf(int city) { lock (Sync) { float[] p; return Spots().TryGetValue(city, out p) ? p : null; } }

        static readonly Dictionary<int, List<long>> waiting = new Dictionary<int, List<long>>();
        static readonly Dictionary<long, Tuple<int, DateTime>> joined = new Dictionary<long, Tuple<int, DateTime>>();

        // bots que sairam do lobby para o Rush (chamada no chat): esperam o boss no lugar do Rush da cidade deles
        public static void Join(int city, IEnumerable<Bots.BotProfile> group, double minutes)
        {
            lock (Sync)
            {
                foreach (var b in group) if (b != null) joined[b.Cid] = Tuple.Create(city, DateTime.UtcNow.AddMinutes(minutes));
                waiting.Remove(city);              // monta a turma de novo com eles
            }
        }
        static readonly Dictionary<int, DateTime> waitingAt = new Dictionary<int, DateTime>();
        // os bots que estao esperando o Rush nesta cidade (troca a turma a cada 10 min); nunca os da sua party
        public static List<Bots.BotProfile> Waiting(int city, ICollection<long> exclude)
        {
            lock (Sync)
            {
                List<long> l; DateTime at;
                if (!waiting.TryGetValue(city, out l) || !waitingAt.TryGetValue(city, out at) || (DateTime.UtcNow - at).TotalMinutes > 10)
                {
                    // moradores da cidade (CityWholeCity); quem saiu do lobby para o Rush desta cidade vem primeiro
                    var src = RiceServer.Config.CityWholeCity ? Bots.Residents(city) : Bots.All().Where(x => BotSocial.Online(x) && !x.NeoBot).ToList();
                    var first = joined.Where(kv => kv.Value.Item1 == city && DateTime.UtcNow < kv.Value.Item2).Select(kv => kv.Key).ToList();
                    l = first.Concat(src.Where(x => !x.IsAlt && !x.Special && !Bots.IsAce(x) && !first.Contains(x.Cid)).OrderBy(x => Rng.Next()).Select(x => x.Cid))
                             .Take(Math.Max(first.Count, RiceServer.Config.RushBots)).ToList();
                    waiting[city] = l; waitingAt[city] = DateTime.UtcNow;
                }
                return l.Where(c => exclude == null || !exclude.Contains(c)).Select(Bots.ByCid).Where(b => b != null && BotSocial.Online(b)).ToList();
            }
        }

        // contagem antes do boss (os corredores ainda esperam parados)
        public static bool Counting(Rice.Game.Character ch)
        {
            lock (Sync) { Zone z; return ch != null && zones.TryGetValue(ch.CID, out z) && z.State == 0; }
        }

        // numero (sort) do boss do Rush (o 1o HUV); 0 = sem Rush
        public static ushort BossSort(Rice.Game.Character ch)
        {
            lock (Sync)
            {
                Zone z;
                return ch != null && zones.TryGetValue(ch.CID, out z) && z.Huvs.Count > 0 ? z.Huvs[0].Sort : (ushort) 0;
            }
        }

        // HUVs (boss e os que ele solta) que ja estao com vida 0
        public static HashSet<ushort> DeadSorts(Rice.Game.Character ch)
        {
            lock (Sync)
            {
                Zone z;
                if (ch == null || !zones.TryGetValue(ch.CID, out z)) return new HashSet<ushort>();
                return new HashSet<ushort>(z.Huvs.Where(h => h.Health <= 0).Select(h => h.Sort));
            }
        }

        static readonly Dictionary<ulong, DateTime> lastDrop = new Dictionary<ulong, DateTime>();
        static int hitLogs;

        static void SendInfo(Zone z)
        {
            uint start = ClientTime(z.StartAt);
            var a = new RicePacket(1111);
            a.Writer.Write(start); a.Writer.Write((uint) z.HuvType); a.Writer.Write((uint) z.Course);
            Send(z, a);
            var u = new RicePacket(1127);
            u.Writer.Write(0u); u.Writer.Write(start); u.Writer.Write((uint) z.HuvType); u.Writer.Write((uint) z.Course);
            u.Writer.Write((uint) (1 + z.Racers.Count)); u.Writer.Write(0u); u.Writer.Write(0u); u.Writer.Write(0u);
            Send(z, u);
            var n = new RicePacket(1124);
            n.Writer.Write((uint) (1 + z.Racers.Count));
            Send(z, n);
        }

        [RicePacket(1110, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Enter(RicePacket packet)
        {
            var c = packet.Sender; var ch = c.Player.ActiveCharacter;
            var cfg = RiceServer.Config;
            EnsureTimer();
            Zone z;
            lock (Sync)
            {
                if (zones.TryGetValue(ch.CID, out z) && z.State < 2) { z.Client = c; SendInfo(z); return; }
                z = new Zone
                {
                    Client = c, Ch = ch, HuvType = HuvTypes[Rng.Next(HuvTypes.Length)], Course = Rng.Next(100),
                    StartAt = DateTime.Now.AddSeconds(Math.Max(3, cfg.RushCountdown))
                };
                // a party e os corredores que estavam esperando o Rush aqui entram
                var party = BotSocial.PartyBots(ch);
                z.Racers.AddRange(party);
                foreach (var b in Waiting(ch.City, party.Select(x => x.Cid).ToList()).Take(Math.Max(0, cfg.RushBots - party.Count)))
                    z.Racers.Add(b);
                zones[ch.CID] = z;
                // aprende o lugar do Rush desta cidade
                if (ch.LastX != 0 || ch.LastY != 0)
                {
                    var sp = Spots();
                    float[] old;
                    if (!sp.TryGetValue(ch.City, out old) || (old[0] - ch.LastX) * (old[0] - ch.LastX) + (old[1] - ch.LastY) * (old[1] - ch.LastY) > 60f * 60f)
                    {
                        sp[ch.City] = new[] { ch.LastX, ch.LastY };
                        try { System.IO.File.WriteAllText(SpotsFile, Newtonsoft.Json.JsonConvert.SerializeObject(sp)); } catch (Exception) { }
                        Log.WriteLine("Rush: lugar do Rush da cidade {0} gravado ({1:0}, {2:0}).", ch.City, ch.LastX, ch.LastY);
                    }
                }
            }
            SendInfo(z);
            Chat.SendSystemMessage(c, string.Format("Rush Time comeca em {0} s! {1} corredor(es) entraram com voce.", cfg.RushCountdown, z.Racers.Count));
            Log.WriteLine("Rush: {0} entrou, boss {1}, percurso {2}, {3} bot(s), comeca em {4} s.", ch.Name, z.HuvType, z.Course, z.Racers.Count, cfg.RushCountdown);
        }

        [RicePacket(1112, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void Exit(RicePacket packet)
        {
            var ch = packet.Sender.Player.ActiveCharacter;
            lock (Sync)
            {
                Zone z;
                if (zones.TryGetValue(ch.CID, out z)) Log.WriteLine("Rush: {0} saiu da zona do Rush ({1}).", ch.Name, z.State == 0 ? "antes de comecar" : z.State == 1 ? "no meio" : "no fim");
                zones.Remove(ch.CID);
            }
            packet.Sender.Send(new RicePacket(1113));
        }

        public static void Leave(Rice.Game.Character ch)
        {
            if (ch == null) return;
            lock (Sync) zones.Remove(ch.CID);
        }

        static void Tick()
        {
            List<Zone> list;
            lock (Sync) list = zones.Values.ToList();
            foreach (var z in list)
            {
                if (z.State == 0 && DateTime.Now >= z.StartAt)
                {
                    z.State = 1;
                    z.EndAt = DateTime.Now.AddMinutes(10);
                    var pk = new RicePacket(1117);
                    pk.Writer.Write((ushort) 0); pk.Writer.Write((ushort) 0);
                    pk.Writer.Write((uint) z.HuvType); pk.Writer.Write((uint) z.Course); pk.Writer.Write(0u);
                    Send(z, pk);
                    Chat.SendSystemMessage(z.Client, "Rush Time comecou! Todos no boss!");
                    Log.WriteLine("Rush: comecou (boss {0}).", z.HuvType);
                }
                else if (z.State == 1 && DateTime.Now >= z.EndAt) End(z, false);
            }
        }

        [RicePacket(1118, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void NotifyHuv(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 32) return;
            var ch = packet.Sender.Player.ActiveCharacter;
            Zone z;
            var m = (byte[]) buf.Clone();
            lock (Sync)
            {
                if (!zones.TryGetValue(ch.CID, out z) || z.State != 1) return;
                int type = BitConverter.ToInt32(m, 8);
                float health = BitConverter.ToSingle(m, 16);
                uint timeout = BitConverter.ToUInt32(m, 24);
                z.Uid = z.Uid % 19000 + 1;
                ushort sort = (ushort) (1000 + z.Uid);
                BitConverter.GetBytes((uint) z.Huvs.Count).CopyTo(m, 0);
                BitConverter.GetBytes(ch.Serial).CopyTo(m, 4);
                BitConverter.GetBytes(sort).CopyTo(m, 6);
                BitConverter.GetBytes((uint) (1 + z.Racers.Count)).CopyTo(m, 28);
                if (z.Huvs.Count == 0 && timeout > 0) z.EndAt = DateTime.Now.AddMilliseconds(timeout);
                z.Huvs.Add(new Huv { Serial = ch.Serial, Sort = sort, Type = type, Health = health, Max = health, Frame = m });
                Log.WriteLine("Rush: boss {0} ({1}) com {2:0} de vida.", type, sort, health);
            }
            var pk = new RicePacket(1118);
            pk.Writer.Write(m);
            Send(z, pk);
        }

        // 549 JumpVehicle (servidor de area): o seu carro foi jogado para cima. No Rush e o poder do boss: os bots pulam
        // tambem (CityBots.BossPower), caem, ficam parados 3 s e voltam.
        [RicePacket(549, RiceServer.ServerType.Area)]
        public static void JumpVehicle(RicePacket packet)
        {
            var ch = packet.Sender.Player != null ? packet.Sender.Player.ActiveCharacter : null;
            if (ch != null) CityBots.BossPower(ch);
        }

        [RicePacket(1114, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void HitPoint(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            int idx = BitConverter.ToInt32(buf, 0); float dmg = BitConverter.ToSingle(buf, 8);
            ushort sort = BitConverter.ToUInt16(buf, 6);
            var ch = packet.Sender.Player.ActiveCharacter;
            if (hitLogs < 30) { hitLogs++; Log.WriteLine("Rush: seu golpe idx {0} sort {1} dano {2:0.##} ({3})", idx, sort, dmg, BitConverter.ToString(buf, 0, Math.Min(buf.Length, 24))); }
            lock (Sync)
            {
                // o indice que o cliente manda nem sempre bate: acha o HUV pelo numero (sort) ou o primeiro vivo
                Zone z;
                if (zones.TryGetValue(ch.CID, out z))
                {
                    bool ok = idx >= 0 && idx < z.Huvs.Count && z.Huvs[idx].Health > 0 && (sort == 0 || z.Huvs[idx].Sort == sort || z.Huvs[idx].Serial == sort);
                    if (!ok)
                    {
                        int found = -1;
                        for (int i = 0; i < z.Huvs.Count; i++) if (z.Huvs[i].Health > 0 && z.Huvs[i].Sort == sort) { found = i; break; }
                        if (found < 0 && !(idx >= 0 && idx < z.Huvs.Count && z.Huvs[idx].Health > 0))
                            for (int i = 0; i < z.Huvs.Count; i++) if (z.Huvs[i].Health > 0) { found = i; break; }
                        if (found >= 0) idx = found;
                    }
                }
            }
            // dano muito pequeno (o cliente manda fracao?) vira pontos de vida normais
            if (dmg > 0 && dmg < 1f) dmg *= 100f;
            if (float.IsNaN(dmg) || dmg < 0) dmg = 0;
            if (dmg <= 0) dmg = (float) Math.Max(1, RiceServer.Config.RushPlayerMinDamage);
            Hit(ch, idx, dmg * (float) Math.Max(0.1, RiceServer.Config.RushPlayerDamage), null);
            HitDrop(packet.Sender, ch);
        }

        // golpe de um bot da rush no boss (CityBots)
        public static void BotHit(Rice.Game.Character ch, Bots.BotProfile b, float dmg, ushort sort = 0)
        {
            int idx = -1;
            lock (Sync)
            {
                Zone z;
                if (!zones.TryGetValue(ch.CID, out z) || z.State != 1) return;
                // o HUV em que o bot bateu (boss ou um dos que ele soltou); senao o primeiro vivo
                if (sort != 0) for (int i = 0; i < z.Huvs.Count; i++) if (z.Huvs[i].Sort == sort && z.Huvs[i].Health > 0) { idx = i; break; }
                if (idx < 0) for (int i = 0; i < z.Huvs.Count; i++) if (z.Huvs[i].Health > 0) { idx = i; break; }
            }
            if (idx < 0) return;
            // golpe do bot = fracao da vida maxima do HUV (boss: RushBotBossHit; os que ele solta: RushBotHuvHit) x forca
            // do carro do bot; assim os bots nao derretem o boss nem explodem os outros HUVs na hora
            float max; bool boss; ushort hs, hser; RiceClient zc;
            lock (Sync)
            {
                Zone z;
                if (!zones.TryGetValue(ch.CID, out z) || idx >= z.Huvs.Count) return;
                max = z.Huvs[idx].Max; boss = idx == 0; hs = z.Huvs[idx].Sort; hser = z.Huvs[idx].Serial; zc = z.Client;
            }
            var cfg = RiceServer.Config;
            double share = boss ? cfg.RushBotBossHit : cfg.RushBotHuvHit;
            float real = cfg.BotRealDamage && b != null ? BotSocial.HitDamage(b)
                       : (float) (Math.Max(1f, max) * Math.Max(0, share) * Math.Max(0.1f, dmg));
            Hit(ch, idx, real, b);
            // mostra o dano do bot como no HUV (187 ChaseHit: quem bateu, dono, numero do HUV, dano, nome)
            if (cfg.RushShowBotDamage && zc != null && b != null)
            {
                try
                {
                    var pk = new RicePacket(187);
                    pk.Writer.Write((ushort) BotSocial.SerialOf(b));
                    pk.Writer.Write(hser);
                    pk.Writer.Write(hs);
                    pk.Writer.Write(real);
                    pk.Writer.Write(new byte[0x16 - 10]);
                    var nm = new byte[44 - 0x16];
                    var ne = Encoding.Unicode.GetBytes(b.Name ?? "");
                    Array.Copy(ne, nm, Math.Min(ne.Length, nm.Length - 2));
                    pk.Writer.Write(nm);
                    zc.Send(pk);
                }
                catch (Exception) { }
            }
        }

        // cada golpe seu no boss/HUV pode dar uma peca do seu nivel para baixo; RushHitUppedChance de vir upada (+1 a +9)
        // e com Convert
        static void HitDrop(RiceClient c, Rice.Game.Character ch)
        {
            var cfg = RiceServer.Config;
            if (ch == null || !cfg.RushHitDrops) return;
            lock (Sync)
            {
                DateTime last;
                if (lastDrop.TryGetValue(ch.CID, out last) && (DateTime.Now - last).TotalSeconds < Math.Max(0.2, cfg.RushHitDropSeconds)) return;
                lastDrop[ch.CID] = DateTime.Now;
            }
            string text;
            double roll = Rng.NextDouble();
            var it = roll < cfg.HuvDropMittronChance ? Rice.Game.PartCrafting.MittronOrSeed(ch, out text)
                   : Rng.NextDouble() < cfg.RushRentChance ? Rice.Game.PartCrafting.RandomRent(ch, out text)
                   : Rice.Game.PartCrafting.RandomPartFor(ch, cfg.RushHitUppedChance, out text);
            if (it == null) return;
            try { ch.FlushModInfo(c); } catch (Exception) { }
            Misc.GetItemFx(c, it);
            Chat.SendSystemMessage(c, "Rush: " + text);
        }

        static void Hit(Rice.Game.Character ch, int idx, float dmg, Bots.BotProfile by)
        {
            Zone z; bool allDown;
            RicePacket st;
            lock (Sync)
            {
                if (ch == null || !zones.TryGetValue(ch.CID, out z) || z.State != 1 || idx < 0 || idx >= z.Huvs.Count || z.Huvs[idx].Health <= 0) return;
                z.Huvs[idx].Health = Math.Max(0f, z.Huvs[idx].Health - Math.Max(0f, dmg));
                st = new RicePacket(1115);
                st.Writer.Write((uint) z.Huvs.Count);
                foreach (var h in z.Huvs)
                {
                    st.Writer.Write(h.Serial); st.Writer.Write(h.Sort); st.Writer.Write((ushort) (h.Type & 0xffff)); st.Writer.Write((ushort) 0);
                    st.Writer.Write(h.Health);
                }
                allDown = z.Huvs.All(h => h.Health <= 0);
            }
            Send(z, st);
            if (allDown) End(z, by == null);
        }

        static void End(Zone z, bool playerWon)
        {
            lock (Sync) { if (z.State == 2) return; z.State = 2; zones.Remove(z.Ch.CID); }
            Coupons.AddParam(z.Ch, z.Client, Coupons.RushTry, 1);     // cupom FondoX: tentativas de Rush Time
            long money = 0;
            bool won = z.Huvs.Count > 0 && z.Huvs.All(h => h.Health <= 0);
            if (won)
            {
                int city = z.Ch.City >= 1 && z.Ch.City <= 4 ? z.Ch.City : 1;
                money = (long) Math.Round(CityMoney[city] * BotSocial.RewardBonus(z.Ch));
                z.Ch.GrantMito((ulong) money);
                var mm = new RicePacket(138);
                mm.Writer.Write(1); mm.Writer.Write(1); mm.Writer.Write(money); mm.Writer.Write(z.Ch.Mito); mm.Writer.Write((ushort) 0);
                Send(z, mm);
                BotSocial.ShareRewards(z.Ch, 0, money);
                PartyTalk.Praise(z.Ch, "Rush Time");
                try { Licenses.Count(z.Ch, "GC_RushtimeClear"); } catch (Exception lex) { Log.WriteError("Licencas (Rush): " + lex.Message); }
                // os corredores que nao sao da sua party tambem ganham (participaram)
                var party = BotSocial.PartyBots(z.Ch);
                foreach (var b in z.Racers.Where(x => !party.Contains(x) && !x.IsAlt && !x.Special && BotSocial.GrowthOf(x) > 0))
                {
                    if (b.Mito < 0) b.Mito = 0;
                    long rushGain = (long) Math.Round(money * 0.3 * Math.Max(0, RiceServer.Config.BotActivityScale));
                    b.Mito += rushGain;
                    CrewLadder.LeaderShare(b.Cid, rushGain, "rush");
                    Bots.AddExp(b, (long) Math.Round((20 + 2L * b.Level) * Math.Max(0, RiceServer.Config.BotActivityScale)));
                    Licenses.Activity(b, RiceServer.Config.BotLicenseActivity);
                }
                Bots.Save();
            }
            var pk = new RicePacket(1120);
            pk.Writer.Write(money); pk.Writer.Write(0L);
            pk.Writer.Write(new byte[42]);
            var name = new byte[26];
            var e = Encoding.Unicode.GetBytes(won ? z.Ch.Name : "");
            Array.Copy(e, name, Math.Min(e.Length, 24));
            pk.Writer.Write(name);
            Send(z, pk);
            Chat.SendSystemMessage(z.Client, won ? "Rush Time: boss derrotado! +" + money.ToString("N0") + " mito." : "Rush Time acabou.");
            Log.WriteLine("Rush: fim ({0}).", won ? "boss derrotado, " + money + " mito" : "tempo");
        }

        [RicePacket(1122, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void CastEvent(RicePacket packet)
        {
            var pk = new RicePacket(1122); pk.Writer.Write(packet.Buffer); packet.Sender.Send(pk);
        }

        [RicePacket(1123, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ObjectUpdate(RicePacket packet)
        {
            var pk = new RicePacket(1123); pk.Writer.Write(packet.Buffer); packet.Sender.Send(pk);
        }
    }
}
