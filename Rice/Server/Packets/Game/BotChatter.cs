using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Conversa dos bots no chat, agora com interacao:
    //  - Party (HUV, Rush, Undercity): um bot chama; outros podem aceitar e andam juntos pela cidade. Voce aceita
    //    escrevendo "bora", "sim", "s", "yes", "y"... e recebe o convite de party do bot.
    //  - Leilao: o bot baixa o preco de verdade. Outros bots se interessam (5-40 s) e compram depois de 55 s ou mais,
    //    so se a peca/carro for melhor para eles e eles tiverem nivel. Se voce se interessar ("quero", "eu compro"...)
    //    o bot te espera 3 minutos (AuctionHoldSeconds).
    //  - Corrida: o bot chama e outros aceitam; a sala dele aparece na Battle Zone com 1 lugar para voce. Entrou,
    //    5 s depois larga. Aceitar o desafio vale +100 exp e +1000 mito.
    //  - Trocas entre bots: quem propos vai ate o outro (para de frente). So aceitam peca/carro/visual melhor e com nivel.
    //  - Crew: membros anunciam vagas com o requisito da crew; bots e voce (se tiver o nivel) podem entrar.
    // Os bots que sao os seus outros corredores (slots) nunca entram nisso.
    public static class BotChatter
    {
        static Timer timer;
        static readonly Random Rng = new Random();
        static DateTime next = DateTime.UtcNow.AddSeconds(40);

        class Offer
        {
            public string Kind;                 // party, race, deal, crew
            public string Activity = "";        // party: HUV / Rush Time / Undercity
            public Bots.BotProfile Bot;
            public DateTime Until;
            public Auction.Deal Deal;
            public Crew.CrewData Crew;
            public int Challenge;
            public Bots.BotProfile Rival;       // corrida em dupla: o membro da outra crew
            public bool Done;
            public readonly List<Bots.BotProfile> Joined = new List<Bots.BotProfile>();
            public readonly HashSet<long> Answered = new HashSet<long>();   // jogadores que ja responderam
        }
        static readonly List<Offer> offers = new List<Offer>();

        public static void Start()
        {
            if (timer != null) return;
            timer = new Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("Chat dos bots: " + ex.Message); } }, null, 5000, 5000);
        }

        static string Pick(params string[] s) { return s[Rng.Next(s.Length)]; }
        public static string PickLine(params string[] s) { lock (Rng) return s[Rng.Next(s.Length)]; }

        static List<Bots.BotProfile> Talkers()
        {
            return Bots.All().Where(b => !b.IsAlt && !b.NeoBot && BotSocial.Online(b)).ToList();
        }

        static List<RiceClient> Clients()
        {
            return RiceServer.GetPlayers().Where(p => p != null && p.ActiveCharacter != null && p.GameClient != null)
                             .Select(p => p.GameClient).ToList();
        }

        // fala do bot: perto de voce (BotLocalChatRange m) no chat local com balaozinho; longe ou em outro mapa, no chat geral
        static void SayAll(int delay, string type, Bots.BotProfile b, string text)
        {
            if (b == null) return;
            foreach (var c in Clients())
            {
                var cc = c;
                if (type != "channel") { Bots.SayLater(cc, delay, type, b.Name, text); continue; }
                Later(delay, () =>
                {
                    var p = cc.Player;
                    float d = p != null ? CityBots.DistanceTo(p.ActiveCharacter, b.Cid) : float.MaxValue;
                    if (d <= RiceServer.Config.BotLocalChatRange) PartyTalk.SayArea(p, b.Name, text);
                    else Bots.Say(cc, "channel", b.Name, text);
                });
            }
        }

        public static void Speak(Bots.BotProfile b, string text, int delay = 0) { SayAll(delay, "channel", b, text); }

        // confirmacao: na party/crew (Tell) e, para quem esta perto dele, no balaozinho (sem ir para o chat geral)
        public static void Confirm(Bots.BotProfile b, string text)
        {
            if (b == null) return;
            Tell(b, text, 500);
            foreach (var c in Clients())
            {
                var p = c.Player;
                var ch = p != null ? p.ActiveCharacter : null;
                if (ch == null || BotSocial.PartyBots(ch).Contains(b)) continue;
                if (CityBots.DistanceTo(ch, b.Cid) <= RiceServer.Config.BotLocalChatRange) { var pp = p; Later(500, () => PartyTalk.SayArea(pp, b.Name, text)); }
            }
        }

        // o que o bot faz sozinho (comprou peca, trocou de carro, subiu de nivel...): so aparece se ele estiver na sua
        // party (chat da party) ou na sua crew (chat da crew). Fora disso ele fica quieto.
        public static void Tell(Bots.BotProfile b, string text, int delay = 0)
        {
            if (b == null) return;
            foreach (var c in Clients())
            {
                var ch = c.Player != null ? c.Player.ActiveCharacter : null;
                if (ch == null) continue;
                if (BotSocial.PartyBots(ch).Contains(b)) { Bots.SayLater(c, Math.Max(1, delay), "party", b.Name, text); continue; }
                // bot da sua crew: conta no chat da crew
                var t = Crew.Of((long) ch.CID);
                if (t != null && RiceServer.Config.CrewBotsTell && t.Members.Contains(b.Cid)) Bots.SayLater(c, Math.Max(1, delay), "team", b.Name, text);
            }
        }

        // ofertas (party, corrida, leilao, crew, ajuda) com intervalo de 1 a 3 minutos entre uma e outra, para dar tempo
        // de aceitar a que voce quer antes de aparecer outra
        static DateTime nextOffer = DateTime.UtcNow.AddSeconds(60);
        // alguem esta ocupado (missao, entrega, quest, HUV, Rush, Undercity)? entao ninguem chama para nada
        static bool AnyoneBusy()
        {
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                if (ch == null) continue;
                if (ch.Quest != null || Missions.Active(ch) || CityBots.Busy(ch) || Rush.Running(ch) || UnderCity.InRun(ch)) return true;
            }
            return false;
        }

        static bool OfferSlot()
        {
            if (RiceServer.Config.BotOffersWhenBusy == false && AnyoneBusy()) return false;
            lock (offers)
            {
                if (DateTime.UtcNow < nextOffer) return false;
                var cfg = RiceServer.Config;
                double min = Math.Max(10, cfg.BotOfferGapMin), max = Math.Max(min, cfg.BotOfferGapMax);
                nextOffer = DateTime.UtcNow.AddSeconds(min + Rng.NextDouble() * (max - min));
                return true;
            }
        }
        static DateTime OfferUntil() { return DateTime.UtcNow.AddSeconds(Math.Max(60, RiceServer.Config.BotOfferSeconds)); }

        // ---- o que os bots fazem nas lojas do Driver Dome (CityBots.Errand / ShopErrand) --------------------------
        static long ItemPrice(ItemTableEntry e)
        {
            long v;
            return e != null && long.TryParse((e.BuyValue ?? "").Trim(), out v) && v > 0 ? v : 20000;
        }

        static long UpPrice(Bots.BotProfile b) { return 20000 + 1500L * Math.Max(1, b.Level); }

        // ---- meta do bot: sem mito, nivel ou peca, ele corre atras (missao, entrega, HUV, Rush, corrida) ---------
        static void SetGoal(Bots.BotProfile b, string kind, string name, long mito, int level, string item, uint deal, string why)
        {
            if (b == null || !string.IsNullOrEmpty(b.GoalKind)) return;
            if (BotSocial.GrowthOf(b) < 2) return;          // bot parado nao cria meta (so na sua party)
            b.GoalKind = kind; b.GoalName = name ?? ""; b.GoalMito = mito; b.GoalLevel = level; b.GoalItem = item ?? ""; b.GoalDeal = deal;
            b.GoalMinutes = kind == "peca" ? 3 + Rng.Next(6) : 0;
            Bots.Save();
            Log.WriteLine("Bots: {0} tem uma meta: {1} ({2}).", b.Name, kind, name);
            // chama alguem para a party ajudar (no chat, respeitando o intervalo entre ofertas); senao so conta na party
            if (Rng.NextDouble() < RiceServer.Config.BotHelpAskChance && OfferSlot())
            {
                var o = new Offer { Kind = "help", Bot = b, Until = OfferUntil() };
                lock (offers) offers.Add(o);
                Speak(b, why + " " + Pick("Alguem faz party comigo pra me ajudar com isso? (responda sim)", "Quem me ajuda? Faz party comigo! (responda sim)"));
                // outro bot pode topar ajudar: eles andam juntos e a meta anda mais rapido
                if (Rng.NextDouble() < RiceServer.Config.BotHelpByBotChance)
                {
                    var hb = Talkers().Where(x => x != b && !x.Special && !Bots.IsAce(x) && string.IsNullOrEmpty(x.GoalKind) && x.Level >= b.Level - 15)
                                      .OrderBy(x => Rng.Next()).FirstOrDefault();
                    if (hb != null)
                    {
                        int d = 6000 + Rng.Next(9000);
                        Speak(hb, Pick("Eu te ajudo, " + b.Name + "! Bora juntos.", "Fechou, " + b.Name + ", vou contigo!"), d);
                        Later(d, () => { b.GoalBotHelper = hb.Cid; Bots.Group(b, new[] { hb }, 900); Log.WriteLine("Bots: {0} ajuda {1}.", hb.Name, b.Name); });
                    }
                }
            }
            else Tell(b, why);
        }

        static RiceClient ClientOfChar(long cid)
        {
            var p = RiceServer.GetPlayers().FirstOrDefault(x => x != null && x.ActiveCharacter != null && (long) x.ActiveCharacter.CID == cid);
            return p != null ? p.GameClient : null;
        }

        static string Progress(Bots.BotProfile b)
        {
            return b.GoalKind == "nivel" ? "nivel " + b.Level + "/" + b.GoalLevel
                 : b.GoalKind == "peca" ? "quase achando"
                 : Math.Max(0, b.Mito).ToString("N0") + "/" + b.GoalMito.ToString("N0") + " mito";
        }

        static bool Reached(Bots.BotProfile b)
        {
            return b.GoalKind == "nivel" ? b.Level >= b.GoalLevel && b.Mito >= b.GoalMito
                 : b.GoalKind == "peca" ? b.GoalMinutes <= 0
                 : b.Mito >= b.GoalMito;
        }

        // voce concluiu algo (missao, HUV, Rush...) com um bot que esta te pedindo ajuda na party: ele avanca e conta
        public static void HelpProgress(Rice.Game.Character ch, string what)
        {
            if (ch == null) return;
            var c = ClientOfChar((long) ch.CID);
            foreach (var b in BotSocial.PartyBots(ch).Where(x => !string.IsNullOrEmpty(x.GoalKind) && x.GoalHelper == (long) ch.CID).ToList())
            {
                if (b.Mito < 0) b.Mito = 0;
                b.Mito += Math.Max(2000, b.GoalMito / 5);
                Bots.AddExp(b, 80 + Rng.Next(80));
                if (b.GoalMinutes > 0) b.GoalMinutes = Math.Max(0, b.GoalMinutes - 2);
                Bots.Save();
                if (Reached(b)) { FinishHelp(b, ch); continue; }
                if (c != null) Bots.SayLater(c, 2500, "party", b.Name, Pick("Boa! Com esse " + what + " ja tenho " + Progress(b) + " pro " + b.GoalName + ".",
                                                                             "Valeu pela ajuda! Agora: " + Progress(b) + ". Falta pouco pro " + b.GoalName + "!"));
            }
        }

        // meta batida com a sua ajuda: agradece, te recompensa (conforme o tamanho da meta), sai da party e vai comprar
        // troca concluida com um bot: se voce deu o que ele queria (a peca, um carro, ou o mito que faltava), ele
        // agradece muito, te recompensa mais (BotTradeRewardShare) e sai da party
        public static void TradeDone(RiceClient c, Rice.Game.Character ch, Bots.BotProfile b, List<string> parts, bool car, long money)
        {
            if (b == null || ch == null || string.IsNullOrEmpty(b.GoalKind)) return;
            if (b.Mito < 0) b.Mito = 0;
            if (money > 0) b.Mito += money;
            bool got = (!string.IsNullOrEmpty(b.GoalItem) && parts.Contains(b.GoalItem))
                       || (b.GoalKind == "peca" && parts.Count > 0)
                       || (b.GoalKind == "carro" && car)
                       || ((b.GoalKind == "mito" || b.GoalKind == "up" || b.GoalKind == "carro") && money > 0 && b.Mito >= b.GoalMito);
            if (!got) { Bots.Save(); return; }
            long value = Math.Max(b.GoalMito, 20000);
            long mito = Math.Max(2000, Math.Min(RiceServer.Config.BotTradeRewardMax, (long) (value * RiceServer.Config.BotTradeRewardShare)));
            uint exp = (uint) Math.Max(200, Math.Min(80000, value / 12));
            ch.GrantMito((ulong) mito); ch.GrantExperience(exp);
            string name = b.GoalName;
            bool bought = money > 0 && (b.GoalKind == "mito" || b.GoalKind == "up" || b.GoalKind == "carro") && !parts.Contains(b.GoalItem);
            b.GoalHelper = 0;
            Bots.SayLater(c, 1500, "party", b.Name, Pick("Era exatamente isso que eu precisava! Muito, muito obrigado, " + ch.Name + "!",
                                                         "Voce e demais, " + ch.Name + "! Valeu mesmo pelo " + name + "!"));
            Chat.SendSystemMessage(c, b.Name + " te recompensou pela troca: +" + mito.ToString("N0") + " mito e +" + exp + " exp.");
            try { ch.FlushModInfo(c); } catch (Exception) { }
            if (BotSocial.PartyBots(ch).Contains(b)) Later(4000, () => { try { BotSocial.Remove(c, b, false); } catch (Exception) { } });
            Log.WriteLine("Bots: {0} conseguiu {1} na troca com {2} (+{3} mito, +{4} exp).", b.Name, name, ch.Name, mito, exp);
            if (bought) Later(5000, () => CompleteGoal(b));         // com o mito, vai comprar
            else
            {
                // ja recebeu o que queria: meta cumprida sem comprar nada
                b.GoalKind = ""; b.GoalItem = ""; b.GoalName = ""; b.GoalDeal = 0; b.GoalMito = 0; b.GoalLevel = 0; b.AskedTrade = false;
                if (b.GoalBotHelper != 0) { b.GoalBotHelper = 0; Bots.Ungroup(b.Cid); }
                Bots.Save();
            }
        }

        static void FinishHelp(Bots.BotProfile b, Rice.Game.Character ch)
        {
            var c = ClientOfChar((long) ch.CID);
            long value = b.GoalKind == "nivel" ? Math.Max(b.GoalMito, 20000L * Math.Max(1, b.GoalLevel)) : Math.Max(b.GoalMito, 20000);
            long mito = Math.Max(1000, Math.Min(RiceServer.Config.BotHelpRewardMax, (long) (value * RiceServer.Config.BotHelpRewardShare)));
            uint exp = (uint) Math.Max(100, Math.Min(50000, value / 20));
            ch.GrantMito((ulong) mito);
            ch.GrantExperience(exp);
            b.GoalHelper = 0;
            if (c != null)
            {
                Bots.SayLater(c, 1000, "party", b.Name, "Consegui o que precisava pro " + b.GoalName + "! Muito obrigado, " + ch.Name + "!");
                Chat.SendSystemMessage(c, b.Name + " te recompensou pela ajuda: +" + mito.ToString("N0") + " mito e +" + exp + " exp.");
                try { ch.FlushModInfo(c); } catch (Exception) { }
                Later(3500, () => { try { BotSocial.Remove(c, b, false); } catch (Exception) { } });
            }
            Log.WriteLine("Bots: {0} cumpriu a meta com a ajuda de {1} (+{2} mito, +{3} exp).", b.Name, ch.Name, mito, exp);
            Later(4500, () => CompleteGoal(b));
        }

        public static void ShopParts(Bots.BotProfile b)
        {
            if (b == null || b.Special || Bots.IsAce(b) || b.IsAlt) return;
            if (BotSocial.GrowthOf(b) < 2) return;          // parado: so olha a loja
            if (b.Mito < 0) b.Mito = 0;
            // tenta achar uma peca melhor do que a pior das duas dessa categoria
            ItemTableEntry ge = null; string slot = null, old = null;
            for (int t = 0; t < 6 && ge == null; t++)
            {
                var e = Auction.RewardItem(Math.Max(1, b.Level + 10)) as ItemTableEntry;
                if (e == null || !BotSocial.PartCats.Contains(e.Category)) continue;
                string k1 = e.Category, k2 = e.Category + "#2", s1, s2;
                b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
                string sl = s1 == null ? k1 : s2 == null ? k2 : (Points(s1) <= Points(s2) ? k1 : k2);
                string o; b.Parts.TryGetValue(sl, out o);
                if (o == null || e.BasePoints > Points(o)) { ge = e; slot = sl; old = o; }
            }
            if (ge == null)
            {
                // nao tem peca melhor na loja: vai atras (missoes) ou upa a que tem
                var any = b.Parts.Values.FirstOrDefault();
                if (any != null && b.Mito >= UpPrice(b) && Rng.Next(2) == 0)
                {
                    b.Mito -= UpPrice(b); Bots.Save();
                    Tell(b, Pick("Upei a minha " + NameOf(any) + " no Part Shop.", "Dei um upgrade na " + NameOf(any) + "."));
                    return;
                }
                if (any != null && b.Mito < UpPrice(b))
                    SetGoal(b, "up", NameOf(any), UpPrice(b), 0, "", 0, "Queria upar a " + NameOf(any) + ", mas falta mito. Vou fazer umas entregas.");
                else SetGoal(b, "peca", "peca melhor", 0, 0, "", 0, "Nao tinha peca melhor no Part Shop... vou atras fazendo missoes.");
                return;
            }
            int need = Auction.ItemLevel(ge.ID);
            long price = ItemPrice(ge);
            if (b.Level < need) { SetGoal(b, "nivel", ge.Name, price, need, ge.ID, 0, "Quero a " + ge.Name + ", mas pede nivel " + need + ". Vou upar no HUV e no Rush!"); return; }
            if (b.Mito < price) { SetGoal(b, "mito", ge.Name, price, 0, ge.ID, 0, "A " + ge.Name + " custa " + price.ToString("N0") + " e eu tenho " + b.Mito.ToString("N0") + ". Vou juntar mito!"); return; }
            b.Mito -= price;
            b.Parts[slot] = ge.ID; Bots.Save();
            Tell(b, Pick("Comprei " + ge.Name + " no Part Shop, ja equipei!", "Peca nova: " + ge.Name + ". Agora vai!"));
        }

        public static void ShopGarage(Bots.BotProfile b, bool buy)
        {
            if (b == null || b.Special || Bots.IsAce(b) || b.IsAlt || b.CustomLook) return;
            if (buy && BotSocial.GrowthOf(b) < 2) return;   // parado: nao compra carro
            int want = Bots.TierForLevel(b.Level);
            var gar = Bots.GarageOf(b);
            if (!buy)
            {
                // troca de carro na garagem: so entre os carros que ele tem
                var mine = gar.Where(g => g.Car != b.Car && !Bots.Unsupported(g.Car)).ToList();
                if (mine.Count == 0) { Tell(b, "So tenho esse carro na garagem."); return; }
                var pick = mine.OrderByDescending(g => Bots.CarKind(g.Car) == Bots.FocusOf(b) ? 1 : 0).ThenBy(g => Rng.Next()).First();
                Bots.UseCar(b, pick);
                Tell(b, Pick("Troquei de carro na garagem: agora e o " + Bots.CarName(pick.Car) + ".", "Peguei o " + Bots.CarName(pick.Car) + " na garagem."));
                Log.WriteLine("Bots: {0} trocou para o carro {1}.", b.Name, pick.Car);
                return;
            }
            // so os carros que a loja mostra nesse V
            var ok = Bots.ShopCarsFor(want, b, b.Level >= 90).Where(x => x != b.Car).Take(4).ToList();
            if (ok.Count == 0)
                ok = VehicleTable.Vehicles.Where(kv => kv.Value != null && kv.Value.Sellable == 1 && kv.Value.Level == want && kv.Key != b.Car && kv.Key != 95
                                                       && !Bots.Unsupported(kv.Key) && !kv.Value.IsSpecialCategory())
                                          .Select(kv => kv.Key).ToList();
            if (ok.Count == 0) { Tell(b, "Nao achei outro carro do meu V."); return; }
            int car = ok[Rng.Next(ok.Count)];
            if (buy)
            {
                long price = 0;
                try { price = VehicleTable.GetVehicleUpgrade(car, want).Price; } catch (Exception) { }
                if (b.Mito < price)
                {
                    SetGoal(b, "carro", Bots.CarName(car), price, 0, "", 0, "Queria o " + Bots.CarName(car) + ", mas custa " + price.ToString("N0") + " mito. Bora correr pra juntar!");
                    return;
                }
                b.Mito -= price;
            }
            b.Car = car; b.CarGrade = want;
            if (!gar.Any(g => g.Car == car)) gar.Add(new Bots.BotCar { Car = car, Grade = want, Color = b.Color });   // vai para a garagem
            Bots.Save();
            CityBots.Respawn(b);
            Tell(b, buy ? Pick("Comprei um " + Bots.CarName(car) + " V" + want + " novinho!", "Carro novo: " + Bots.CarName(car) + "!")
                         : Pick("Troquei de carro na garagem: agora e o " + Bots.CarName(car) + ".", "Peguei o " + Bots.CarName(car) + " na garagem."));
            Log.WriteLine("Bots: {0} {1} o carro {2}.", b.Name, buy ? "comprou" : "trocou para", car);
        }

        // a cada minuto: quem tem meta faz uma atividade, ganha mito/exp e, quando chega la, vai comprar
        static DateTime nextGoalTick = DateTime.UtcNow.AddSeconds(60), lastGoalTalk = DateTime.MinValue;
        static readonly string[] Jobs = { "missao", "delivery", "HUV", "Rush Time", "corrida", "patrulha" };

        static void GoalTick()
        {
            if (DateTime.UtcNow < nextGoalTick) return;
            nextGoalTick = DateTime.UtcNow.AddSeconds(60);
            bool changed = false;
            // bots na party de algum jogador nao ganham exp/mito sozinhos: so com o que fazem com voce (BotSocial.ShareRewards)
            var inParty = new HashSet<long>();
            foreach (var pl in RiceServer.GetPlayers())
                if (pl != null && pl.ActiveCharacter != null) foreach (var pb in BotSocial.PartyBots(pl.ActiveCharacter)) inParty.Add(pb.Cid);
            var cfgA = RiceServer.Config;
            double scale = Math.Max(0, cfgA.BotActivityScale);
            // bots sem meta: de vez em quando participam de algo (missao, entrega, HUV, Rush, corrida) e so ai ganham
            foreach (var b in Bots.All().Where(x => string.IsNullOrEmpty(x.GoalKind) && !x.IsAlt && !x.Special && !Bots.IsAce(x) && !x.NeoBot
                                                    && !inParty.Contains(x.Cid) && BotSocial.Online(x)).ToList())
            {
                if (Rng.NextDouble() > cfgA.BotActivityChance) continue;
                int grow = BotSocial.GrowthOf(b);
                if (grow == 0) continue;                       // parado: nao evolui sozinho
                if (grow == 1)
                {
                    // da sua crew: so nivel, bem devagar; o mito da atividade (corrida, entrega, HUV): metade para o lider
                    if (Bots.AddExp(b, (long) Math.Round((15 + 2L * b.Level) * cfgA.BotCrewExpScale * (0.6 + Rng.NextDouble() * 0.8))) > 0)
                        Tell(b, Pick("Subi pro nivel " + b.Level + "!", "Nivel " + b.Level + ", bora!"));
                    long earned = (long) Math.Round((300 + 80L * b.Level) * scale * (0.6 + Rng.NextDouble() * 0.8));
                    if (b.Mito < 0) b.Mito = 0;
                    b.Mito += earned;
                    CrewLadder.LeaderShare(b.Cid, earned, "atividade");
                    changed = true;
                    continue;
                }
                if (b.Mito < 0) b.Mito = 0;
                b.Mito += (long) Math.Round((300 + 80L * b.Level) * scale * (0.6 + Rng.NextDouble() * 0.8));
                if (Bots.AddExp(b, (long) Math.Round((15 + 2L * b.Level) * scale * (0.6 + Rng.NextDouble() * 0.8))) > 0)
                    Tell(b, Pick("Subi pro nivel " + b.Level + "!", "Nivel " + b.Level + ", bora!"));
                Licenses.Activity(b, cfgA.BotLicenseActivity);
                changed = true;
            }
            foreach (var b in Bots.All().Where(x => !string.IsNullOrEmpty(x.GoalKind) && !x.IsAlt && !x.Special && !inParty.Contains(x.Cid)).ToList())
            {
                if (BotSocial.GrowthOf(b) < 2) continue;      // parado: a meta antiga fica parada
                string job = Jobs[Rng.Next(Jobs.Length)];
                // com meta: participa mais vezes, mas so ganha quando participa (e um pouco mais devagar que antes)
                if (Rng.NextDouble() > cfgA.BotGoalActivityChance) continue;
                Licenses.Activity(b, cfgA.BotLicenseActivity);
                long gain = (long) Math.Round((1500 + (long) b.Level * (150 + Rng.Next(250))) * scale);
                if (job == "Rush Time" || job == "HUV") gain = gain * 3 / 2;
                if (b.GoalBotHelper != 0) { gain = gain * 17 / 10; if (b.GoalMinutes > 0) b.GoalMinutes--; }   // com ajuda vai mais rapido
                // na sua party e voce tem a peca que ele quer: pede para trocar (uma vez)
                if (b.GoalHelper != 0 && !b.AskedTrade && !string.IsNullOrEmpty(b.GoalItem))
                {
                    var hp = RiceServer.GetPlayers().FirstOrDefault(x => x != null && x.ActiveCharacter != null && (long) x.ActiveCharacter.CID == b.GoalHelper);
                    if (hp != null && hp.GameClient != null && hp.ActiveCharacter.Inventory.Any(it => it.itemEntry != null && it.itemEntry.ID == b.GoalItem))
                    {
                        b.AskedTrade = true;
                        Bots.Say(hp.GameClient, "party", b.Name, "Ei, " + hp.ActiveCharacter.Name + ", vi que voce tem a " + b.GoalName + "! Troca comigo? Te dou uma recompensa boa.");
                    }
                }
                if (b.Mito < 0) b.Mito = 0;
                b.Mito += gain;
                if (Bots.AddExp(b, (long) Math.Round((25 + Rng.Next(60)) * scale)) > 0) Tell(b, Pick("Subi pro nivel " + b.Level + "!", "Nivel " + b.Level + ", bora!"));
                if (b.GoalMinutes > 0) b.GoalMinutes--;
                changed = true;
                bool reached = b.GoalKind == "nivel" ? b.Level >= b.GoalLevel && b.Mito >= b.GoalMito
                             : b.GoalKind == "peca" ? b.GoalMinutes <= 0
                             : b.Mito >= b.GoalMito;
                if (!reached) continue;                         // o progresso nao aparece no chat
                if (b.GoalHelper != 0)
                {
                    var hc = RiceServer.GetPlayers().FirstOrDefault(x => x != null && x.ActiveCharacter != null && (long) x.ActiveCharacter.CID == b.GoalHelper);
                    if (hc != null && BotSocial.PartyBots(hc.ActiveCharacter).Contains(b)) { FinishHelp(b, hc.ActiveCharacter); continue; }
                    b.GoalHelper = 0;
                }
                CompleteGoal(b);
            }
            if (changed) Bots.Save();
        }

        static void CompleteGoal(Bots.BotProfile b)
        {
            string kind = b.GoalKind, item = b.GoalItem, name = b.GoalName; uint deal = b.GoalDeal;
            b.GoalKind = ""; b.GoalItem = ""; b.GoalName = ""; b.GoalDeal = 0; b.GoalMito = 0; b.GoalLevel = 0; b.AskedTrade = false;
            if (b.GoalBotHelper != 0)
            {
                var hb = Bots.ByCid(b.GoalBotHelper);
                b.GoalBotHelper = 0;
                Bots.Ungroup(b.Cid);
                if (hb != null)
                {
                    hb.Mito = Math.Max(0, hb.Mito) + 3000 + 300L * hb.Level;
                    Tell(b, Pick("Valeu demais pela ajuda, " + hb.Name + "!", "Consegui! Obrigado, " + hb.Name + ", te devo uma."), 1500);
                }
            }
            Log.WriteLine("Bots: {0} cumpriu a meta {1} ({2}).", b.Name, kind, name);
            if (deal != 0)
            {
                var d = Auction.GetDeal(deal);
                if (d != null) { Tell(b, "Consegui! Agora da pra comprar o " + d.Name + "."); BuyDeal(b, d, Bots.ByName(d.Seller)); return; }
            }
            if (kind == "peca")
            {
                // achou a peca fazendo missoes
                for (int t = 0; t < 8; t++)
                {
                    var e = Auction.RewardItem(Math.Max(1, b.Level)) as ItemTableEntry;
                    if (e == null || !BotSocial.PartCats.Contains(e.Category) || b.Level < Auction.ItemLevel(e.ID)) continue;
                    string k1 = e.Category, k2 = e.Category + "#2", s1, s2;
                    b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
                    string sl = s1 == null ? k1 : s2 == null ? k2 : (Points(s1) <= Points(s2) ? k1 : k2);
                    string o; b.Parts.TryGetValue(sl, out o);
                    if (o != null && e.BasePoints <= Points(o)) continue;
                    b.Parts[sl] = e.ID; Bots.Save();
                    Tell(b, "Achei " + e.Name + " fazendo missoes! Ja equipei.");
                    return;
                }
                Tell(b, "Nao achei peca melhor... depois tento de novo.");
                return;
            }
            Tell(b, Pick("Juntei o que precisava! Indo comprar o " + name + ".", "Meta batida! Vou buscar o " + name + "."));
            string place = kind == "carro" ? "concessionaria" : "part shop";
            Action buy = kind == "carro" ? (Action) (() => ShopGarage(b, true))
                       : kind == "up" ? (Action) (() => { if (b.Mito >= UpPrice(b)) { b.Mito -= UpPrice(b); Bots.Save(); Tell(b, "Upei a " + name + "!"); } })
                       : (Action) (() => BuyPart(b, item));
            // no lobby: vai ate a loja, para 3 s na porta, compra e volta dirigindo; confirma que conseguiu
            Action act = () => { buy(); Confirm(b, Pick("Consegui o " + name + "! Finalmente.", "Pronto, ja tenho o " + name + "!", "Meta cumprida: " + name + "!")); };
            double stay = Math.Max(0.5, RiceServer.Config.BotGoalShopStay);
            if (!CityBots.PartyErrand(b, place, act, stay) && !CityBots.Errand(b, place, act, stay)) Later(2000, act);
        }

        static void BuyPart(Bots.BotProfile b, string id)
        {
            var e = ItemTable.Items.FirstOrDefault(x => x.ID == id) as ItemTableEntry;
            if (e == null) { ShopParts(b); return; }
            long price = ItemPrice(e);
            if (b.Level < Auction.ItemLevel(e.ID) || b.Mito < price) { ShopParts(b); return; }
            string k1 = e.Category, k2 = e.Category + "#2", s1, s2;
            b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
            string sl = s1 == null ? k1 : s2 == null ? k2 : (Points(s1) <= Points(s2) ? k1 : k2);
            b.Mito -= price; b.Parts[sl] = e.ID; Bots.Save();
            Tell(b, "Comprei a " + e.Name + " no Part Shop! Ja equipei.");
        }

        public static void ShopBattle(Bots.BotProfile b)
        {
            if (b == null || b.IsAlt) return;
            var others = Talkers().Where(x => x != b).OrderBy(x => Rng.Next()).ToList();
            Race(b, others);
        }

        public static void ShopAuction(Bots.BotProfile b)
        {
            if (b == null || b.IsAlt) return;
            Tell(b, Pick("Dei uma olhada no leilao, ta caro hoje.", "Leilao conferido. Nada que preste.", "Vi umas promocoes no leilao!"));
        }

        static void Later(int ms, Action a)
        {
            Task.Delay(Math.Max(1, ms)).ContinueWith(_ =>
            {
                try { a(); }
                catch (Exception ex) { Log.WriteError("Chat dos bots: " + ex.Message); }
            });
        }

        static void Tick()
        {
            var cfg = RiceServer.Config;
            lock (offers) offers.RemoveAll(o => DateTime.UtcNow > o.Until.AddMinutes(5));
            try { GoalTick(); } catch (Exception ex) { Log.WriteError("Metas dos bots: " + ex.Message); }
            if (RiceServer.Config.BotLicensePassive) { try { Licenses.BotTick(); } catch (Exception ex) { Log.WriteError("Licencas dos bots: " + ex.Message); } }
            try { Battle.ChallengeTick(); } catch (Exception ex) { Log.WriteError("Desafios: " + ex.Message); }
            try { ApproachTick(); } catch (Exception ex) { Log.WriteError("Bot vindo ate voce: " + ex.Message); }
            if (!cfg.BotChatter || DateTime.UtcNow < next) return;
            next = DateTime.UtcNow.AddSeconds(Math.Max(20, cfg.BotChatterSeconds) * (0.6 + Rng.NextDouble() * 0.8));
            if (Clients().Count == 0) return;
            var bots = Talkers();
            if (bots.Count < 3) return;
            var a = bots[Rng.Next(bots.Count)];
            var others = bots.Where(x => x != a).OrderBy(x => Rng.Next()).ToList();
            int pick = Rng.Next(8);
            // evento especial (Natal, Halloween, Valentine): de vez em quando a conversa e sobre ele
            if (EventChatter.Current() != null && Rng.Next(3) == 0 && EventChatter.Talk(a, others, (dl, bb, tx) => SayAll(dl, "channel", bb, tx))) return;
            // party/corrida/leilao/crew so quando o intervalo entre ofertas passou; senao conversa normal
            if (pick != 2 && pick != 7 && !OfferSlot()) pick = Rng.Next(2) == 0 ? 2 : 7;
            switch (pick)
            {
                case 0: Race(a, others); break;
                case 1: Deal(); break;
                case 2: ItemTalk(a, others); break;
                case 3: PartyCall(a, others, "HUV"); break;
                case 4: PartyCall(a, others, "Rush Time"); break;
                case 5: PartyCall(a, others, "Undercity"); break;
                case 6: if (!cfg.CrewLadder) CrewCall(); break;       // escada de crews: bots nao recrutam
                default: BotTrade(a, others.FirstOrDefault()); break;
            }
        }

        // ---- respostas do jogador ------------------------------------------------------------------------------
        static readonly Regex Yes = new Regex(@"^\s*(bora+|s|sim+|yes|y|ok|okay|claro|aceito|eu vou|vou|to dentro|partiu|pode ser|blz|beleza|fechou|fechado|eu|eu topo|topo|demorou|vamo+s?|go)\b", RegexOptions.IgnoreCase);
        static readonly Regex Want = new Regex(@"\b(quero|eu quero|interesse|tenho interesse|compro|eu compro|me espera|espera|reserva|vou comprar)\b", RegexOptions.IgnoreCase);

        public static void OnPlayerChat(RiceClient c, string type, string message)
        {
            if (c == null || c.Player == null || c.Player.ActiveCharacter == null || string.IsNullOrWhiteSpace(message)) return;
            var ch = c.Player.ActiveCharacter;
            try { EventChatter.OnPlayerChat(ch.Name, message, Talkers(), (dl, bb, tx) => SayAll(dl, "channel", bb, tx)); }
            catch (Exception ex) { Log.WriteError("Bots no evento: " + ex.Message); }
            bool yes = Yes.IsMatch(message), want = Want.IsMatch(message);
            if (!yes && !want) return;
            Offer o;
            lock (offers)
            {
                var open = offers.Where(x => !x.Done && DateTime.UtcNow <= x.Until && !x.Answered.Contains((long) ch.CID)).ToList();
                // falou o nome de um bot: a oferta dele; senao a mais recente (interesse = leilao primeiro)
                o = open.LastOrDefault(x => x.Bot != null && message.IndexOf(x.Bot.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? (want ? open.LastOrDefault(x => x.Kind == "deal") : null)
                    ?? open.LastOrDefault();
                if (o == null) return;
                o.Answered.Add((long) ch.CID);
            }
            int d = 1200 + Rng.Next(1500);
            switch (o.Kind)
            {
                case "party":
                {
                    string err = BotSocial.InvitePlayer(c, o.Bot, o.Joined, "Bora pro " + o.Activity + " com a gente!");
                    Bots.SayLater(c, d, "channel", o.Bot.Name, err == null ? "Fechou, " + ch.Name + "! Te mandei o convite da party." : err);
                    break;
                }
                case "race":
                    if (!Battle.ChallengeOpen(o.Challenge)) { Bots.SayLater(c, d, "channel", o.Bot.Name, "A sala ja fechou, " + ch.Name + ". Fica para a proxima!"); break; }
                    Bots.SayLater(c, d, "channel", o.Bot.Name, "Boa, " + ch.Name + "! A sala 'Desafio de " + o.Bot.Name + "' esta na Battle Zone com um lugar para voce. Vale +"
                                  + RiceServer.Config.BotRaceBonusExp + " exp e +" + RiceServer.Config.BotRaceBonusMito + " mito.");
                    break;
                case "deal":
                {
                    if (!Auction.OnSaleNow(o.Deal.Serial)) { Bots.SayLater(c, d, "channel", o.Bot.Name, "Ih, " + ch.Name + ", ja vendi. Foi mal!"); break; }
                    double hold = Math.Max(30, RiceServer.Config.AuctionHoldSeconds);
                    Auction.Hold(o.Deal.Serial, hold);
                    Bots.SayLater(c, d, "channel", o.Bot.Name, "Beleza, " + ch.Name + "! Te espero " + (int) Math.Round(hold / 60) + " minutos: " + o.Deal.Name + " por "
                                  + o.Deal.Price.ToString("N0") + " mito no leilao.");
                    // depois da reserva, se voce nao comprou, volta a vender para os bots
                    var oo = o;
                    Later((int) (hold * 1000) + 55000 + Rng.Next(60000), () => TryBotBuy(oo));
                    break;
                }
                case "help":
                {
                    if (string.IsNullOrEmpty(o.Bot.GoalKind)) { Bots.SayLater(c, d, "channel", o.Bot.Name, "Ja consegui, valeu " + ch.Name + "!"); break; }
                    string err = BotSocial.InvitePlayer(c, o.Bot, null, "Me ajuda a conseguir o " + o.Bot.GoalName + "?");
                    if (err == null) o.Bot.GoalHelper = (long) ch.CID;
                    Bots.SayLater(c, d, "channel", o.Bot.Name, err == null ? "Valeu, " + ch.Name + "! Te mandei o convite da party." : err);
                    break;
                }
                case "duel":
                    CrewLadder.Accept(c, ch, o.Bot);
                    break;
                case "teamduel":
                    CrewLadder.AcceptTeam(c, ch, o.Bot, o.Rival);
                    break;
                case "trade":
                    Bots.MeetPlayer(o.Bot, 120);                  // espera voce abrir a troca
                    Bots.SayLater(c, d, "channel", o.Bot.Name, "Fechou, " + ch.Name + "! Clica em mim e escolhe Trocar.");
                    break;
                case "crew":
                {
                    var t = o.Crew;
                    if (t == null) break;
                    if (ch.Level < t.MinLevel) { Bots.SayLater(c, d, "channel", o.Bot.Name, "Foi mal, " + ch.Name + ", a " + t.Name + " pede nivel " + t.MinLevel + " ou mais."); break; }
                    if (Crew.Of((long) ch.CID) != null) { Bots.SayLater(c, d, "channel", o.Bot.Name, ch.Name + ", voce ja tem crew. Sai dela antes se quiser vir."); break; }
                    Later(d, () => Crew.Apply(c, t, t.Name));
                    break;
                }
            }
        }

        // ---- bot vem ate voce (na rua) e pede algo: trocar, correr ou ajudar --------------------------------------
        // Ele dirige ate voce e para de frente. Enquanto isso as outras chamadas no chat param por BotApproachPauseSeconds
        // (2 min). Voce tem 1 minuto (BotOfferSeconds) para responder (sim/bora); sem resposta ele volta para o lugar dele.
        static DateTime nextApproach = DateTime.UtcNow.AddSeconds(150);
        static readonly HashSet<long> sentInvite = new HashSet<long>();

        static void ApproachTick()
        {
            var cfg = RiceServer.Config;
            if (!cfg.BotApproach || DateTime.UtcNow < nextApproach) return;
            double gmin = Math.Max(60, cfg.BotApproachGapMin), gmax = Math.Max(gmin, cfg.BotApproachGapMax);
            nextApproach = DateTime.UtcNow.AddSeconds(gmin + Rng.NextDouble() * (gmax - gmin));
            if (!cfg.BotOffersWhenBusy && AnyoneBusy()) return;
            lock (offers) if (offers.Any(x => !x.Done && DateTime.UtcNow <= x.Until && x.Answered.Count == 0)) return;   // tem chamada esperando resposta
            foreach (var p in RiceServer.GetPlayers())
            {
                var ch = p != null ? p.ActiveCharacter : null;
                var c = p != null ? p.GameClient : null;
                if (ch == null || c == null || ch.Area == null || Bots.InTutorial(ch)) continue;
                var party = BotSocial.PartyBots(ch);
                var myCrew = Crew.Of((long) ch.CID);
                var pool = Talkers().Where(b => !party.Contains(b) && !b.Special && !Bots.IsAce(b) && Bots.LinkOf(b.Cid) == null).ToList();
                var crewMates = myCrew != null ? pool.Where(b => { var bc = Crew.Of(b.Cid); return bc != null && bc.Tid == myCrew.Tid; }).ToList() : new List<Bots.BotProfile>();
                // so para em voce quem e da sua crew (a que voce criou ou a mesma em que voce esta)
                var b0 = crewMates.Count > 0 ? crewMates[Rng.Next(crewMates.Count)] : null;
                if (b0 == null) continue;
                bool mate = crewMates.Contains(b0);
                double window = Math.Max(30, cfg.BotOfferSeconds);
                Bots.MeetPlayer(b0, window + 20);                // vem e para de frente para voce
                lock (offers) nextOffer = DateTime.UtcNow.AddSeconds(Math.Max(window, cfg.BotApproachPauseSeconds));   // as outras chamadas esperam
                string kind = Pick("gift", "gift", "gift", "race", "help");
                if (kind == "gift")
                {
                    // bot da sua crew achou coisas para voce: vem dirigindo, para a 3 m e manda a troca (sem precisar de "sim")
                    var items = Trade.RollGift(b0, ch);
                    if (items.Count == 0) { Bots.Unlink(b0.Cid); continue; }
                    Trade.SetGift(b0, ch, items, 240);
                    Bots.MeetPlayer(b0, 240);
                    var gp = p; var gb = b0; var gc = c;
                    Later(3000, () =>
                    {
                        float d0 = CityBots.DistanceTo(gp.ActiveCharacter, gb.Cid);
                        string giftLine = "Ei " + gp.ActiveCharacter.Name + ", achei umas coisas pra voce! To indo ai te mandar a troca.";
                        if (d0 <= RiceServer.Config.BotLocalChatRange) PartyTalk.SayArea(gp, gb.Name, giftLine); else Bots.Say(gc, "channel", gb.Name, giftLine);
                        Bots.Say(gc, "team", gb.Name, giftLine);
                    });
                    // chegou perto (ou passou 90 s): manda o pedido de troca
                    for (int k = 1; k <= 45; k++)
                    {
                        int kk = k;
                        Later(2000 * k + 3000, () =>
                        {
                            try
                            {
                                if (gp.ActiveCharacter == null || Bots.LinkOf(gb.Cid) == null) return;
                                float dd = CityBots.DistanceTo(gp.ActiveCharacter, gb.Cid);
                                if (dd <= RiceServer.Config.MeetDistance + 3f || kk == 45)
                                {
                                    lock (offers) { if (sentInvite.Contains(gb.Cid)) return; sentInvite.Add(gb.Cid); }
                                    Trade.BotInvite(gc, gp.ActiveCharacter, gb);
                                    Later(60000, () => { lock (offers) sentInvite.Remove(gb.Cid); });
                                }
                            }
                            catch (Exception ex) { Log.WriteError("Presente do bot: " + ex.Message); }
                        });
                    }
                    Log.WriteLine("Bots: {0} (sua crew) foi levar presente para {1}: {2}.", b0.Name, ch.Name, string.Join(", ", items));
                    break;
                }
                Offer o;
                string text;
                if (kind == "race")
                {
                    var chal = Battle.OpenChallenge(b0, new List<Bots.BotProfile>(), window + 60);
                    if (chal == null) { Bots.Unlink(b0.Cid); continue; }
                    o = new Offer { Kind = "race", Bot = b0, Until = DateTime.UtcNow.AddSeconds(window), Challenge = chal.Id };
                    text = Pick("Ei " + ch.Name + ", bora um racha mano a mano? Abri sala na Battle Zone.", ch.Name + ", te desafio! Corre comigo na Battle Zone?");
                }
                else if (kind == "help")
                {
                    string what = Pick("HUV", "Rush Time", "Undercity");
                    o = new Offer { Kind = "party", Activity = what, Bot = b0, Until = DateTime.UtcNow.AddSeconds(window) };
                    text = Pick(ch.Name + ", me ajuda no " + what + "? Faz party comigo!", "Ei " + ch.Name + ", vamos de " + what + " juntos? Preciso de ajuda.");
                }
                else
                {
                    o = new Offer { Kind = "trade", Bot = b0, Until = DateTime.UtcNow.AddSeconds(window) };
                    text = Pick("Ei " + ch.Name + ", quer trocar alguma coisa comigo? Peca, carro...", ch.Name + ", bora trocar? Tenho umas pecas aqui.");
                }
                lock (offers) offers.Add(o);
                var pp = p; var bb = b0; string line = text + " (responda sim/bora em 1 minuto)";
                Later(4000, () =>
                {
                    float d = CityBots.DistanceTo(pp.ActiveCharacter, bb.Cid);
                    if (d <= RiceServer.Config.BotLocalChatRange) PartyTalk.SayArea(pp, bb.Name, line); else Bots.Say(c, "channel", bb.Name, line);
                    if (mate) Bots.Say(c, "team", bb.Name, line);
                });
                Log.WriteLine("Bots: {0} foi ate {1} ({2}).", b0.Name, ch.Name, kind);
                // sem resposta em 1 minuto: ele volta para o lugar dele
                var oo = o;
                Later((int) (window * 1000) + 4000, () =>
                {
                    bool answered; lock (offers) answered = oo.Answered.Count > 0;
                    if (!answered) { Bots.Unlink(bb.Cid); Bots.SayLater(c, 0, "channel", bb.Name, Pick("Fica pra proxima!", "Beleza, depois a gente ve.")); }
                });
                break;                                            // um bot por vez
            }
        }

        // ---- party: HUV / Rush / Undercity ----------------------------------------------------------------------
        static void PartyCall(Bots.BotProfile a, List<Bots.BotProfile> others, string what)
        {
            string text = what == "HUV" ? Pick("Party pra HUV, quem vem cacar?", "Bora HUV? Liga a sirene e chega!", "Montando party pra HUV, falta gente!")
                        : what == "Rush Time" ? Pick("Rush Time ja ja! Quem vai comigo no boss?", "Party pra Rush? Vamos derrubar o boss!")
                        : Pick("Alguem pra Undercity? Preciso de party.", "Undercity agora, quem topa ir junto?");
            var o = new Offer { Kind = "party", Activity = what, Bot = a, Until = OfferUntil() };
            lock (offers) offers.Add(o);
            // ninguem entrou com voce: quem chamou e quem respondeu vao embora do lobby para o HUV/Rush na cidade deles
            if (what == "HUV" || what == "Rush Time")
            {
                var oo = o;
                Later((int) (Math.Max(30, RiceServer.Config.BotOfferSeconds) * 1000) + 5000, () => Depart(oo));
            }
            SayAll(0, "channel", a, text + " (responda bora/sim para entrar)");
            int n = Rng.Next(0, 3), delay = 3000;
            foreach (var b in others.Take(n))
            {
                var bb = b;
                delay += 2000 + Rng.Next(4000);
                SayAll(delay, "channel", bb, Pick("Eu vou!", "To dentro!", "Bora, " + a.Name + "!", "Partiu " + what + "!"));
                Later(delay, () => { lock (offers) o.Joined.Add(bb); Bots.Group(a, new[] { bb }, 300); });
            }
        }

        // escada de crews: o membro da crew da vez chama voce para o mano a mano (1 minuto para responder)
        public static void OfferDuel(Rice.Game.Player p, Bots.BotProfile b, string text)
        {
            if (p == null || b == null) return;
            var o = new Offer { Kind = "duel", Bot = b, Until = DateTime.UtcNow.AddSeconds(Math.Max(30, RiceServer.Config.BotOfferSeconds)) };
            lock (offers) offers.Add(o);
            var c = p.GameClient;
            float d = CityBots.DistanceTo(p.ActiveCharacter, b.Cid);
            if (d <= RiceServer.Config.BotLocalChatRange) PartyTalk.SayArea(p, b.Name, text); else Bots.Say(c, "channel", b.Name, text);
        }

        // bot da sua crew chama para correrem juntos contra um membro da crew da vez (chat da crew)
        public static void OfferTeamDuel(Rice.Game.Player p, Bots.BotProfile mate, Bots.BotProfile rival, string text)
        {
            if (p == null || mate == null || rival == null) return;
            var o = new Offer { Kind = "teamduel", Bot = mate, Rival = rival, Until = DateTime.UtcNow.AddSeconds(Math.Max(30, RiceServer.Config.BotOfferSeconds)) };
            lock (offers) offers.Add(o);
            Bots.Say(p.GameClient, "team", mate.Name, text);
        }

        static void Depart(Offer o)
        {
            List<Bots.BotProfile> group;
            lock (offers) group = new[] { o.Bot }.Concat(o.Joined).Distinct().ToList();
            var inParty = new HashSet<long>();
            foreach (var pl in RiceServer.GetPlayers())
                if (pl != null && pl.ActiveCharacter != null) foreach (var pb in BotSocial.PartyBots(pl.ActiveCharacter)) inParty.Add(pb.Cid);
            group = group.Where(b => b != null && !b.IsAlt && !inParty.Contains(b.Cid)).ToList();
            if (group.Count == 0) return;
            var leader = group[0];
            int city = leader.HomeCity > 0 ? leader.HomeCity : 1;
            double minutes = o.Activity == "HUV" ? 4 + Rng.NextDouble() * 4 : 5 + Rng.NextDouble() * 3;
            Bots.Ungroup(leader.Cid);
            foreach (var b in group) CityBots.LeaveLobby(b, minutes * 60);
            if (o.Activity == "HUV") CityBots.CityHuv(city, leader, group.Skip(1).ToList(), minutes);
            else Rush.Join(city, group, minutes);
            Log.WriteLine("Bots: {0} saiu do lobby para o {1} em {2} com {3}.", leader.Name, o.Activity, CityName(city),
                          group.Count > 1 ? string.Join(", ", group.Skip(1).Select(b => b.Name)) : "ninguem");
        }

        static string CityName(int c) { return c == 2 ? "Koinonia" : c == 3 ? "Cras" : c == 4 ? "Oros" : "Moon Palace"; }

        // ---- corrida (desafio) -------------------------------------------------------------------------------
        static void Race(Bots.BotProfile a, List<Bots.BotProfile> others)
        {
            // so quem cabe na arena (Beginner ate o nivel 30 com V1-V4; Pro pelo EXP Reward) entra na sala
            int arena = Battle.ArenaFor(a);
            if (arena < 0) return;
            others = others.Where(x => Battle.BotFits(arena, x)).ToList();
            SayAll(0, "channel", a, Pick("Quem encara um racha? Abri sala na Battle Zone!", "Bora corrida? Desafio aberto na Battle Zone!", "Sala aberta, vem correr comigo!")
                                    + " (responda bora/sim)");
            int n = 1 + Rng.Next(Math.Max(1, Math.Min(6, others.Count)));
            var racers = others.Take(n).ToList();
            int delay = 2500;
            foreach (var r in racers.Take(3))
            {
                delay += 1500 + Rng.Next(2500);
                SayAll(delay, "channel", r, Pick("To dentro!", "Bora!", "Vou te passar, " + a.Name + "!", "Ja entrei na sala."));
            }
            double open = Math.Max(30, RiceServer.Config.BotRaceOpenSeconds);
            var chal = Battle.OpenChallenge(a, racers, open, arena);
            if (chal == null) return;
            var o = new Offer { Kind = "race", Bot = a, Until = DateTime.UtcNow.AddSeconds(open), Challenge = chal.Id };
            lock (offers) offers.Add(o);
        }

        // ---- leilao (desconto de verdade) ----------------------------------------------------------------------
        static void Deal()
        {
            var d = Auction.MakeDeal(Rng, 10, 35);
            if (d == null) return;
            var s = Bots.ByName(d.Seller);
            if (s == null || s.IsAlt) return;
            var o = new Offer { Kind = "deal", Bot = s, Deal = d, Until = DateTime.UtcNow.AddSeconds(Math.Max(240, RiceServer.Config.BotOfferSeconds)) };
            lock (offers) offers.Add(o);
            SayAll(0, "channel", s, Pick("Promocao no leilao: " + d.Name + " de " + d.OldPrice.ToString("N0") + " por " + d.Price.ToString("N0") + " mito!",
                                       d.Name + " no leilao baixou para " + d.Price.ToString("N0") + " mito. Corre que acaba!"));
            // outros bots se interessam entre 5 s e 40 s
            var fans = Talkers().Where(x => x != s && Qualifies(x, d)).OrderBy(x => Rng.Next()).Take(Rng.Next(0, 3)).ToList();
            foreach (var f in fans)
                SayAll(5000 + Rng.Next(35000), "channel", f, Pick("Opa, me interessei!", "Esse " + d.Name + " ta barato, hein.", "Vou dar uma olhada no leilao."));
            // e compram a partir de 55 s
            Later(55000 + Rng.Next(60000), () => TryBotBuy(o));
        }

        static int CarPower(int car, int grade)
        {
            try
            {
                var u = VehicleTable.GetVehicleUpgrade(car, Math.Max(1, Math.Min(9, grade)));
                return u.Speed + u.Acceleration + u.Durability + u.Boost;
            }
            catch (Exception) { return 0; }
        }

        static double Points(string id)
        {
            var e = id != null ? ItemTable.Items.FirstOrDefault(x => x.ID == id) as ItemTableEntry : null;
            return e != null ? e.BasePoints : 0;
        }

        static string NameOf(string id)
        {
            var e = id != null ? ItemTable.Items.FirstOrDefault(x => x.ID == id) : null;
            return e != null ? e.Name : "peca";
        }

        static int CarLevel(int car, int grade)
        {
            int need = Rice.Game.Character.GetRequiredLevelForTier(grade);
            VehicleListEntry v;
            if (VehicleTable.Vehicles.TryGetValue(car, out v) && v != null && v.IsSpecialCategory())
                need = Math.Max(need, Math.Min(90, RiceServer.Config.SpecialCarsMinLevel));
            return need;
        }

        // o bot tem nivel para usar e e melhor para ele?
        static bool Qualifies(Bots.BotProfile b, Auction.Deal d)
        {
            if (b == null || b.IsAlt || b.Special || Bots.IsAce(b)) return false;
            if (d.IsCar)
            {
                if (Bots.Unsupported(d.CarType) || b.Level < CarLevel(d.CarType, d.Grade)) return false;
                return CarPower(d.CarType, d.Grade) > CarPower(b.Car, BotSocial.GradeOf(b));
            }
            var e = ItemTable.Items.FirstOrDefault(x => x.ID == d.ItemId) as ItemTableEntry;
            if (e == null || !BotSocial.PartCats.Contains(e.Category)) return false;
            if (b.Level < Auction.ItemLevel(d.ItemId)) return false;
            string s1, s2;
            b.Parts.TryGetValue(e.Category, out s1); b.Parts.TryGetValue(e.Category + "#2", out s2);
            return s1 == null || s2 == null || e.BasePoints > Math.Min(Points(s1), Points(s2));
        }

        static void Give(Bots.BotProfile b, Auction.Deal d)
        {
            if (d.IsCar) { b.Car = d.CarType; b.CarGrade = Math.Max(1, d.Grade); return; }
            var e = ItemTable.Items.FirstOrDefault(x => x.ID == d.ItemId) as ItemTableEntry;
            if (e == null) return;
            string k1 = e.Category, k2 = e.Category + "#2", s1, s2;
            b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
            string slot = s1 == null ? k1 : s2 == null ? k2 : (Points(s1) <= Points(s2) ? k1 : k2);
            b.Parts[slot] = e.ID;
        }

        static void TryBotBuy(Offer o)
        {
            if (o == null || o.Done || o.Deal == null) return;
            if (!Auction.OnSaleNow(o.Deal.Serial)) { o.Done = true; return; }
            if (Auction.IsHeld(o.Deal.Serial)) { Later(30000, () => TryBotBuy(o)); return; }
            var talkers = Talkers().Where(x => x != o.Bot).OrderBy(x => Rng.Next()).ToList();
            var buyer = talkers.FirstOrDefault(x => Qualifies(x, o.Deal) && x.Mito >= o.Deal.Price);
            o.Done = true;
            if (buyer == null)
            {
                // alguem queria, mas falta mito ou nivel: vai atras disso
                var fan = talkers.FirstOrDefault(x => string.IsNullOrEmpty(x.GoalKind) && Wants(x, o.Deal));
                if (fan != null)
                {
                    int need = o.Deal.IsCar ? CarLevel(o.Deal.CarType, o.Deal.Grade) : Auction.ItemLevel(o.Deal.ItemId);
                    if (fan.Level < need)
                        SetGoal(fan, "nivel", o.Deal.Name, o.Deal.Price, need, o.Deal.ItemId, o.Deal.Serial, "Quero o " + o.Deal.Name + ", mas pede nivel " + need + ". Vou upar e volto!");
                    else
                        SetGoal(fan, "mito", o.Deal.Name, o.Deal.Price, 0, o.Deal.ItemId, o.Deal.Serial,
                                "Queria o " + o.Deal.Name + ", mas so tenho " + Math.Max(0, fan.Mito).ToString("N0") + " mito. Vou fazer umas corridas pra juntar!");
                    return;
                }
                if (Rng.Next(2) == 0) SayAll(0, "channel", o.Bot, "Ninguem levou o " + o.Deal.Name + "? Continua no leilao.");
                return;
            }
            BuyDeal(buyer, o.Deal, o.Bot);
        }

        // o comprador vai ate o leilao do Driver Dome, espera 3 s, compra e volta; fora do lobby, compra na hora
        static void BuyDeal(Bots.BotProfile buyer, Auction.Deal deal, Bots.BotProfile seller)
        {
            Tell(buyer, Pick("Vou no leilao comprar o " + deal.Name + "!", "Esse " + deal.Name + " e meu, to indo no leilao!"));
            bool[] done = { false };
            Action buy = () =>
            {
                lock (done) { if (done[0]) return; done[0] = true; }
                if (buyer.Mito < deal.Price) { Tell(buyer, "Opa, faltou mito pro " + deal.Name + "..."); return; }
                if (!Auction.BotTake(deal.Serial, buyer)) { Tell(buyer, "Ih, ja venderam o " + deal.Name + "..."); return; }
                buyer.Mito -= deal.Price;
                if (seller != null) seller.Mito = Math.Max(0, seller.Mito) + deal.Price;
                Give(buyer, deal);
                Bots.Save();
                Tell(buyer, Pick("Comprei o " + deal.Name + "!" + (seller != null ? " Valeu, " + seller.Name + "." : ""), "Levei o " + deal.Name + ", ja equipei!"));
                if (seller != null) Tell(seller, Pick("Vendido! Valeu, " + buyer.Name + ".", "Boa compra, " + buyer.Name + "!"), 2500);
            };
            if (CityBots.Errand(buyer, "leilao", buy, 4)) Later(150000, buy);   // se nao chegar, compra assim mesmo
            else Later(3000, buy);
        }

        // quer (e melhor para ele), mesmo sem nivel/mito agora
        static bool Wants(Bots.BotProfile b, Auction.Deal d)
        {
            if (b == null || b.IsAlt || b.Special || Bots.IsAce(b)) return false;
            if (d.IsCar) return !Bots.Unsupported(d.CarType) && CarPower(d.CarType, d.Grade) > CarPower(b.Car, BotSocial.GradeOf(b));
            var e = ItemTable.Items.FirstOrDefault(x => x.ID == d.ItemId) as ItemTableEntry;
            if (e == null || !BotSocial.PartCats.Contains(e.Category)) return false;
            string s1, s2;
            b.Parts.TryGetValue(e.Category, out s1); b.Parts.TryGetValue(e.Category + "#2", out s2);
            return s1 == null || s2 == null || e.BasePoints > Math.Min(Points(s1), Points(s2));
        }

        // ---- item no chat (so conversa) ------------------------------------------------------------------------
        static void ItemTalk(Bots.BotProfile a, List<Bots.BotProfile> others)
        {
            var e = Auction.RewardItem(a.Level);
            if (e == null) return;
            SayAll(0, "channel", a, Pick("Vendo " + e.Name + ", chama no PV.", "Alguem tem " + e.Name + " pra trocar?", "Troco " + e.Name + " por algo melhor."));
            int delay = 2000;
            foreach (var b in others.Take(Rng.Next(0, 3)))
            {
                delay += 1500 + Rng.Next(2500);
                SayAll(delay, "channel", b, Pick("Quanto?", "Tenho interesse!", "Ja tenho uma, valeu."));
            }
        }

        // ---- crew: anuncio de vaga com requisito ----------------------------------------------------------------
        static void CrewCall()
        {
            var crews = Crew.All().Where(c => c.IsBot && c.Members.Count > 0).ToList();
            if (crews.Count == 0) return;
            var elite = crews.FirstOrDefault(c => c.Name == Bots.EliteCrew);
            var t = elite != null && Rng.Next(3) == 0 ? elite : crews[Rng.Next(crews.Count)];
            var who = t.Members.Select(Bots.ByCid).Where(b => b != null && !b.IsAlt && BotSocial.Online(b)).OrderBy(x => Rng.Next()).FirstOrDefault();
            if (who == null) return;
            string req = t.MinLevel > 1 ? "nivel " + t.MinLevel + " ou mais" : "qualquer nivel";
            SayAll(0, "channel", who, Pick("A crew " + t.Name + " esta recrutando! Requisito: " + req + ".",
                                         "Vagas na " + t.Name + " (" + req + "). Quem quer entrar?") + " (responda sim para entrar)");
            var o = new Offer { Kind = "crew", Bot = who, Crew = t, Until = OfferUntil() };
            lock (offers) offers.Add(o);
            // um bot que cumpre o requisito pode entrar (sai da crew antiga, se a nova for mais forte)
            var cand = Talkers().Where(b => !b.Special && !Bots.IsAce(b) && b.Level >= t.MinLevel && b.CrewName != t.Name)
                                .Where(b => { var cur = Crew.Of(b.Cid); return cur == null || cur.MinLevel < t.MinLevel; })
                                .OrderBy(x => Rng.Next()).FirstOrDefault();
            if (cand == null || Rng.Next(2) == 0) return;
            int d = 4000 + Rng.Next(6000);
            SayAll(d, "channel", cand, Pick("Eu quero! Tenho nivel " + cand.Level + ".", "Me coloca na " + t.Name + "!"));
            Later(d + 2500, () =>
            {
                var old = Crew.Of(cand.Cid);
                if (old != null) Crew.RemoveMember(old, cand.Cid);
                Crew.AddMember(t, cand.Cid);
                cand.CrewName = t.Name; cand.CrewTid = t.Tid;
                Bots.Save();
                SayAll(0, "channel", who, "Bem-vindo a " + t.Name + ", " + cand.Name + "!");
                Log.WriteLine("Crew: {0} entrou na {1}.", cand.Name, t.Name);
            });
        }

        // ---- troca entre dois bots ------------------------------------------------------------------------------
        // Quem aceitou recebe so o que e melhor e o que tem nivel para usar; quem propos vai ate ele (para de frente).
        static void BotTrade(Bots.BotProfile a, Bots.BotProfile b)
        {
            if (a == null || b == null || a.IsAlt || b.IsAlt) return;
            if (b.Special || Bots.IsAce(b)) return;              // bots fixos tem pecas fixas
            SayAll(0, "channel", a, Pick(b.Name + ", bora uma troca?", "Ei " + b.Name + ", troca comigo?"));
            int kind = Rng.Next(4);
            if (kind == 0 && !a.Special && !Bots.IsAce(a)) { TradeCar(a, b); return; }
            if (kind == 1 && a.Visuals != null && a.Visuals.Count > 0 && !a.Special && !Bots.IsAce(a)) { TradeVisual(a, b); return; }
            TradePart(a, b);
        }

        static void Accepted(Bots.BotProfile a, Bots.BotProfile b, string text)
        {
            Bots.Meet(a, b, 25);                                  // vai ate quem aceitou
            Bots.Save();
            SayAll(2500, "channel", b, text);
        }

        static void TradeCar(Bots.BotProfile a, Bots.BotProfile b)
        {
            int ga = BotSocial.GradeOf(a), gb = BotSocial.GradeOf(b);
            bool better = CarPower(a.Car, ga) > CarPower(b.Car, gb);
            bool level = b.Level >= CarLevel(a.Car, ga) && a.Level >= CarLevel(b.Car, gb);   // GradeOf ja e no minimo o V do carro
            if (better && level && !Bots.Unsupported(a.Car) && !Bots.Unsupported(b.Car))
            {
                int ca = a.Car, cb = b.Car;
                a.Car = cb; a.CarGrade = gb; b.Car = ca; b.CarGrade = ga;
                Accepted(a, b, "Fechado! " + Bots.CarName(ca) + " V" + ga + " pelo meu " + Bots.CarName(cb) + " V" + gb + ", valeu " + a.Name + "!");
                Log.WriteLine("Bots: {0} e {1} trocaram de carro.", a.Name, b.Name);
            }
            else if (!level)
                SayAll(2500, "channel", b, "Nao tenho nivel para esse carro ainda. Cancelado.");
            else
                SayAll(2500, "channel", b, Pick("V" + ga + "? O meu e melhor, ta me tirando? Cancelei.", "Nem pensar, o meu carro e melhor. Cancelado!"));
        }

        static void TradeVisual(Bots.BotProfile a, Bots.BotProfile b)
        {
            var kv = a.Visuals.ElementAt(Rng.Next(a.Visuals.Count));
            if (b.Visuals == null) b.Visuals = new Dictionary<string, int>();
            if (b.Visuals.ContainsKey(kv.Key))
            {
                SayAll(2500, "channel", b, "Ja tenho " + kv.Key + " no meu carro, nao preciso. Cancelado.");
                return;
            }
            b.Visuals[kv.Key] = kv.Value;
            a.Visuals.Remove(kv.Key);
            Accepted(a, b, "Fechado! Ja coloquei o visual (" + kv.Key + ") no carro.");
            Log.WriteLine("Bots: {0} deu o visual {1} para {2}.", a.Name, kv.Value, b.Name);
        }

        static void TradePart(Bots.BotProfile a, Bots.BotProfile b)
        {
            var give = Auction.RewardItem(Math.Max(a.Level, 1));
            var ge = give as ItemTableEntry;
            if (ge == null || !BotSocial.PartCats.Contains(ge.Category))
            {
                SayAll(2500, "channel", b, "Nao tenho nada pra trocar agora, depois.");
                return;
            }
            if (b.Level < Auction.ItemLevel(ge.ID))
            {
                SayAll(2500, "channel", b, ge.Name + " pede nivel " + Auction.ItemLevel(ge.ID) + ", ainda nao da para mim. Cancelado.");
                return;
            }
            string k1 = ge.Category, k2 = ge.Category + "#2", s1, s2;
            b.Parts.TryGetValue(k1, out s1); b.Parts.TryGetValue(k2, out s2);
            string slot = s1 == null ? k1 : s2 == null ? k2 : (Points(s1) <= Points(s2) ? k1 : k2);
            string old; b.Parts.TryGetValue(slot, out old);
            if (old == null || ge.BasePoints > Points(old))
            {
                b.Parts[slot] = ge.ID;
                Accepted(a, b, "Fechado! " + ge.Name + " (+" + (int) ge.BasePoints + ")" + (old != null ? " no lugar da " + NameOf(old) + " (+" + (int) Points(old) + ")" : "") + ". Ja equipei!");
                Log.WriteLine("Bots: {0} deu {1} para {2}.", a.Name, ge.ID, b.Name);
            }
            else
                SayAll(2500, "channel", b, Pick(ge.Name + "? A minha e melhor (+" + (int) Points(old) + "). Cancelei!",
                                              "Isso e pior que a minha " + NameOf(old) + ", me estressou. Cancelado!"));
        }
    }
}
