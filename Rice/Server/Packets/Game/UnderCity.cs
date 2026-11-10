using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Undercity (missao de party embaixo da cidade, 4 missoes). Regras e layouts conforme a documentacao do
    // emulador openDC (servidor 2008), implementado aqui do zero para o Rice. Os tamanhos conferem com o
    // DriftCity.exe 2016 (ex.: 0x100 UnderCityResult = 386 bytes, 0xc0 HuvHit = 40).
    //
    // Entrada: 0xfc UnderCitySuggest (u32 serial, i32 tempo, i32 cidade, nome) -> 0xff UnderCityLoad (tempo, cidade,
    //   taxa, i64 mito). Aqui sempre "sozinho" (os bots da sua party vao junto: o servidor os controla).
    // Etapas: 0xfa UnderCityStage (tipo, etapa, tempo) -> 0xfb (etapa, tempo).
    // Missao 1 (100 HUBs): 0xbb8 -> 0xbb9 ; 0xbbc (cada HUB) -> 0xbbd ; no 100o -> 0xbbe resultado.
    // Missao 2 (HUBs barreira + HUB principal): 0xbc2 -> 0xbc3 ; 0xbc6 -> 0xbc7 ; HUB principal -> 0xbc8.
    // Missoes 3 e 4 (ondas de HUV + BossHUV): 0xc2 HuvParam ; 0xbe HuvReq (lista) ; o servidor solta as ondas
    //   com 0xc3 HuvTimeout + 0xbf HuvRes ; 0xc0 HuvHit ; 0xc5 HuvHealth ; 0xbcc recompensa do meio.
    // Fim: 0xc1 HuvResult + 0x100 UnderCityResult.
    // Bots da party: destroem HUBs na Missao 1 (0xbbd com o serial deles), perseguem os HUVs e batem neles
    // (0xc0 com o serial deles; o cliente conta os HUVs de cada um na lista da party).
    public static class UnderCity
    {
        const int Hubs = 100, Unit = 0x5c, EscortLast = 4, Boss = 5, BossSeconds = 900;
        static readonly int[] Fees = { 600, 1200, 2100, 4200 };
        static readonly int[][] PatrolReward = { new[] { 2, 2, 5 }, new[] { 4, 8, 12 }, new[] { 7, 15, 20 }, new[] { 11, 23, 29 } };
        static readonly int[] MainHubExp = { 77, 154, 231, 308 };
        static readonly int[][] MiddleRewardTable = { new[] { 9, 3 }, new[] { 12, 4 }, new[] { 16, 7 }, new[] { 22, 11 } };
        static readonly double[][] BossExp = { new[] { 6000.0, 100, 33 }, new[] { 6000.0, 110, 60 }, new[] { 16666.0, 120, 166 }, new[] { 13333.0, 200, 266 } };
        static readonly Dictionary<int, int> WaveSeconds = new Dictionary<int, int> { { 1, 240 }, { 2, 240 }, { 3, 300 }, { 4, 360 } };
        static readonly int[] OreLevel = { 30, 35, 40, 45 };
        static readonly string[] CityLetter = { "m", "k", "c", "o" };
        static readonly string[][] SetParts = { new[] { "si_s01", "si_s02" }, new[] { "si_a01", "si_a02" }, new[] { "si_c01", "si_c02" }, new[] { "si_b01", "si_b02" } };
        const string SEnhancer = "i_n_00044", NEnhancer = "i_n_00043", Uc120 = "i_n_00064";

        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static readonly Dictionary<ulong, Run> runs = new Dictionary<ulong, Run>();
        // copia sem trava (CityBots/CityTraffic consultam de dentro das travas deles)
        static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, bool> active = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();
        static Timer timer;
        static int sortSeq;

        static int Ms() { return Environment.TickCount; }

        class Actor
        {
            public ushort[] Info;          // stage, id, level, type, serial, sort, hp (XiHuvInfo como o cliente mandou)
            public int Stage, Hp, MaxHp, Exp, Money, HitAt;
            public ushort Sort;
        }

        class BotStat
        {
            public Bots.BotProfile Bot;
            public int Hubs, Jumps, Huvs, Exp, Mito, NextHub, NextHit;
            public double Boss;
        }

        class Run
        {
            public Rice.Game.Player Player;
            public Rice.Game.Character Ch;
            public RiceClient Client;
            public int City, StartPhase = 1, Stage;
            // Init
            public int ChaseStage = -1, ChaseBase, BossBonus, Penalty, PenaltyCheck, ReviveCheck, ChaseStart;
            public float[] ChasePos = new float[4];
            public int BossTry = -1, Retry, Uc2Record, Start, AccLeft;
            public int[] LeftHuv = new int[4];
            public HashSet<int> Patrol;            // HUBs ja destruidos na Missao 1 (null = fora dela)
            public uint PatrolId;
            public List<ushort[]> Stack = new List<ushort[]>();
            public Dictionary<ushort, Actor> Actors = new Dictionary<ushort, Actor>();
            public List<Actor> Bosses = new List<Actor>();
            public bool Over, MiddlePaid;
            // jogador
            public int Hubs, Jumps, Huvs, Exp, Mito;
            public double BossDmg;
            public int[] Items = { -1, -1, -1 };
            public bool Uc120;
            public List<BotStat> Bots = new List<BotStat>();
            public long ShareExp, ShareMito;       // a repartir com seus outros corredores na party
            public HashSet<string> SeenObjects = new HashSet<string>();

            public void Init()
            {
                ChaseStage = -1; ChaseBase = 0; BossBonus = 0; Penalty = 0; PenaltyCheck = ReviveCheck = 0;
                ChasePos = new float[4]; ChaseStart = 0; BossTry = -1; Retry = 0; Bosses.Clear(); Uc2Record = 0;
                LeftHuv = new int[4]; Start = Ms(); AccLeft = 0; Patrol = null; Stack.Clear(); Actors.Clear(); Over = false; MiddlePaid = false;
            }

            public int CityIdx { get { return Math.Max(0, Math.Min(3, City - 1)); } }
        }

        public static void Start()
        {
            if (timer != null) return;
            timer = new Timer(_ =>
            {
                try { Tick(); } catch (Exception ex) { Log.WriteError("Undercity: " + ex.Message); }
            }, null, 1000, 500);
        }

        // O personagem esta numa Undercity? (trafego da cidade e bots estacionados ficam de fora)
        public static bool InRun(Rice.Game.Character ch)
        {
            return ch != null && active.ContainsKey(ch.CID);
        }

        static Run RunOf(RicePacket packet)
        {
            var p = packet.Sender.Player;
            var ch = p != null ? p.ActiveCharacter : null;
            if (ch == null) return null;
            Run r;
            if (!runs.TryGetValue(ch.CID, out r) || r.Over) return null;
            r.Client = packet.Sender;
            return r;
        }

        static RiceClient GameOf(Run r)
        {
            return r.Player != null && r.Player.GameClient != null ? r.Player.GameClient : r.Client;
        }

        static void Send(Run r, RicePacket pk)
        {
            try { if (r.Client != null) r.Client.Send(pk); } catch (Exception) { }
        }

        static void Say(Run r, string text)
        {
            try { var c = GameOf(r); if (c != null) Chat.SendSystemMessage(c, text); } catch (Exception) { }
        }

        static void Note(string text) { Log.WriteLine("Undercity: " + text); }

        static void Flush(Run r)
        {
            try { var c = GameOf(r); if (c != null) { r.Ch.FlushModInfo(c); r.Ch.FlushNotices(c); } } catch (Exception) { }
        }

        static byte[] WStr(string s, int bytes)
        {
            var b = new byte[bytes];
            var e = Encoding.Unicode.GetBytes(s ?? "");
            Array.Copy(e, b, Math.Min(e.Length, bytes - 2));
            return b;
        }

        // ---- recompensas ------------------------------------------------------------------------------------------
        static bool HasUc120(Run r)
        {
            return r.Ch.Inventory.Any(i => i.ID == Uc120 && i.stackNum > 0);
        }

        // exp/mito para o jogador (bonus da party, multiplicador do servidor, item Undercity 120%)
        static void Gain(Run r, ref int exp, ref int mito)
        {
            var cfg = RiceServer.Config;
            double mult = cfg.UnderCityRewardMultiplier * BotSocial.RewardBonus(r.Ch);
            if (HasUc120(r)) { mult *= 1.2; if (exp > 0 || mito > 0) r.Uc120 = true; }
            exp = (int) Math.Max(0, Math.Round(exp * mult));
            mito = (int) Math.Max(0, Math.Round(mito * mult));
            if (exp > 0) r.Ch.GrantExperience(exp);
            if (mito > 0) r.Ch.GrantMito((ulong) mito);
            r.Exp += exp; r.Mito += mito;
            r.ShareExp += exp; r.ShareMito += mito;
        }

        static void ShareNow(Run r)
        {
            if (r.ShareExp > 0 || r.ShareMito > 0) BotSocial.ShareRewards(r.Ch, r.ShareExp, r.ShareMito);
            r.ShareExp = r.ShareMito = 0;
        }

        // da um item e devolve (InvenIdx, TableIdx), ou (0, -1)
        static int Give(Run r, string id, uint count, out int tableIdx)
        {
            tableIdx = -1;
            try
            {
                Rice.Game.Item it;
                if (!r.Ch.GrantItem(id, count, out it) || it == null) return 0;
                tableIdx = (int) it.GetInfo().TableIdx;
                return (int) it.invIdx;
            }
            catch (Exception ex) { Log.WriteError("Undercity item " + id + ": " + ex.Message); return 0; }
        }

        static void UseUc120(Run r)
        {
            try
            {
                var it = r.Ch.Inventory.FirstOrDefault(i => i.ID == Uc120 && i.stackNum > 0);
                if (it == null) return;
                Rice.Game.Item dropped;
                r.Ch.DropItem((int) it.invIdx, out dropped, 1, true);
            }
            catch (Exception ex) { Log.WriteError("Undercity 120%: " + ex.Message); }
        }

        // ---- entrada ----------------------------------------------------------------------------------------------
        [RicePacket(0xfc, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void Suggest(RicePacket packet)
        {
            var buf = packet.Buffer;
            var p = packet.Sender.Player;
            var ch = p != null ? p.ActiveCharacter : null;
            if (ch == null || buf.Length < 12) return;
            var cfg = RiceServer.Config;
            int time = BitConverter.ToInt32(buf, 4), city = BitConverter.ToInt32(buf, 8);
            if (city < 1 || city > 4) city = ch.City >= 1 && ch.City <= 4 ? ch.City : 1;
            if (!cfg.UnderCityEnabled)
            {
                Reject(packet.Sender);
                Bots.Popup(packet.Sender, "A Undercity esta desligada (UnderCityEnabled no server.json).");
                return;
            }
            int fee = cfg.UnderCityFee ? (cfg.UnderCityFees != null && cfg.UnderCityFees.Length >= city ? cfg.UnderCityFees[city - 1] : Fees[city - 1]) : 0;
            Rush.Leave(ch);                            // saiu da zona da Rush para a Undercity
            var party = BotSocial.PartyBots(ch);
            lock (Sync)
            {
                Run old;
                if (runs.TryGetValue(ch.CID, out old)) old.Over = true;
                if (fee > 0 && ch.Mito < fee)
                {
                    runs.Remove(ch.CID);
                    bool gone; active.TryRemove(ch.CID, out gone);
                    Reject(packet.Sender);
                    Bots.Popup(packet.Sender, "Undercity: voce precisa de " + fee + " mito para entrar.");
                    return;
                }
                if (fee > 0) ch.SpendMito((ulong) fee);
                var r = new Run { Player = p, Ch = ch, Client = packet.Sender, City = city };
                r.Init();
                r.StartPhase = Math.Max(1, Math.Min(4, cfg.UnderCityStartPhase));
                foreach (var b in party.Take(3)) r.Bots.Add(new BotStat { Bot = b });
                runs[ch.CID] = r;
                active[ch.CID] = true;

                var pk = new RicePacket(0xff);
                pk.Writer.Write(time);
                pk.Writer.Write(city);
                pk.Writer.Write(fee);
                pk.Writer.Write(ch.Mito);
                packet.Sender.Send(pk);
                Note(string.Format("{0} entra na cidade {1}, taxa {2}, {3} bot(s) na party{4}.", ch.Name, city, fee, r.Bots.Count,
                                   r.StartPhase > 1 ? ", comecando na missao " + r.StartPhase : ""));
            }
            // o cliente devia mandar 0xfa (1, 1) depois de carregar; se nao mandar (ficou na cena do robo), o servidor
            // manda a etapa 1 sozinho (UnderCityStageFallbackSeconds)
            Run started; lock (Sync) runs.TryGetValue(ch.CID, out started);
            if (started != null && cfg.UnderCityStageFallbackSeconds > 0)
                System.Threading.Tasks.Task.Delay((int) (cfg.UnderCityStageFallbackSeconds * 1000)).ContinueWith(_ =>
                {
                    try
                    {
                        lock (Sync)
                        {
                            Run cur;
                            if (!runs.TryGetValue(started.Ch.CID, out cur) || cur != started || started.Over || started.Stage != 0) return;
                            started.Init();
                            started.Stage = 1;
                            var st = new RicePacket(0xfb);
                            st.Writer.Write(1);
                            st.Writer.Write(0);
                            Send(started, st);
                            Note("o cliente nao mandou a etapa 1 (0xfa); mandada pelo servidor.");
                        }
                    }
                    catch (Exception ex) { Log.WriteError("Undercity: " + ex.Message); }
                });
            if (party.Count > 0)
            {
                var b = party[Rng.Next(party.Count)];
                Bots.SayLater(packet.Sender, 1500, "party", b.Name, Pick("Bora pra Undercity!", "Partiu! Eu pego os HUBs da esquerda.", "Vamos nessa, cuidado com o Boss!"));
            }
        }

        static string Pick(params string[] s) { return s[Rng.Next(s.Length)]; }

        static void Reject(RiceClient c)
        {
            var pk = new RicePacket(0xfe);
            pk.Writer.Write(0u);
            pk.Writer.Write(0);
            c.Send(pk);
        }

        [RicePacket(0xfd, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void Agree(RicePacket packet)
        {
            // sozinho nao ha o que combinar (os bots sempre aceitam)
        }

        [RicePacket(0xfe, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void RejectReq(RicePacket packet)
        {
            lock (Sync) { var r = RunOf(packet); if (r != null) Drop(r); }
        }

        [RicePacket(0x101, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void Exit(RicePacket packet)
        {
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                Note(r.Ch.Name + " saiu da Undercity.");
                Drop(r);
            }
        }

        static void Drop(Run r)
        {
            r.Over = true;
            Run cur;
            if (runs.TryGetValue(r.Ch.CID, out cur) && cur == r) { runs.Remove(r.Ch.CID); bool x; active.TryRemove(r.Ch.CID, out x); }
        }

        // ---- etapas -----------------------------------------------------------------------------------------------
        [RicePacket(0xfa, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void Stage(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            int type = BitConverter.ToInt32(buf, 0), st = BitConverter.ToInt32(buf, 4), time = BitConverter.ToInt32(buf, 8);
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                if (type != 1 && type != 2) return;
                if (type == 1 && st == 1) r.BossBonus = 0;
                if (st == 0 || st == 1) r.Init();
                else if (st == 7)
                {
                    if (r.StartPhase >= 3) { SkipMission2(r); return; }
                    if (r.BossTry < 1) r.BossTry = 1;
                    int limit = 120 + (r.BossTry - 1) * 10 + (r.City == 1 ? 20 : 0);
                    r.ChaseBase = Ms() + 12600 + limit * 1000;
                }
                r.Stage = st;
                var pk = new RicePacket(0xfb);
                pk.Writer.Write(st);
                pk.Writer.Write(time);
                Send(r, pk);
                Note("etapa " + st + ".");
            }
        }

        // ---- Missao 1: 100 HUBs -----------------------------------------------------------------------------------
        [RicePacket(0xbb8, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void PatrolStart(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            uint patrol = BitConverter.ToUInt32(buf, 0), slot = BitConverter.ToUInt32(buf, 4);
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                r.Init();
                r.Hubs = r.Jumps = r.Huvs = 0; r.BossDmg = 0; r.Exp = r.Mito = 0; r.Items = new[] { -1, -1, -1 };
                int now = Ms(), every = (int) (Math.Max(1, RiceServer.Config.UnderCityBotHubSeconds) * 1000);
                foreach (var b in r.Bots)
                {
                    b.Hubs = b.Jumps = b.Huvs = b.Exp = b.Mito = 0; b.Boss = 0;
                    b.NextHub = now + 8000 + Rng.Next(every);
                }
                r.Patrol = new HashSet<int>();
                r.PatrolId = patrol;
                var pk = new RicePacket(0xbb9);
                pk.Writer.Write(patrol);
                pk.Writer.Write(slot);
                pk.Writer.Write(0f); pk.Writer.Write(0f); pk.Writer.Write(0f); pk.Writer.Write(0f);
                Send(r, pk);
                Note("Missao 1 (patrulha " + patrol + ") comecou.");
                if (r.StartPhase >= 2)
                {
                    r.Patrol = null;
                    var res = new RicePacket(0xbbe);
                    res.Writer.Write(patrol); res.Writer.Write(1u); res.Writer.Write(0u); res.Writer.Write(0u);
                    res.Writer.Write(0); res.Writer.Write(r.Ch.Mito);
                    Send(r, res);
                    Note("teste: Missao 1 pulada.");
                }
            }
        }

        [RicePacket(0xbba, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void PatrolGiveUp(RicePacket packet)
        {
            var buf = packet.Buffer;
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                r.Patrol = null;
                var pk = new RicePacket(0xbbb);
                pk.Writer.Write(buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0u);
                Send(r, pk);
            }
        }

        [RicePacket(0xbbc, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void PatrolGoal(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            uint patrol = BitConverter.ToUInt32(buf, 0), place = BitConverter.ToUInt32(buf, 4), jump = BitConverter.ToUInt32(buf, 8);
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null || r.Patrol == null) return;
                if (place >= Hubs || r.Patrol.Contains((int) place)) return;
                if (jump != 0) r.Jumps++;
                r.Hubs++;
                HubTaken(r, (int) place, r.Ch.Serial);
            }
        }

        static void HubTaken(Run r, int place, uint serial)
        {
            r.Patrol.Add(place);
            var pk = new RicePacket(0xbbd);
            pk.Writer.Write(r.PatrolId); pk.Writer.Write((uint) place); pk.Writer.Write(0u); pk.Writer.Write(0u);
            pk.Writer.Write(serial); pk.Writer.Write(0); pk.Writer.Write(r.Ch.Mito);
            Send(r, pk);
            if (r.Patrol.Count < Hubs) return;
            r.Patrol = null;
            var t = PatrolReward[r.CityIdx];
            int exp = t[0] * r.Hubs, mito = t[1] * r.Hubs + t[2] * r.Jumps;
            Gain(r, ref exp, ref mito);
            foreach (var b in r.Bots) { b.Exp += t[0] * b.Hubs; b.Mito += t[1] * b.Hubs + t[2] * b.Jumps; }
            var res = new RicePacket(0xbbe);
            res.Writer.Write(r.PatrolId); res.Writer.Write(1u); res.Writer.Write((uint) exp); res.Writer.Write(0u);
            res.Writer.Write(mito); res.Writer.Write(r.Ch.Mito);
            Send(r, res);
            Flush(r);
            Note(string.Format("Missao 1 concluida em {0} s: voce {1} HUBs ({2} vermelhos), +{3} exp +{4} mito; bots {5}.",
                               (Ms() - r.Start) / 1000, r.Hubs, r.Jumps, exp, mito,
                               string.Join(", ", r.Bots.Select(b => b.Bot.Name + " " + b.Hubs))));
            Say(r, string.Format("Missao 1: voce destruiu {0} HUBs, +{1} exp, +{2} mito.", r.Hubs, exp, mito));
            ShareNow(r);
        }

        // ---- Missao 2: HUBs barreira e HUB principal --------------------------------------------------------------
        [RicePacket(0xbc2, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void BossPatrolStart(RicePacket packet)
        {
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                if (r.BossTry < 0) r.BossTry = 1;
                int n = Math.Max(2, Math.Min(4, RiceServer.Config.UnderCityBarrierPlayers));
                int hubs = (n - 1) * (6 - r.BossTry);
                if (hubs < 1) hubs = (r.BossTry - 1) * -3 + 15;
                int limit = 120 + (r.BossTry - 1) * 10 + (r.City == 1 ? 20 : 0) + Math.Max(0, RiceServer.Config.UnderCityMission2ExtraSeconds);
                r.ChaseBase = Ms() + 300000;
                var pk = new RicePacket(0xbc3);
                pk.Writer.Write(limit * 1000);
                pk.Writer.Write(hubs);
                Send(r, pk);
                Note(string.Format("Missao 2 tentativa {0}: {1} HUBs barreira, {2} s.", r.BossTry, hubs, limit));
            }
        }

        [RicePacket(0xbc4, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void BossPatrolGiveUp(RicePacket packet)
        {
            var buf = packet.Buffer;
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                r.Init();
                var pk = new RicePacket(0xbc5);
                pk.Writer.Write(buf.Length >= 4 ? BitConverter.ToUInt32(buf, 0) : 0u);
                Send(r, pk);
            }
        }

        static void SkipMission2(Run r)
        {
            r.BossTry = -1;
            var pk = new RicePacket(0xbc8);
            pk.Writer.Write(1u); pk.Writer.Write(0u); pk.Writer.Write(0u); pk.Writer.Write(0); pk.Writer.Write(r.Ch.Mito);
            Send(r, pk);
            Note("teste: Missao 2 pulada.");
        }

        [RicePacket(0xbc6, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void BossPatrolGoal(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 12) return;
            int time = BitConverter.ToInt32(buf, 0);
            uint table = BitConverter.ToUInt32(buf, 4), place = BitConverter.ToUInt32(buf, 8);
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                r.Uc2Record = r.ChaseBase + r.Penalty - Ms();
                var pk = new RicePacket(0xbc7);
                pk.Writer.Write(time); pk.Writer.Write(table); pk.Writer.Write(place); pk.Writer.Write(0u); pk.Writer.Write(0u);
                pk.Writer.Write((uint) r.Ch.Serial); pk.Writer.Write(0); pk.Writer.Write(r.Ch.Mito);
                Send(r, pk);
                if (place != 0) return;
                r.BossTry = -1;
                int tbl;
                int inven = Give(r, SEnhancer, 1, out tbl);
                r.Items[0] = tbl;
                int exp = MainHubExp[r.CityIdx], mito = 0;
                Gain(r, ref exp, ref mito);
                foreach (var b in r.Bots) b.Exp += MainHubExp[r.CityIdx];
                var res = new RicePacket(0xbc8);
                res.Writer.Write(1u); res.Writer.Write((uint) exp); res.Writer.Write((uint) inven); res.Writer.Write(0); res.Writer.Write(r.Ch.Mito);
                Send(r, res);
                Flush(r);
                Note(string.Format("Missao 2 concluida ({0} nova(s) tentativa(s), {1} s sobrando).", r.Retry, r.Uc2Record / 1000));
                ShareNow(r);
            }
        }

        // ---- Missoes 3 e 4: HUVs --------------------------------------------------------------------------------
        [RicePacket(0xc2, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void HuvParam(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 16) return;
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                for (int i = 0; i < 4; i++) r.ChasePos[i] = BitConverter.ToSingle(buf, i * 4);
            }
        }

        [RicePacket(0xbe, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void HuvReq(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 8) return;
            int start = BitConverter.ToInt32(buf, 0);
            int n = (int) Math.Min(BitConverter.ToUInt32(buf, 4), (uint) ((buf.Length - 8) / 14));
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                r.Actors.Clear();
                r.ChaseBase = Ms();
                r.ChaseStage = 1;
                r.Penalty = 0;
                r.BossTry = -1;
                r.ChaseStart = start;
                r.Stack = new List<ushort[]>();
                for (int i = 0; i < n; i++)
                {
                    var h = new ushort[7];
                    for (int k = 0; k < 7; k++) h[k] = BitConverter.ToUInt16(buf, 8 + 14 * i + 2 * k);
                    r.Stack.Add(h);
                }
                if (r.StartPhase >= 4)
                {
                    r.Stack = r.Stack.Where(h => h[0] == Boss).ToList();
                    r.ChaseStage = Boss;
                    Note("teste: ondas puladas (o BossHUV sai ja).");
                }
                r.Bosses.Clear();
                int now = Ms();
                foreach (var b in r.Bots) b.NextHit = now + 5000 + Rng.Next(3000);
                Note("lista de HUVs: " + n + " (" + string.Join(", ", r.Stack.GroupBy(h => h[0]).OrderBy(g => g.Key)
                                                                             .Select(g => "etapa " + g.Key + ": " + g.Count())) + ").");
            }
        }

        [RicePacket(0xc4, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void HuvTimeBonus(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 4) return;
            int bonus = BitConverter.ToInt32(buf, 0);
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                r.BossBonus += bonus;
                var pk = new RicePacket(0xc4);
                pk.Writer.Write(bonus);
                pk.Writer.Write(r.BossBonus);
                Send(r, pk);
            }
        }

        [RicePacket(0xc0, RiceServer.ServerType.Game | RiceServer.ServerType.Area, CheckedIn = true)]
        public static void HuvHit(RicePacket packet)
        {
            var buf = packet.Buffer;
            if (buf.Length < 0x16) return;
            ushort hitter = BitConverter.ToUInt16(buf, 0), sort = BitConverter.ToUInt16(buf, 6);
            int hp = BitConverter.ToInt32(buf, 0xa);
            lock (Sync)
            {
                var r = RunOf(packet);
                if (r == null) return;
                Actor a;
                if (!r.Actors.TryGetValue(sort, out a) || hp < 0) return;
                int delta = a.Hp - hp;
                if (delta < 1 || delta > 1000000) return;
                var head = new byte[0x16];
                Array.Copy(buf, head, 0x16);
                Hit(r, a, hp, delta, true, hitter, null, head);
            }
        }

        // Um golpe num HUV (seu ou de um bot da party). head = os 0x16 primeiros bytes do 0xc0.
        static void Hit(Run r, Actor a, int hp, int delta, bool mine, ushort hitter, BotStat bot, byte[] head)
        {
            a.Hp = hp;
            int now = Ms();
            bool boss = a.Stage == Boss;
            if (boss) a.HitAt = now;
            Actor twin = null;
            if (boss)
            {
                var alive = r.Bosses.Where(b => r.Actors.ContainsKey(b.Sort)).ToList();
                if (alive.Count > 1) { twin = alive.First(b => b != a); twin.Hp = hp; }
            }
            int exp = 0, mito = 0;
            int parties = 1 + r.Bots.Count;
            if (a.Hp == 0 && !boss)
            {
                if (mine) { exp = a.Exp * 2; mito = a.Money * 2; r.Huvs++; }
                else
                {
                    double share = RiceServer.Config.UnderCityBotKillShare;
                    exp = (int) (a.Exp * 2 * share); mito = (int) (a.Money * 2 * share);
                    if (bot != null) { bot.Huvs++; bot.Exp += a.Exp * 2; bot.Mito += a.Money * 2; }
                }
            }
            if (boss)
            {
                var k = BossExp[r.CityIdx];
                double e = delta / k[0] * (Math.Log(Math.Max(r.Ch.Level, 1)) * k[1] + k[2]);
                double others = parties >= 4 ? 0.1 : parties == 3 ? 0.14286 : parties == 2 ? 0.25 : 1;
                if (mine) { r.BossDmg += delta; exp = (int) Math.Round(e * (parties > 1 ? 0.75 : 1)); }
                else { exp = (int) Math.Round(e * others); if (bot != null) { bot.Boss += delta; bot.Exp += (int) Math.Round(e * 0.75); } }
            }
            Gain(r, ref exp, ref mito);
            if (twin != null)
            {
                var tw = new RicePacket(0xc0);
                tw.Writer.Write(head, 0, 4);
                tw.Writer.Write((ushort) r.Ch.Serial);
                tw.Writer.Write(twin.Sort);
                tw.Writer.Write(head, 8, 0x16 - 8);
                tw.Writer.Write(0); tw.Writer.Write(r.Ch.Mito); tw.Writer.Write(0);
                Send(r, tw);
            }
            var pk = new RicePacket(0xc0);
            pk.Writer.Write(head, 0, 0x16);
            pk.Writer.Write(mito); pk.Writer.Write(r.Ch.Mito); pk.Writer.Write(exp);
            Send(r, pk);
            if (a.Hp > 0) return;
            r.Actors.Remove(a.Sort);
            if (a.Stage == EscortLast)
            {
                if (r.Actors.Count == 0 && r.Stack.Count == 0) MiddleReward(r);
            }
            else if (boss)
            {
                if (twin != null) r.Actors.Remove(twin.Sort);
                Note("BossHUV derrubado" + (mine ? "" : " por " + (bot != null ? bot.Bot.Name : "um bot")) + " (" + r.Actors.Count + " HUV(s) restando).");
                if (r.Actors.Count == 0) End(r, 1);
            }
            Flush(r);
        }

        static void MiddleReward(Run r)
        {
            if (r.MiddlePaid) return;
            r.MiddlePaid = true;
            var t = MiddleReward_(r);
            int exp = r.Huvs * t[0], mito = r.Huvs * t[1];
            foreach (var b in r.Bots) { b.Exp += b.Huvs * t[0]; b.Mito += b.Huvs * t[1]; }
            if (exp <= 0 && mito <= 0) { Note("ondas de escolta terminadas (voce nao pegou HUVs)."); return; }
            Gain(r, ref exp, ref mito);
            var pk = new RicePacket(0xbcc);
            pk.Writer.Write(r.Uc120 ? 1u : 0u); pk.Writer.Write((uint) exp); pk.Writer.Write(mito); pk.Writer.Write(r.Ch.Mito);
            Send(r, pk);
            Flush(r);
            ShareNow(r);
            Note("ondas de escolta terminadas: +" + exp + " exp +" + mito + " mito.");
        }

        static int[] MiddleReward_(Run r) { return MiddleRewardTable[r.CityIdx]; }

        static void Release(Run r, List<ushort[]> huvs, int now)
        {
            var t = new RicePacket(0xc3);
            t.Writer.Write(r.ChaseBase + r.Penalty - now);
            t.Writer.Write(r.Penalty);
            Send(r, t);
            var pk = new RicePacket(0xbf);
            pk.Writer.Write(r.ChaseStart);
            for (int i = 0; i < 4; i++) pk.Writer.Write(r.ChasePos[i]);
            pk.Writer.Write((uint) huvs.Count);
            foreach (var h in huvs)
            {
                sortSeq = (sortSeq + 1) % 30000;
                ushort sort = (ushort) (30001 + sortSeq);
                int exp, money;
                Chase.HuvReward(h[2], h[1], out exp, out money);
                var a = new Actor { Info = h, Stage = h[0], Sort = sort, Hp = h[6], MaxHp = Math.Max(1, (int) h[6]), Exp = exp, Money = money };
                if (a.Stage == Boss) { a.HitAt = now; r.Bosses.Add(a); }
                r.Actors[sort] = a;
                pk.Writer.Write(h[0]); pk.Writer.Write(h[1]); pk.Writer.Write(h[2]); pk.Writer.Write(h[3]);
                pk.Writer.Write((ushort) r.Ch.Serial); pk.Writer.Write(sort); pk.Writer.Write(h[6]);
            }
            Send(r, pk);
            Note(string.Format("etapa de perseguicao {0}: {1} HUV(s) soltos ({2} no total), proxima em {3} s.",
                               r.ChaseStage - 1, huvs.Count, r.Actors.Count, (r.ChaseBase - now) / 1000));
        }

        static void Health(Run r, Actor a, int delta, int kind)
        {
            var pk = new RicePacket(0xc5);
            pk.Writer.Write((ushort) 0); pk.Writer.Write((ushort) r.Ch.Serial); pk.Writer.Write(a.Sort); pk.Writer.Write((ushort) kind);
            pk.Writer.Write(a.Hp); pk.Writer.Write(delta);
            Send(r, pk);
        }

        static void BossHeal(Run r, int now)
        {
            var bosses = r.Bosses.Where(b => r.Actors.ContainsKey(b.Sort)).ToList();
            if (bosses.Count > 1 && bosses.Take(2).Any(b => b.HitAt + 60000 <= now))
            {
                foreach (var b in bosses.Take(2)) b.HitAt = now;
                foreach (var b in bosses)
                {
                    int d = b.MaxHp / 20;
                    if (b.Hp + d <= b.MaxHp) { b.Hp += d; Health(r, b, d, 2); }
                }
            }
            if (now >= r.PenaltyCheck)
            {
                if (r.Actors.Count > 1)
                {
                    var alive = r.Actors.Values.Where(a => a.Hp > 0).ToList();
                    var boss = alive.Where(a => a.Stage == Boss).ToList();
                    var escorts = alive.Where(a => a.Stage != Boss).ToList();
                    if (boss.Count > 0 && boss[0].Hp < boss[0].MaxHp)
                    {
                        foreach (var e in escorts)
                        {
                            int d = Math.Min(e.MaxHp / 10, e.Hp);
                            e.Hp -= d;
                            Health(r, e, -d, 0);
                        }
                        int gain = Math.Min(escorts.Count * boss[0].MaxHp / 100, boss[0].MaxHp - boss[0].Hp);
                        foreach (var b in boss) { b.Hp += gain; Health(r, b, gain, 1); }
                        foreach (var a in r.Actors.Values.Where(x => x.Hp == 0).ToList()) r.Actors.Remove(a.Sort);
                    }
                }
                r.PenaltyCheck = now + 60000;
            }
            if (now >= r.ReviveCheck)
            {
                foreach (var b in bosses)
                {
                    int d = b.MaxHp / 50;
                    if (b.Hp + d <= b.MaxHp) { b.Hp += d; Health(r, b, d, 2); }
                }
                r.ReviveCheck = now + 30000;
            }
        }

        // ---- relogio ----------------------------------------------------------------------------------------------
        static void Tick()
        {
            lock (Sync)
            {
                foreach (var r in runs.Values.ToList())
                {
                    try { TickRun(r); }
                    catch (Exception ex) { Log.WriteError("Undercity tick: " + ex.Message); }
                }
            }
        }

        static void TickRun(Run r)
        {
            if (r.Over) { Drop(r); return; }
            if (r.Player == null || r.Player.ActiveCharacter != r.Ch || r.Player.GameClient == null) { Drop(r); return; }
            int now = Ms();
            BotHubs(r, now);
            BotHits(r, now);
            if (r.Over) return;
            if (r.BossTry >= 0)
            {
                if (now - (r.ChaseBase + r.Penalty) >= 0)
                {
                    r.BossTry++;
                    r.Retry++;
                    r.ChaseBase = now + (r.BossTry * 10 + 110) * 1000;
                    if (r.BossTry > 3) { End(r, -1); return; }
                    Note("Missao 2 sem tempo: tentativa " + r.BossTry + ".");
                    Send(r, new RicePacket(0xbc9));
                }
                return;
            }
            if (r.ChaseStage < 0) return;
            if (r.Actors.Count == 0)
            {
                int left = r.ChaseBase + r.Penalty - now;
                if (left > 0) r.AccLeft += left;
                r.ChaseBase = now;
            }
            List<ushort[]> released = null;
            if (now - (r.ChaseBase + r.Penalty) >= 0)
            {
                if (r.Stack.Count == 0) { End(r, 0); return; }
                if (r.Actors.Count > 0 && r.ChaseStage >= 2 && r.ChaseStage <= 5) r.LeftHuv[r.ChaseStage - 2] = r.Actors.Count;
                int st = r.ChaseStage;
                released = r.Stack.Where(h => h[0] <= st).ToList();
                r.Stack = r.Stack.Where(h => h[0] > st).ToList();
                if (released.Count == 0) { r.ChaseStage = -1; r.Stack.Clear(); return; }
                if (WaveSeconds.ContainsKey(st)) r.ChaseBase = now + WaveSeconds[st] * 1000;
                else if (st == Boss)
                {
                    MiddleRewardOnce(r);
                    r.ChaseBase = now + BossSeconds * 1000 + r.BossBonus;
                    r.PenaltyCheck = now + 60000; r.ReviveCheck = now + 30000;
                }
                else if (r.Actors.Count > 0) { End(r, 0); return; }
                r.ChaseStage = st + 1;
            }
            if (released != null) Release(r, released, now);
            if (r.ChaseStage == Boss + 1) BossHeal(r, now);
        }

        // a recompensa do meio sai uma vez so (ao acabar as escoltas ou ao soltar o Boss)
        static void MiddleRewardOnce(Run r) { MiddleReward(r); }

        // Missao 1: cada bot da party destroi um HUB a cada UnderCityBotHubSeconds (o cliente ve o HUB sumir).
        static void BotHubs(Run r, int now)
        {
            if (r.Patrol == null || r.Bots.Count == 0) return;
            double secs = RiceServer.Config.UnderCityBotHubSeconds;
            if (secs <= 0) return;
            foreach (var b in r.Bots)
            {
                if (r.Patrol == null) return;
                if (now - b.NextHub < 0) continue;
                b.NextHub = now + (int) (secs * 1000 / Licenses.BotUnderFactor(b.Bot) * (0.6 + Rng.NextDouble() * 0.8));
                var free = Enumerable.Range(0, Hubs).Where(x => !r.Patrol.Contains(x)).ToList();
                // deixa sempre alguns HUBs para voce
                if (free.Count <= Math.Max(0, RiceServer.Config.UnderCityHubsLeftForYou)) continue;
                int place = free[Rng.Next(free.Count)];
                b.Hubs++;
                if (Rng.NextDouble() < 0.1) b.Jumps++;
                HubTaken(r, place, BotSocial.SerialOf(b.Bot));
            }
        }

        // Missoes 3 e 4: bots da party perto de um HUV batem nele (0xc0 com o serial do bot).
        static void BotHits(Run r, int now)
        {
            if (r.Actors.Count == 0 || r.Bots.Count == 0) return;
            var cfg = RiceServer.Config;
            if (cfg.UnderCityBotHitSeconds <= 0) return;
            var near = CityBots.PursuersNear(r.Ch, cfg.UnderCityBotHitRange);
            foreach (var b in r.Bots)
            {
                if (r.Actors.Count == 0 || r.Over) return;
                if (now - b.NextHit < 0) continue;
                b.NextHit = now + (int) (cfg.UnderCityBotHitSeconds * 1000 * (0.7 + Rng.NextDouble() * 0.6));
                CityBots.NearInfo info = near.FirstOrDefault(x => x.Cid == b.Bot.Cid);
                if (info == null && !cfg.UnderCityBotHitAnywhere) continue;
                Actor a = null;
                if (info != null)
                {
                    string key = info.Serial + "/" + info.Number;
                    if (r.SeenObjects.Add(key))
                        Note("HUV visto pelos bots: serial " + info.Serial + ", numero " + info.Number
                             + " (HUVs: " + string.Join(",", r.Actors.Keys) + ").");
                    if (!r.Actors.TryGetValue((ushort) info.Serial, out a)) r.Actors.TryGetValue(info.Number, out a);
                }
                if (a == null)
                {
                    var alive = r.Actors.Values.Where(x => x.Hp > 0).ToList();
                    var escorts = alive.Where(x => x.Stage != Boss).ToList();
                    var pool = escorts.Count > 0 ? escorts : alive;
                    if (pool.Count == 0) return;
                    a = pool.OrderBy(x => x.Hp).First();
                }
                bool boss = a.Stage == Boss;
                double pct = (boss ? cfg.UnderCityBotBossDamage : cfg.UnderCityBotDamage)
                             * Licenses.BotHuvFactor(b.Bot) * Licenses.BotUnderFactor(b.Bot);     // licencas do bot
                int dmg = Math.Max(1, (int) Math.Round(a.MaxHp * pct * (0.75 + Rng.NextDouble() * 0.5)));
                int hp = Math.Max(0, a.Hp - dmg);
                int delta = a.Hp - hp;
                if (delta <= 0) continue;
                var head = new byte[0x16];
                ushort ser = (ushort) BotSocial.SerialOf(b.Bot);
                BitConverter.GetBytes(ser).CopyTo(head, 0);
                BitConverter.GetBytes(a.Info[1]).CopyTo(head, 2);
                BitConverter.GetBytes((ushort) r.Ch.Serial).CopyTo(head, 4);
                BitConverter.GetBytes(a.Sort).CopyTo(head, 6);
                BitConverter.GetBytes(hp).CopyTo(head, 0xa);
                BitConverter.GetBytes(delta).CopyTo(head, 0xe);
                Hit(r, a, hp, delta, false, ser, b, head);
            }
        }

        // ---- fim --------------------------------------------------------------------------------------------------
        static double RankPoint(double boss, int huvs, int jumps, int hubs)
        {
            return boss / 100 * 0.55 + huvs / 28.0 * 0.35 + jumps / 10.0 * 0.05 + hubs / 100.0 * 0.05;
        }

        static int Rating(Run r, int result, int now)
        {
            double secs = (now - r.Start) / 1000.0;
            int a = Math.Min(r.Hubs + r.Bots.Sum(b => b.Hubs), 100);
            int b2 = Math.Min(Math.Max((r.Jumps + r.Bots.Sum(b => b.Jumps)) * 10, 0), 100);
            int uc2 = r.Retry == 0 ? r.Uc2Record / 1000 : 0;
            int c = Math.Min(Math.Max((6 - r.Retry) * 15 + uc2, 0), 100);
            int left = r.LeftHuv.Sum();
            int d = Math.Min(90 - left + Math.Max(r.AccLeft / 1000 - left * 10, 0), 100);
            int bossLeft = Math.Max((r.ChaseBase + r.Penalty - now) / 1000, 0);
            int e = Math.Min(bossLeft / 4 + 40, 100);
            int f = Math.Min(Math.Max(100 - (int) Math.Round(Math.Abs(2150 - result * secs)) / 10, 0), 100);
            int mean = (a + b2 + c + d + e + f) / 6;
            int cls = mean >= 92 ? 5 : mean >= 82 ? 4 : mean >= 72 ? 3 : mean >= 62 ? 2 : 1;
            Note(string.Format("nota: HUBs {0} vermelhos {1} Missao2 {2} HUVs {3} tempo do Boss {4} tempo total {5} -> {6}, classe {7}.",
                               a, b2, c, d, e, f, mean, cls));
            return cls;
        }

        static void End(Run r, int result)
        {
            if (r.Over) return;
            r.Over = true;
            int now = Ms();
            var cfg = RiceServer.Config;
            int tbl;
            if (result < 0) { Give(r, NEnhancer, 1, out tbl); r.Items[0] = tbl; }
            int cls = Rating(r, result, now);
            // dano no Boss vira %
            double sum = r.BossDmg + r.Bots.Sum(b => b.Boss);
            double myBoss = sum > 0 ? Math.Round(r.BossDmg * 100 / sum) : 0;
            foreach (var b in r.Bots) b.Boss = sum > 0 ? Math.Round(b.Boss * 100 / sum) : 0;

            int got = 0;
            if (result > 0)
            {
                var parts = SetParts[r.CityIdx];
                string part = (cls >= cfg.UnderCityBetaClass ? parts[1] : parts[0]) + "_00" + Rng.Next(1, 5);
                got = Give(r, part, 1, out tbl);
                r.Items[1] = tbl;
                int[] byClass = cfg.UnderCityEnhancers != null && cfg.UnderCityEnhancers.Length > 0 ? cfg.UnderCityEnhancers : new[] { 1, 1, 2, 3, 3 };
                int count = byClass[Math.Min(Math.Max(cls, 1), byClass.Length) - 1];
                if (count > 0) { Give(r, cfg.UnderCityEnhancer ?? "i_n_00003", (uint) count, out tbl); r.Items[2] = tbl; }
            }
            else if (result == 0)
            {
                int d = Math.Abs(OreLevel[r.CityIdx] - r.Ch.Level);
                double chance = d < 21 ? 100 : d < 31 ? 80 : d < 41 ? 60 : 40;
                if (Rng.NextDouble() * 100 <= chance) { Give(r, "gi_" + CityLetter[r.CityIdx] + "_001", 1, out tbl); r.Items[1] = tbl; }
            }
            if (result > 0) { try { Licenses.CountUndercity(r.Ch, r.City); } catch (Exception lex) { Log.WriteError("Licencas (Undercity): " + lex.Message); } }
            var res = new RicePacket(0xc1);
            res.Writer.Write(result);
            res.Writer.Write((uint) got);
            Send(r, res);
            if (r.Uc120) UseUc120(r);

            // tabela final: voce e ate 3 bots, ordenados pelos pontos
            var rows = new List<Tuple<int, double, int, string, int[], double, int[]>>();
            rows.Add(Tuple.Create(0, RankPoint(myBoss, r.Huvs, r.Jumps, r.Hubs), r.Ch.Level, r.Ch.Name,
                                  new[] { r.Exp, r.Mito, r.Hubs, r.Jumps, r.Huvs }, myBoss, r.Items));
            for (int i = 0; i < r.Bots.Count && i < 3; i++)
            {
                var b = r.Bots[i];
                rows.Add(Tuple.Create(i + 1, RankPoint(b.Boss, b.Huvs, b.Jumps, b.Hubs), b.Bot.Level, b.Bot.Name,
                                      new[] { b.Exp, b.Mito, b.Hubs, b.Jumps, b.Huvs }, b.Boss, new[] { -1, -1, -1 }));
            }
            var pk = new RicePacket(0x100);
            pk.Writer.Write(result);
            pk.Writer.Write(cls);
            pk.Writer.Write(r.Retry);
            pk.Writer.Write(now - r.Start);
            int written = 0;
            foreach (var row in rows.OrderByDescending(x => x.Item2).Take(4))
            {
                pk.Writer.Write(row.Item1);
                pk.Writer.Write((float) row.Item2);
                pk.Writer.Write(row.Item3);
                pk.Writer.Write(WStr(row.Item4, 42));
                foreach (var v in row.Item5) pk.Writer.Write(v);
                pk.Writer.Write((int) row.Item6);
                foreach (var v in row.Item7) pk.Writer.Write(v);
                pk.Writer.Write((byte) 0); pk.Writer.Write((byte) 0);
                written++;
            }
            for (; written < 4; written++)
            {
                var blank = new byte[Unit];
                BitConverter.GetBytes(-1).CopyTo(blank, 0);
                pk.Writer.Write(blank);
            }
            Send(r, pk);
            Flush(r);
            ShareNow(r);
            string[] grades = { "D", "C", "B", "A", "S" };
            string outcome = result > 0 ? "concluida" : result < 0 ? "falhou na Missao 2" : "falhou (tempo)";
            if (result > 0) PartyTalk.Praise(r.Ch, "Undercity");
            Note(string.Format("fim: {0}, classe {1}, {2} s; voce {3} HUBs, {4} vermelhos, {5} HUVs, Boss {6}%, {7} exp, {8} mito.",
                               outcome, grades[cls - 1], (now - r.Start) / 1000, r.Hubs, r.Jumps, r.Huvs, myBoss, r.Exp, r.Mito));
            Say(r, string.Format("Undercity {0}! Classe {1}. Voce ganhou {2} exp e {3} mito no total.", outcome, grades[cls - 1], r.Exp, r.Mito));
            r.Stage = 0;
            // fica registrada ate o 0x101 (sair) para o cliente; o relogio a remove
        }

        // ---- comandos ---------------------------------------------------------------------------------------------
        public static bool HandleCommand(RiceClient c, string lower, string body)
        {
            if (lower == "undercity" || lower == "uc")
            {
                Chat.SendSystemMessage(c, "Undercity: fale com a Tatiana (ou digite /ucm, /uck, /ucc, /uco no chat) com sua party de bots. "
                    + "Taxa por cidade: 600/1200/2100/4200 mito. Os bots destroem HUBs e batem nos HUVs com voce. "
                    + "Teste: !!uc fase 1-4 (comecar na missao N; 1 = normal).");
                return true;
            }
            if (lower.StartsWith("uc fase"))
            {
                int n;
                if (!int.TryParse(lower.Substring(7).Trim(), out n) || n < 1 || n > 4)
                {
                    Chat.SendSystemMessage(c, "Uso: !!uc fase 1 a 4 (1 HUBs, 2 barreiras, 3 ondas de HUV, 4 BossHUV). Agora: " + RiceServer.Config.UnderCityStartPhase + ".");
                    return true;
                }
                RiceServer.Config.UnderCityStartPhase = n;
                Chat.SendSystemMessage(c, n == 1 ? "Undercity normal (comeca na Missao 1)." : "Teste: a proxima Undercity comeca na Missao " + n + " (as anteriores contam como feitas, sem premio).");
                return true;
            }
            return false;
        }
    }
}
