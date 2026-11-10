using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Escada de crews (mano a mano na Battle Zone):
    //   Rookies -> MoonFlow -> MitoKings -> KoiGhost -> CrasRacers -> CrasElite -> OrosLegends -> LegendsDC (-> Most Wanted)
    // Voce so desafia a crew da vez. Fique perto de um membro por DuelNearSeconds: ele pergunta se quer correr; "sim" abre
    // uma sala "<bot> - <crew>" com 2 lugares. Ganhou: ele sai da crew dele e entra na sua (sem crew, uma e criada).
    // Sem membros na crew: o chefe te chama e a sala dele fica na Battle Zone ate voce vencer. Chefe vencido: carro dele,
    // BossMitoBase x (chefes vencidos + 1) e +1 andar na garagem; ele tambem entra na sua crew e a proxima crew libera.
    // Os bots que voce venceu doam mito ou peca a cada CrewGiftMinutes-Max min (mais nivel, mais premio).
    public static class CrewLadder
    {
        static readonly string FilePath = Paths.World("crew_ladder.json");
        static readonly object Sync = new object();
        static readonly Random Rng = new Random();
        static Timer timer;

        public class State
        {
            public int Stage;
            public List<long> Beaten = new List<long>();
            public int Bosses;
            public bool BossCalled;
            public bool MostWanted;
            public bool RazorBeaten;                  // venceu o Razor (chefe da Most Wanted)
            public bool RazorBonus;                   // Razor na sua crew: tudo que voce ganhar (exp e mito) em dobro
            public List<long> Recruited = new List<long>();   // bots que voce chamou para a crew (recrutar/amizade): usam vaga
            public Dictionary<long, DateTime> NextGift = new Dictionary<long, DateTime>();
        }
        static Dictionary<string, State> states;
        static readonly Dictionary<string, DateTime> nearSince = new Dictionary<string, DateTime>();
        static readonly Dictionary<string, DateTime> offeredAt = new Dictionary<string, DateTime>();
        static DateTime nextGiftCheck = DateTime.UtcNow.AddSeconds(30);

        public static void Start()
        {
            if (timer != null) return;
            timer = new Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("Escada de crews: " + ex.Message); } }, null, 8000, 2000);
        }

        static Dictionary<string, State> All()
        {
            if (states != null) return states;
            try { states = File.Exists(FilePath) ? JsonConvert.DeserializeObject<Dictionary<string, State>>(File.ReadAllText(FilePath)) : null; }
            catch (Exception ex) { Log.WriteError(FilePath + ": " + ex.Message); }
            return states ?? (states = new Dictionary<string, State>());
        }

        static void Save() { try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(All(), Formatting.Indented)); } catch (Exception) { } }

        public static State Of(Rice.Game.Character ch)
        {
            lock (Sync)
            {
                State s;
                // progresso por login (conta), nao por corredor: os slots da mesma conta dividem a escada
                string k = KeyOf(ch);
                if (!All().TryGetValue(k, out s))
                {
                    State old;
                    if (All().TryGetValue(ch.CID.ToString(), out old)) { s = old; All().Remove(ch.CID.ToString()); }   // progresso antigo (por corredor)
                    else s = new State();
                    All()[k] = s;
                    Save();
                }
                return s;
            }
        }

        static string KeyOf(Rice.Game.Character ch) { return "conta" + ch.UID; }

        // ---- metade para o lider da crew ---------------------------------------------------------------------
        // Membro (bot ou outro corredor seu) ganhou mito numa corrida, entrega ou HUV: ele fica com o dele e o lider da
        // crew (o corredor que criou) ganha a metade disso, entregue quando o lider estiver jogando.
        const string ShareFile = "crew_share2.json";
        // lider -> (quem rendeu e o que fez -> mito para o lider)
        static Dictionary<string, Dictionary<string, long>> pendingShare;
        static Dictionary<string, Dictionary<string, long>> Pending()
        {
            if (pendingShare != null) return pendingShare;
            try { pendingShare = File.Exists(ShareFile) ? JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, long>>>(File.ReadAllText(ShareFile)) : null; }
            catch (Exception) { pendingShare = null; }
            return pendingShare ?? (pendingShare = new Dictionary<string, Dictionary<string, long>>());
        }

        static string WhatName(string what)
        {
            switch (what)
            {
                case "atividade": return "missoes/entregas/HUV";
                case "corrida": return "corrida";
                case "rush": return "Rush";
                case "grupo": return "HUV/Battle Zone com o grupo";
                default: return "premio";
            }
        }

        public static void LeaderShare(long memberCid, long earned, string what, string name = null)
        {
            if (earned <= 0 || !RiceServer.Config.CrewLeaderShare || !RiceServer.Config.CrewLadder) return;
            var t = Crew.Of(memberCid);
            if (t == null || t.IsBot || t.Master == memberCid || t.Master <= 0 || t.Master >= Bots.CidBase) return;
            long half = earned / 2;
            if (half <= 0) return;
            if (name == null) { var mb = Bots.ByCid(memberCid); name = mb != null ? mb.Name : "membro"; }
            string label = name + " (" + WhatName(what) + ")";
            lock (Sync)
            {
                Dictionary<string, long> d;
                if (!Pending().TryGetValue(t.Master.ToString(), out d)) Pending()[t.Master.ToString()] = d = new Dictionary<string, long>();
                long cur; d.TryGetValue(label, out cur);
                d[label] = cur + half;
                try { File.WriteAllText(ShareFile, JsonConvert.SerializeObject(Pending())); } catch (Exception) { }
            }
        }

        // o lider recebe: uma linha por membro ("EoPixNada? (corrida): +350 mito para voce")
        static void PayShare(RiceClient c, Rice.Game.Character ch)
        {
            Dictionary<string, long> d;
            lock (Sync)
            {
                if (!Pending().TryGetValue(ch.CID.ToString(), out d) || d.Count == 0) return;
                Pending().Remove(ch.CID.ToString());
                try { File.WriteAllText(ShareFile, JsonConvert.SerializeObject(Pending())); } catch (Exception) { }
            }
            long total = d.Values.Sum();
            if (total <= 0) return;
            ch.GrantMito((ulong) total, false);
            MitoChanged(c, ch, total);
            foreach (var kv in d.OrderByDescending(x => x.Value).Take(8))
                Chat.SendSystemMessage(c, "[Crew] " + kv.Key + ": +" + kv.Value.ToString("N0") + " mito para voce.");
            if (d.Count > 8) Chat.SendSystemMessage(c, "[Crew] ... e mais " + (d.Count - 8) + " membro(s). Total: +" + total.ToString("N0") + " mito.");
            Log.WriteLine("Crew: {0} recebeu {1} mito dos membros ({2}).", ch.Name, total, string.Join(", ", d.Select(kv => kv.Key + " " + kv.Value)));
        }

        // ---- vagas da crew: CrewSlotsStart no comeco, +CrewSlotsPerBoss por chefe vencido. So contam os bots que voce
        // chamou (recrutar/amizade); os vencidos no mano a mano e seus outros corredores nao usam vaga.
        public static int SlotsOf(Rice.Game.Character ch) { var cfg = RiceServer.Config; return Math.Max(0, cfg.CrewSlotsStart) + Math.Max(0, cfg.CrewSlotsPerBoss) * Of(ch).Bosses; }

        public static int UsedSlots(Rice.Game.Character ch)
        {
            var t = Crew.Of((long) ch.CID);
            var st = Of(ch);
            lock (Sync)
            {
                if (t == null) return 0;
                st.Recruited.RemoveAll(x => !t.Members.Contains(x));
                return st.Recruited.Count;
            }
        }

        public static bool CanRecruit(Rice.Game.Character ch, out string why)
        {
            why = null;
            if (!RiceServer.Config.CrewLadder || RiceServer.Config.CrewSlotsStart < 0) return true;
            int used = UsedSlots(ch), max = SlotsOf(ch);
            if (used < max) return true;
            why = "Sua crew esta sem vaga (" + used + "/" + max + "). Venca um chefe da escada para ganhar +" + RiceServer.Config.CrewSlotsPerBoss + " vagas.";
            return false;
        }

        public static void NoteRecruit(Rice.Game.Character ch, long cid)
        {
            var st = Of(ch);
            lock (Sync) { if (!st.Recruited.Contains(cid)) st.Recruited.Add(cid); Save(); }
        }

        static readonly Dictionary<ulong, DateTime> nextTeamCall = new Dictionary<ulong, DateTime>();
        static void TeamCall(Rice.Game.Player p, Rice.Game.Character ch, State st, Crew.CrewData crew, List<Bots.BotProfile> members)
        {
            var cfg = RiceServer.Config;
            if (cfg.CrewTeamCallMinutes <= 0 || members.Count == 0) return;
            DateTime t;
            lock (Sync)
            {
                if (!nextTeamCall.TryGetValue(ch.CID, out t)) { nextTeamCall[ch.CID] = DateTime.UtcNow.AddMinutes(cfg.CrewTeamCallMinutes * (0.5 + Rng.NextDouble())); return; }
                if (DateTime.UtcNow < t) return;
                nextTeamCall[ch.CID] = DateTime.UtcNow.AddMinutes(cfg.CrewTeamCallMinutes * (0.7 + Rng.NextDouble() * 0.6));
            }
            var mine = Crew.Of((long) ch.CID);
            if (mine == null) return;
            var mates = mine.Members.Select(Bots.ByCid).Where(b => b != null && !b.IsAlt && !b.Special && !Bots.IsAce(b)).ToList();
            if (mates.Count == 0) return;
            // de preferencia um rival da cidade em que voce esta (a crew da vez e de uma cidade so; Most Wanted: qualquer um)
            var here = members.Where(b => b.HomeCity == ch.City).ToList();
            var pool = here.Count > 0 ? here : members;
            var rival = pool[Rng.Next(pool.Count)];
            var mate = mates.OrderBy(b => Math.Abs(b.Level - rival.Level)).ThenBy(b => Rng.Next()).First();
            if (Battle.DuelActive(rival.Cid)) return;
            BotChatter.OfferTeamDuel(p, mate, rival, "Ei " + ch.Name + ", bora correr nos dois contra o " + rival.Name + " da " + crew.Name
                                                     + "? Se um de nos ganhar dele, ele vem pra nossa crew. (responda sim em 1 minuto)");
            Log.WriteLine("Escada: {0} (crew de {1}) chamou para a dupla contra {2} ({3}).", mate.Name, ch.Name, rival.Name, crew.Name);
        }

        public static string[] Order() { return Bots.LadderCrews(); }

        public static string CurrentCrew(Rice.Game.Character ch)
        {
            var o = Order(); var st = Of(ch);
            if (st.Stage < o.Length) return o[st.Stage];
            return st.MostWanted && !st.RazorBeaten ? Bots.MostWantedCrew : null;
        }

        // algum corredor online ja liberou a Most Wanted (eles aparecem online)
        public static bool AnyMostWanted()
        {
            if (!RiceServer.Config.CrewLadder) return false;
            foreach (var p in RiceServer.GetPlayers())
                if (p != null && p.ActiveCharacter != null && Of(p.ActiveCharacter).MostWanted) return true;
            return false;
        }

        // bonus do Razor: exp e mito que voce ganha em dobro
        public static double RewardMult(Rice.Game.Character ch)
        {
            if (ch == null || !RiceServer.Config.CrewLadder) return 1.0;
            lock (Sync) { State s; return All().TryGetValue(KeyOf(ch), out s) && s.RazorBonus ? 2.0 : 1.0; }
        }

        // peca da loja de uma categoria e grau (N/S/H/U; option FH; dock OH/OU): uma das melhores, sem as de evento
        static ItemTableEntry BestItem(string cat, string grade)
        {
            var l = ItemTable.Items.OfType<ItemTableEntry>()
                        .Where(e => e.Category == cat && e.Grade == grade && e.ID != null && !e.ID.EndsWith("_to") && e.ID.IndexOf("event", StringComparison.OrdinalIgnoreCase) < 0)
                        .OrderByDescending(e => e.BasePoints).Take(5).ToList();
            return l.Count == 0 ? null : l[Rng.Next(l.Count)];
        }

        static string GiveItem(RiceClient c, Rice.Game.Character ch, ItemTableEntry e, bool upgraded)
        {
            if (e == null) return null;
            Rice.Game.Item it;
            if (!ch.GrantItem(e.ID, 1, out it) || it == null) return null;
            if (upgraded && BotSocial.PartCats.Contains(e.Category))
            {
                it.upgrade = 3 + Rng.Next(3);
                it.upgradePoint = it.upgrade * 2;
                ch.SaveItemState(it);
                return e.Name + " +" + it.upgrade;
            }
            return e.Name;
        }

        // premio da Most Wanted: membro = carro V9, 200.000 mito e 4 pecas H upadas (ou dock/option H);
        // Razor = carro V9, 10.000.000 mito, 8 pecas U, docks U e options H (a melhor que existe) e o bonus em dobro
        static void MostWantedPrize(RiceClient c, Rice.Game.Character ch, Bots.BotProfile b, State st, bool razor)
        {
            var got = new List<string>();
            Rice.Game.Vehicle car; Rice.Game.Item key;
            if (ch.GrantVehicle(b.Car, out car, out key, 0, 9))
            {
                var rc = new RicePacket(98);
                rc.Writer.Write(car.GetInfo());
                rc.Writer.Write(1u);
                c.Send(rc);
                got.Add(Bots.CarName(b.Car) + " V9");
            }
            long mito = razor ? 10000000L : 200000L;
            ch.GrantMito((ulong) mito, false);
            MitoChanged(c, ch, mito);
            got.Add(mito.ToString("N0") + " mito");
            string[] cats = { "speed", "accel", "crash", "boost" };
            if (razor)
            {
                foreach (var cat in cats) for (int i = 0; i < 2; i++) { var n = GiveItem(c, ch, BestItem(cat, "U"), true); if (n != null) got.Add(n); }
                foreach (var cat in new[] { "dock_S", "dock_A", "dock_C", "dock_B" }) { var n = GiveItem(c, ch, BestItem(cat, "OU"), false); if (n != null) got.Add(n); }
                foreach (var cat in new[] { "op_S", "op_A", "op_C", "op_B" }) { var n = GiveItem(c, ch, BestItem(cat, "FH"), false); if (n != null) got.Add(n); }
                lock (Sync) { st.RazorBeaten = true; st.RazorBonus = true; }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    double r = Rng.NextDouble();
                    var e = r < 0.6 ? BestItem(cats[Rng.Next(4)], "H")
                          : r < 0.8 ? BestItem(new[] { "dock_S", "dock_A", "dock_C", "dock_B" }[Rng.Next(4)], "OH")
                          : BestItem(new[] { "op_S", "op_A", "op_C", "op_B" }[Rng.Next(4)], "FH");
                    var n = GiveItem(c, ch, e, true);
                    if (n != null) got.Add(n);
                }
            }
            ch.FlushModInfo(c);
            Chat.SendSystemMessage(c, "[Most Wanted] Voce venceu " + b.Name + "! Ganhou: " + string.Join(", ", got) + "."
                                      + (razor ? " Razor entrou na sua crew: tudo que voce ganhar (exp e mito) agora vale em dobro!" : " " + b.Name + " entrou na sua crew."));
            Log.WriteLine("Most Wanted: {0} venceu {1}: {2}.", ch.Name, b.Name, string.Join(", ", got));
        }

        static string CityName(int c) { return c == 2 ? "Koinonia" : c == 3 ? "Cras" : c == 4 ? "Oros" : "Moon Palace"; }

        // chefe da crew: o lider, se for bot; na LegendsDC o lider e o RiceAdmin (jogador), entao o chefe e o bot de nivel
        // mais alto dela (amPerl)
        static long BossCid(Crew.CrewData t)
        {
            if (t == null) return 0;
            if (Bots.ByCid(t.Master) != null) return t.Master;
            var top = t.Members.Select(Bots.ByCid).Where(b => b != null && !b.IsAlt).OrderByDescending(b => b.Level).FirstOrDefault();
            return top != null ? top.Cid : t.Master;
        }

        static List<Bots.BotProfile> MembersOf(Crew.CrewData t)
        {
            long boss = BossCid(t);
            return t.Members.Where(m => m != t.Master && m != boss).Select(Bots.ByCid).Where(b => b != null && !b.IsAlt).ToList();
        }

        static void Tick()
        {
            var cfg = RiceServer.Config;
            if (!cfg.CrewLadder) return;
            bool gifts = DateTime.UtcNow >= nextGiftCheck;
            if (gifts) nextGiftCheck = DateTime.UtcNow.AddSeconds(60);
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                var c = p != null ? p.GameClient : null;
                if (ch == null || c == null || Bots.InTutorial(ch)) continue;
                var st = Of(ch);
                if (gifts) { Gifts(c, ch, st); PayShare(c, ch); }
                string cname = CurrentCrew(ch);
                var crew = cname != null ? Crew.ByName(cname) : null;
                if (crew == null) continue;
                var members = MembersOf(crew);
                var boss = Bots.ByCid(BossCid(crew));
                // sem membros: o chefe chama e a sala dele fica aberta na Battle Zone ate voce vencer
                if (members.Count == 0 && boss != null && !boss.IsAlt)
                {
                    if (!st.BossCalled)
                    {
                        st.BossCalled = true;
                        lock (Sync) Save();
                        string msg = "Voce derrubou a minha crew inteira, " + ch.Name + ". Agora e comigo: a minha sala esta na Battle Zone. Te espero la.";
                        Bots.Say(c, "channel", boss.Name, msg);
                        Chat.SendSystemMessage(c, "[Mensagem de " + boss.Name + ", chefe da " + crew.Name + "] " + msg);
                        Log.WriteLine("Escada: {0} chamou {1} para o mano a mano de chefe ({2}).", boss.Name, ch.Name, crew.Name);
                    }
                    if (!Battle.DuelActive(boss.Cid)) Battle.OpenDuel(boss, boss.Name + " - " + crew.Name + " (CHEFE)", 1e8);
                    continue;
                }
                // de vez em quando um bot da sua crew chama (chat da crew) para correrem juntos contra um membro da crew da vez
                TeamCall(p, ch, st, crew, members);
                // perto de um membro da crew da vez por DuelNearSeconds: ele chama para o mano a mano
                if (ch.Area == null || ch.Area.ID == 0) continue;
                bool noCrew = Crew.Of((long) ch.CID) == null;
                foreach (var b in members)
                {
                    float d = CityBots.DistanceTo(ch, b.Cid);
                    string key = ch.CID + ":" + b.Cid;
                    if (d > cfg.DuelNearMeters) { lock (Sync) nearSince.Remove(key); continue; }
                    DateTime since, last;
                    lock (Sync)
                    {
                        if (!nearSince.TryGetValue(key, out since)) { nearSince[key] = since = DateTime.UtcNow; }
                        if ((DateTime.UtcNow - since).TotalSeconds < Math.Max(1, cfg.DuelNearSeconds)) continue;
                        if (offeredAt.TryGetValue(key, out last) && (DateTime.UtcNow - last).TotalMinutes < 3) continue;
                        offeredAt[key] = DateTime.UtcNow;
                        nearSince.Remove(key);
                    }
                    if (noCrew)
                    {
                        // sem crew nao da para desafiar: ele pede para criar uma
                        Bots.MeetPlayer(b, 20);
                        Bots.Say(c, "channel", b.Name, "Ei " + ch.Name + ", sou da " + crew.Name + ". So corro mano a mano com quem tem crew. Cria a sua crew e volta!");
                        break;
                    }
                    if (Battle.DuelActive(b.Cid)) continue;
                    Bots.MeetPlayer(b, 70);
                    BotChatter.OfferDuel(p, b, "Ei " + ch.Name + ", sou da " + crew.Name + ". Corre mano a mano comigo na Battle Zone? (responda sim em 1 minuto)");
                    Log.WriteLine("Escada: {0} ({1}) chamou {2} para o mano a mano.", b.Name, crew.Name, ch.Name);
                    break;
                }
            }
        }

        // corrida em dupla aceita: sala com voce, o bot da sua crew e o rival
        public static void AcceptTeam(RiceClient c, Rice.Game.Character ch, Bots.BotProfile mate, Bots.BotProfile rival)
        {
            var crew = rival != null ? Crew.Of(rival.Cid) : null;
            if (crew == null || crew.Name != CurrentCrew(ch) || BossCid(crew) == rival.Cid) { Bots.SayLater(c, 1200, "team", mate.Name, "Ih, o " + (rival != null ? rival.Name : "cara") + " ja nao ta mais na crew dele."); return; }
            if (!Battle.DuelActive(rival.Cid)) Battle.OpenDuel(rival, mate.Name + " & " + ch.Name + " vs " + rival.Name, 180, mate);
            Bots.SayLater(c, 1200, "team", mate.Name, "Fechou! A sala '" + mate.Name + " & " + ch.Name + " vs " + rival.Name + "' esta na Battle Zone. Se um de nos chegar na frente dele, ele vem pra nossa crew!");
        }

        // voce aceitou: ele abre a sala com o nome dele e da crew, 2 lugares
        public static void Accept(RiceClient c, Rice.Game.Character ch, Bots.BotProfile b)
        {
            var crew = Crew.Of(b.Cid);
            if (crew == null || crew.Name != CurrentCrew(ch)) { Bots.SayLater(c, 1200, "channel", b.Name, "Deixa pra la, " + ch.Name + "."); return; }
            if (Crew.Of((long) ch.CID) == null) { Bots.SayLater(c, 1200, "channel", b.Name, "Primeiro cria a sua crew, " + ch.Name + ". Sem crew eu nao corro."); return; }
            if (!Battle.DuelActive(b.Cid)) Battle.OpenDuel(b, b.Name + " - " + crew.Name, 180);
            Bots.Unlink(b.Cid);
            Bots.SayLater(c, 1200, "channel", b.Name, "Fechou! A sala '" + b.Name + " - " + crew.Name + "' esta na Battle Zone. Te espero 3 minutos.");
        }

        public static void DuelResult(RiceClient c, Rice.Game.Character ch, long botCid, bool won)
        {
            var b = Bots.ByCid(botCid);
            if (b == null || ch == null) return;
            var crew = Crew.Of(botCid);
            string cname = CurrentCrew(ch);
            if (!won)
            {
                Bots.Say(c, "channel", b.Name, Pick("Ganhei, " + ch.Name + "! Treina mais e volta.", "Hoje nao, " + ch.Name + ". Quando quiser a revanche, e so chegar."));
                Log.WriteLine("Escada: {0} perdeu o mano a mano para {1}.", ch.Name, b.Name);
                return;
            }
            if (crew == null || crew.Name != cname) { Bots.Say(c, "channel", b.Name, "Boa corrida, " + ch.Name + "!"); return; }
            var st = Of(ch);
            bool isBoss = BossCid(crew) == botCid;
            if (crew.Name == Bots.MostWantedCrew)
            {
                var mineMw = Crew.Of((long) ch.CID) ?? Crew.EnsureFor(ch);
                Crew.RemoveMember(crew, botCid);
                Crew.AddMember(mineMw, botCid);
                b.CrewTid = mineMw.Tid; b.CrewName = mineMw.Name;
                Bots.Ungroup(botCid);
                Bots.Save();
                lock (Sync)
                {
                    if (!st.Beaten.Contains(botCid)) st.Beaten.Add(botCid);
                    st.NextGift[botCid] = DateTime.UtcNow.AddMinutes(GiftGap());
                    if (isBoss) st.BossCalled = false;
                }
                Bots.Say(c, "channel", b.Name, isBoss ? "Ninguem tinha me vencido, " + ch.Name + ". Voce e o numero 1 da Blacklist agora."
                                                      : "Voce me tirou da Blacklist, " + ch.Name + ". Corro pela sua crew agora.");
                MostWantedPrize(c, ch, b, st, isBoss);
                lock (Sync) Save();
                return;
            }
            var mine = Crew.EnsureFor(ch);
            Crew.RemoveMember(crew, botCid);
            Crew.AddMember(mine, botCid);
            b.CrewTid = mine.Tid; b.CrewName = mine.Name;
            Bots.Ungroup(botCid);
            Bots.Save();
            lock (Sync)
            {
                if (!st.Beaten.Contains(botCid)) st.Beaten.Add(botCid);
                st.NextGift[botCid] = DateTime.UtcNow.AddMinutes(GiftGap());
            }
            if (isBoss)
            {
                var cfg = RiceServer.Config;
                long mito = Math.Max(0, cfg.BossMitoBase) * (st.Bosses + 1);
                Rice.Game.Vehicle car; Rice.Game.Item key;
                int grade = Math.Max(1, BotSocial.GradeOf(b));
                if (ch.GrantVehicle(b.Car, out car, out key, 0, grade))
                {
                    var rc = new RicePacket(98);
                    rc.Writer.Write(car.GetInfo());
                    rc.Writer.Write(1u);
                    c.Send(rc);
                }
                if (mito > 0) { ch.GrantMito((ulong) mito, false); MitoChanged(c, ch, mito); }
                ch.AddGarageSlot();
                ch.FlushModInfo(c);
                lock (Sync) { st.Bosses++; st.Stage++; st.BossCalled = false; }
                string next = CurrentCrew(ch);
                Bots.Say(c, "channel", b.Name, "Voce e bom mesmo, " + ch.Name + ". O meu carro e seu, e agora eu corro pela sua crew.");
                Chat.SendSystemMessage(c, "Chefe da " + crew.Name + " vencido! Voce ganhou o " + Bots.CarName(b.Car) + " V" + grade + ", " + mito.ToString("N0")
                                          + " mito e +1 andar na garagem (aparece quando entrar de novo). " + b.Name + " entrou na sua crew.");
                if (next != null)
                    Chat.SendSystemMessage(c, "Proxima crew: " + next + " (" + CityName(Bots.CityOfCrew(next)) + ").");
                else
                {
                    lock (Sync) st.MostWanted = true;
                    Chat.SendSystemMessage(c, "Voce venceu todas as crews! A Most Wanted (Blacklist) esta de olho em voce...");
                }
                Log.WriteLine("Escada: {0} venceu o chefe {1} da {2} (+{3} mito, carro {4} V{5}).", ch.Name, b.Name, crew.Name, mito, b.Car, grade);
            }
            else
            {
                int left = MembersOf(crew).Count;
                Bots.Say(c, "team", b.Name, Pick("Perdi, mas foi justo. To na sua crew agora, " + ch.Name + "!", "Voce venceu. Conta comigo na sua crew!"));
                Chat.SendSystemMessage(c, b.Name + " saiu da " + crew.Name + " e entrou na sua crew (" + mine.Name + "). "
                                          + (left > 0 ? "Faltam " + left + " membro(s) da " + crew.Name + "." : "So falta o chefe da " + crew.Name + "!"));
                Log.WriteLine("Escada: {0} venceu {1} da {2}; faltam {3}.", ch.Name, b.Name, crew.Name, left);
            }
            lock (Sync) Save();
        }

        static double GiftGap()
        {
            var cfg = RiceServer.Config;
            return Math.Max(1, cfg.CrewGiftMinutes) + Rng.NextDouble() * Math.Max(0, cfg.CrewGiftMaxMinutes - cfg.CrewGiftMinutes);
        }

        // bots que voce venceu e estao na sua crew doam mito ou uma peca (mais nivel, mais premio)
        static void Gifts(RiceClient c, Rice.Game.Character ch, State st)
        {
            var mine = Crew.Of((long) ch.CID);
            if (mine == null) return;
            bool changed = false;
            foreach (var cid in st.Beaten.ToList())
            {
                if (!mine.Members.Contains(cid)) continue;
                var b = Bots.ByCid(cid);
                if (b == null) continue;
                DateTime t;
                lock (Sync) if (st.NextGift.TryGetValue(cid, out t) && DateTime.UtcNow < t) continue;
                lock (Sync) st.NextGift[cid] = DateTime.UtcNow.AddMinutes(GiftGap());
                changed = true;
                // Most Wanted na sua crew doa em dobro; com o Razor, tudo em dobro de novo
                double mult = (b.MostWanted ? 2.0 : 1.0) * (st.RazorBonus ? 2.0 : 1.0);
                if (Rng.NextDouble() < 0.35)
                {
                    var e = Auction.RewardItem(Math.Max(1, b.Level)) as ItemTableEntry;
                    Rice.Game.Item got;
                    if (e != null && ch.GrantItem(e.ID, 1, out got))
                    {
                        ch.FlushModInfo(c);
                        Bots.Say(c, "team", b.Name, "Achei essa " + e.Name + " e guardei pra voce, " + ch.Name + "!");
                        continue;
                    }
                }
                long mito = (long) Math.Round((1500 + 250L * Math.Max(1, b.Level)) * (0.8 + Rng.NextDouble() * 0.4) * mult);
                ch.GrantMito((ulong) mito, false);
                MitoChanged(c, ch, mito);
                Bots.Say(c, "team", b.Name, "Toma " + mito.ToString("N0") + " mito pra crew, " + ch.Name + "!");
            }
            if (changed) lock (Sync) Save();
        }

        static void MitoChanged(RiceClient c, Rice.Game.Character ch, long delta)
        {
            var m = new RicePacket(138);
            m.Writer.Write(1); m.Writer.Write(1); m.Writer.Write(delta); m.Writer.Write(ch.Mito); m.Writer.Write((ushort) 0);
            c.Send(m);
        }

        static string Pick(params string[] s) { lock (Rng) return s[Rng.Next(s.Length)]; }

        // !!escada: onde voce esta na escada
        public static string Status(Rice.Game.Character ch)
        {
            var st = Of(ch);
            string cur = CurrentCrew(ch);
            if (cur == null) return "Voce venceu todas as crews da escada (" + st.Bosses + " chefes)" + (st.RazorBeaten ? " e a Most Wanted inteira. Razor na sua crew: exp e mito em dobro!" : ".");
            if (cur == Bots.MostWantedCrew)
            {
                var mw = Crew.ByName(cur);
                int lft = mw != null ? MembersOf(mw).Count : 0;
                return "Most Wanted (Blacklist): " + (lft > 0 ? lft + " membro(s) para vencer, espalhados pelas 4 cidades" : "so falta o Razor (sala na Battle Zone)") + ".";
            }
            var t = Crew.ByName(cur);
            int left = t != null ? MembersOf(t).Count : 0;
            return "Crew da vez: " + cur + " (" + CityName(Bots.CityOfCrew(cur)) + "): " + (left > 0 ? left + " membro(s) para vencer" : "so falta o chefe (sala na Battle Zone)")
                   + ". Chefes vencidos: " + st.Bosses + ".";
        }
    }
}
