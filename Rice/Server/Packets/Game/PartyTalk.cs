using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rice.Server.Core;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    // Conversa da party com voce:
    //  - Discussao: parado 3 minutos (PartyArgueSeconds) com 2 ou 3 de Joaopvbr, Marsteh e Brunizin.X na party, eles
    //    discutem no chat local (balaozinho em cima do carro), 10 falas, 2 a 8 s entre elas. Marsteh se acha o melhor,
    //    Brunizin.X o esperto, Joaopvbr e o estressado.
    //  - Sugestoes: andando a toa ou parado, um membro sugere algo (corrida, HUV, Rush, Undercity, entrega, uma peca
    //    que colocou, uma peca que queria). Espera 5 minutos (PartySuggestSeconds) entre uma e outra.
    //  - Elogios: missao, entrega, patrulha, HUV, Rush, Undercity e corrida concluidos.
    public static class PartyTalk
    {
        static Timer timer;
        static readonly Random Rng = new Random();
        class Mood { public DateTime LastSuggest = DateTime.UtcNow; public bool Argued; public DateTime LastPraise = DateTime.MinValue; public bool Arguing; }
        static readonly Dictionary<ulong, Mood> moods = new Dictionary<ulong, Mood>();

        public static void Start()
        {
            if (timer != null) return;
            timer = new Timer(_ => { try { Tick(); } catch (Exception ex) { Log.WriteError("Conversa da party: " + ex.Message); } }, null, 10000, 5000);
        }

        static Mood MoodOf(Rice.Game.Character ch)
        {
            lock (moods)
            {
                Mood m;
                if (!moods.TryGetValue(ch.CID, out m)) { m = new Mood(); moods[ch.CID] = m; }
                return m;
            }
        }

        static string Pick(params string[] s) { return s[Rng.Next(s.Length)]; }

        // fala no chat local (balaozinho) em nome de um bot
        public static void SayArea(Rice.Game.Player p, string name, string text)
        {
            var c = p != null ? p.AreaClient : null;
            if (c == null) { if (p != null && p.GameClient != null) Bots.Say(p.GameClient, "channel", name, text); return; }
            var pk = new RicePacket(571);
            pk.Writer.WriteUnicodeStatic("area", 10);
            pk.Writer.WriteUnicodeStatic(name, 18);
            pk.Writer.WriteUnicode(Lang.T(text));
            try { c.Send(pk); } catch (Exception) { }
        }

        // ---- limpeza do chat ----------------------------------------------------------------------------------
        // O jogo nao tem comando para o servidor apagar o chat. A cada ChatResetMinutes, linhas vazias empurram as
        // falas antigas para fora da janela (o chat do jogo guarda um numero limitado de linhas) e volta a dica do !!help.
        static DateTime nextReset = DateTime.UtcNow.AddMinutes(15);

        static void ResetChat()
        {
            var cfg = RiceServer.Config;
            if (cfg.ChatResetMinutes <= 0) return;
            if (DateTime.UtcNow < nextReset) return;
            nextReset = DateTime.UtcNow.AddMinutes(Math.Max(1, cfg.ChatResetMinutes));
            foreach (var p in RiceServer.GetPlayers().Where(x => x != null && x.ActiveCharacter != null && x.GameClient != null).ToList())
            {
                var c = p.GameClient;
                try
                {
                    for (int i = 0; i < Math.Max(1, cfg.ChatResetLines); i++)
                    {
                        var pk = new RicePacket(147);
                        pk.Writer.WriteUnicodeStatic("channel", 10);
                        pk.Writer.WriteUnicodeStatic("", 18);
                        pk.Writer.WriteUnicode(" ");
                        c.Send(pk);
                    }
                    Chat.SendSystemMessage(c, "Chat reiniciado. Digite !!help no chat para ver todos os comandos.");
                }
                catch (Exception) { }
            }
            Log.WriteLine("Chat reiniciado ({0} linhas).", cfg.ChatResetLines);
        }

        static void Tick()
        {
            var cfg = RiceServer.Config;
            ResetChat();
            if (!cfg.PartyTalk) return;
            foreach (var p in RiceServer.GetPlayers().Where(x => x != null && x.ActiveCharacter != null && x.GameClient != null).ToList())
            {
                var ch = p.ActiveCharacter;
                var party = BotSocial.PartyBots(ch).Where(b => !b.IsAlt).ToList();
                if (party.Count == 0) continue;
                var mood = MoodOf(ch);
                double still = CityBots.StillSeconds(ch);
                bool busy = CityBots.Busy(ch) || ch.Quest != null || Missions.Active(ch) || Rush.Running(ch) || UnderCity.InRun(ch);
                if (busy) mood.LastSuggest = DateTime.UtcNow;   // acabou de fazer algo: espera de novo antes de sugerir
                if (still < 5) mood.Argued = false;
                // discussao dos tres
                var trio = party.Where(b => b.Name == "Marsteh" || b.Name == "Brunizin.X" || b.Name == "Joaopvbr").ToList();
                if (!busy && trio.Count >= 2 && !mood.Argued && !mood.Arguing && still >= Math.Max(30, cfg.PartyArgueSeconds))
                {
                    mood.Argued = true;
                    Argue(p, mood, trio);
                    continue;
                }
                // sugestao (a cada 5 minutos, quando voce nao esta fazendo nada)
                if (!busy && !mood.Arguing && (DateTime.UtcNow - mood.LastSuggest).TotalSeconds >= Math.Max(60, cfg.PartySuggestSeconds)
                    && (DateTime.UtcNow - mood.LastPraise).TotalSeconds >= 60)
                {
                    mood.LastSuggest = DateTime.UtcNow;
                    var b = party[Rng.Next(party.Count)];
                    Bots.SayLater(p.GameClient, 500, "party", b.Name, Suggestion(b, ch));
                }
            }
        }

        static string PartName(Bots.BotProfile b)
        {
            if (b.Parts == null || b.Parts.Count == 0) return null;
            var id = b.Parts.Values.ElementAt(Rng.Next(b.Parts.Count));
            var e = ItemTable.Items.FirstOrDefault(x => x.ID == id);
            return e != null ? e.Name : null;
        }

        static string Suggestion(Bots.BotProfile b, Rice.Game.Character ch)
        {
            string part = PartName(b);
            var l = new List<string>
            {
                "Que tal uma corrida? Cria a sala na Battle Zone que a gente entra.",
                "Bora cacar HUV? Liga a sirene que eu vou na frente.",
                "Rush Time! Bora derrubar o boss juntos?",
                "Ta parado ai, " + ch.Name + "? Bora pra Undercity!",
                "Vamos fazer umas entregas? Da um dinheiro bom.",
                "Que tal uma patrulha? Eu pego os pontos do outro lado.",
                "Queria uma peca melhor pro meu carro... se ver no leilao, me avisa!",
                "Bora la no leilao ver as promocoes?",
            };
            if (part != null)
            {
                l.Add("Coloquei " + part + " no meu carro, ficou bom demais!");
                l.Add("Esse " + part + " ta me ajudando muito. Bora testar numa corrida?");
            }
            if (b.Name == "Marsteh") l.Add("Eu, o melhor, sugiro uma corrida. Voce vai ver como se faz.");
            if (b.Name == "Brunizin.X") l.Add("Tenho um plano esperto: HUV agora, que o bonus ta alto.");
            if (b.Name == "Joaopvbr") l.Add("Parado ai me estressa! Bora fazer alguma coisa!");
            return l[Rng.Next(l.Count)];
        }

        // ---- elogios ------------------------------------------------------------------------------------------
        public static void Praise(Rice.Game.Character ch, string what)
        {
            try
            {
                if (ch == null) return;
                BotChatter.HelpProgress(ch, what);                  // bot que voce esta ajudando conta o progresso
                if (!RiceServer.Config.PartyTalk) return;
                var p = RiceServer.GetPlayers().FirstOrDefault(x => x != null && x.ActiveCharacter == ch);
                if (p == null || p.GameClient == null) return;
                var party = BotSocial.PartyBots(ch).Where(b => !b.IsAlt).OrderBy(x => Rng.Next()).ToList();
                if (party.Count == 0) return;
                var mood = MoodOf(ch);
                if ((DateTime.UtcNow - mood.LastPraise).TotalSeconds < 15) return;
                mood.LastPraise = DateTime.UtcNow;
                int delay = 1500 + Rng.Next(2000);
                var a = party[0];
                Bots.SayLater(p.GameClient, delay, "party", a.Name, Pick(
                    "Mandou bem no " + what + ", " + ch.Name + "!", "Isso ai! " + what + " concluido com sucesso!",
                    "Boa, " + ch.Name + "! Que " + what + " lindo.", "Equipe boa e outra coisa. " + what + " feito!"));
                if (party.Count > 1 && Rng.Next(2) == 0)
                {
                    var b = party[1];
                    string extra = b.Name == "Marsteh" ? "Foi bom, mas eu teria feito melhor." :
                                   b.Name == "Brunizin.X" ? "Foi o meu plano que deu certo, ne?" :
                                   b.Name == "Joaopvbr" ? "Ufa! Ja tava ficando estressado. Boa!" :
                                   Pick("Bora de novo?", "Valeu pela ajuda!", "Proximo!");
                    Bots.SayLater(p.GameClient, delay + 2000 + Rng.Next(3000), "party", b.Name, extra);
                }
            }
            catch (Exception ex) { Log.WriteError("Elogio da party: " + ex.Message); }
        }

        // ---- discussao ------------------------------------------------------------------------------------------
        // M = Marsteh (o melhor), B = Brunizin.X (o esperto), J = Joaopvbr (o estressado)
        static readonly string[][] ScriptsM =
        {
            new[] { "M:{J}, {B}, vao chupar um prego! Quem anda nessa party sou eu, o melhor.",
                    "J:Vai cagar, {M}. Ta parado igual poste e ainda fala!",
                    "B:Calma, gente. O mais esperto aqui ja sabe quem ganha: eu.",
                    "M:Esperto? Tu errou a curva da ponte tres vezes ontem.",
                    "B:Era estrategia. Deixei voces acharem que tinham chance.",
                    "J:Ah, para! Eu ja to estressado, toda vez essa conversa!",
                    "M:Estressado porque perdeu pro meu carro, ne?",
                    "J:Perdi nada! Meu Dragoon tava sem nitro, so isso.",
                    "B:Desculpa de quem perdeu. Eu avisei que era esperto.",
                    "M:Chega. O melhor ja falou. Bora andar que eu to com fome de vitoria." },
            new[] { "M:{B}, vai chupar um prego! Tu nem sabe trocar de marcha.",
                    "B:Engracado, eu troco de marcha pensando. Ja tu...",
                    "M:Eu nem preciso pensar, eu sou o melhor naturalmente.",
                    "B:O melhor em perder o Rush pro boss, ne?",
                    "M:Aquilo foi lag. Lag conta.",
                    "B:Claro, claro. Lag que so acontece contigo.",
                    "M:Quer apostar uma corrida agora?",
                    "B:So se for valendo o teu carro.",
                    "M:Esperto voce, hein. Nao vou cair nessa.",
                    "B:Viu? Esperto e outra coisa." },
            new[] { "M:{J}, vai chupar um prego! Esse Dragoon e lindo, pena o piloto.",
                    "J:Vai cagar, {M}! Eu to calmo, NAO me provoca!",
                    "M:Calmo? Tu buzina ate pro sinal vermelho.",
                    "J:Porque o sinal fica vermelho so pra mim! Ja reparou?",
                    "M:Reparei que eu chego primeiro sempre.",
                    "J:Chega primeiro porque corta caminho pela calcada!",
                    "M:Isso se chama talento.",
                    "J:Isso se chama trapaca! Aff, me estressou.",
                    "M:Respira, Joao. O melhor aqui te perdoa.",
                    "J:Perdoa nada. Proxima corrida eu te passo, anota ai." },
            new[] { "M:{J}, {B}, vao chupar um prego! Ninguem aqui tem o meu estilo.",
                    "B:Estilo de quem compra tudo no leilao caro, ne? Eu compro na promocao.",
                    "J:Eu nem ligo pra estilo, eu ligo pra ganhar! E voces me atrasam!",
                    "M:Atrasam? Eu sou o mais rapido daqui.",
                    "B:O mais rapido em falar, isso sim.",
                    "J:Para os dois! Cada minuto parado e um HUV fugindo!",
                    "M:HUV foge de medo de mim.",
                    "B:Foge de rir de voce, isso sim.",
                    "J:AAAH! Eu vou sozinho entao!",
                    "M:Vai, que eu chego depois e levo o credito. Coisa de melhor." },
        };

        static readonly string[][] ScriptsJ =
        {
            new[] { "J:{M}, vai cagar! Para de buzinar atras de mim!",
                    "M:Buzino porque eu sou o melhor e tu ta na minha frente, coisa rara.",
                    "J:Rara? Eu ganhei as tres ultimas!",
                    "M:Contra os bots do tutorial, ne?",
                    "J:AAAH! Eu vou explodir esse carro!",
                    "M:Explode, mas depois. Agora eu to posando pra foto.",
                    "J:Ninguem quer foto tua, {M}!",
                    "M:Todo mundo quer. Sou o melhor de Moon Palace.",
                    "J:Melhor em falar besteira, so se for.",
                    "M:Ta nervoso porque sabe que e verdade." },
            new[] { "J:{M}, vai cagar! Bateu no meu Dragoon de novo!",
                    "M:Bati nada, tu que freou do nada.",
                    "B:Tecnicamente os dois erraram. Eu, o esperto, fiquei longe.",
                    "J:Ninguem te perguntou, {B}!",
                    "B:Nao precisava. Esperto fala sozinho.",
                    "M:O melhor nao bate. O melhor e... desviado.",
                    "J:Nem tu entende o que fala! Que estresse!",
                    "B:Proposta esperta: corrida, quem perder paga o combustivel.",
                    "M:Fechado. Vou ganhar, claro.",
                    "J:Vou ganhar de voces dois so de raiva!" },
            new[] { "J:{M}, vai cagar! Faz tres minutos que a gente ta parado aqui!",
                    "M:Parado nao, descansando. O melhor precisa descansar.",
                    "J:Descansando de que? Tu nem bateu no HUV hoje!",
                    "M:Deixei voces baterem, pra nao ficarem tristes.",
                    "J:Que raiva! Toda vez a mesma desculpa!",
                    "M:Nao e desculpa, e generosidade.",
                    "J:Generosidade e me deixar ganhar uma corrida, isso sim!",
                    "M:Ai ja e pedir demais.",
                    "J:Respira, Joao, respira... nao, nao da!",
                    "M:Calma. Um dia tu chega perto de mim." },
        };

        static readonly string[][] ScriptsB =
        {
            new[] { "B:{M}, {J}, vao cagar! Voces ficam parados e eu que penso em tudo.",
                    "M:Pensa em tudo e chega sempre em segundo, atras de mim.",
                    "J:Eu que chego em primeiro e voces ficam discutindo!",
                    "B:Chega em primeiro porque eu deixei. Plano esperto.",
                    "J:Que plano o que! Para de inventar!",
                    "M:O unico plano aqui e eu ser o melhor.",
                    "B:O plano e voces brigarem e eu ganhar o leilao enquanto isso.",
                    "J:Ah, e por isso que o preco subiu?! Me estressou!",
                    "M:Esperto mesmo, eu admito. Mas o melhor continuo sendo eu.",
                    "B:Deixa ele achar isso. Faz parte do plano." },
            new[] { "B:{M}, vai cagar! Ta ocupando duas vagas com esse carro.",
                    "M:O melhor carro merece duas vagas.",
                    "B:O melhor carro, com o pior piloto.",
                    "M:Repete isso numa corrida, esperto.",
                    "B:Repito. Ja calculei a curva da ponte, tu perde ali.",
                    "M:Calculou? Eu nem preciso de calculo, eu sou natural.",
                    "B:Natural mesmo e tu errar a curva.",
                    "M:Quem errou foi o Joaopvbr ontem, nao eu.",
                    "B:Mudou de assunto. Esperto que sou, percebi.",
                    "M:Tsc. Bora logo que eu quero provar quem e o melhor." },
            new[] { "B:{M}, {J}, vao cagar! Quem teve a ideia de parar aqui?",
                    "J:Nao fui eu! Eu odeio ficar parado!",
                    "M:Fui eu. O melhor escolhe onde a party para.",
                    "B:O melhor escolheu o lugar mais feio da cidade.",
                    "M:Feio? Daqui da pra ver o meu reflexo no vidro.",
                    "J:Eu nao aguento mais voces dois!",
                    "B:Ideia esperta: a gente vai pro Rush e para de brigar.",
                    "M:Boa ideia. Pena que nao foi minha.",
                    "J:Finalmente alguem falou algo que presta!",
                    "B:Sempre eu, ne? O esperto da party." },
        };

        static void Argue(Rice.Game.Player p, Mood mood, List<Bots.BotProfile> trio)
        {
            bool hasM = trio.Any(b => b.Name == "Marsteh"), hasB = trio.Any(b => b.Name == "Brunizin.X"), hasJ = trio.Any(b => b.Name == "Joaopvbr");
            // quem comeca: aleatorio entre os que podem (Joaopvbr e Brunizin.X so comecam com o Marsteh junto)
            var starts = new List<string[][]>();
            if (hasM) starts.Add(ScriptsM);
            if (hasJ && hasM) starts.Add(ScriptsJ);
            if (hasB && hasM) starts.Add(ScriptsB);
            if (starts.Count == 0) return;
            var set = starts[Rng.Next(starts.Count)];
            var ok = set.Where(sc => sc.All(l => (l[0] != 'M' || hasM) && (l[0] != 'B' || hasB) && (l[0] != 'J' || hasJ)
                                               && (!l.Contains("{B}") || hasB) && (!l.Contains("{J}") || hasJ))).ToList();
            if (ok.Count == 0) return;
            var script = ok[Rng.Next(ok.Count)];
            Func<char, string> who = r => r == 'M' ? "Marsteh" : r == 'B' ? "Brunizin.X" : "Joaopvbr";
            mood.Arguing = true;
            Log.WriteLine("Party: {0} comecou uma discussao ({1} falas).", who(script[0][0]), script.Length);
            int delay = 0;
            for (int i = 0; i < script.Length; i++)
            {
                string line = script[i];
                string name = who(line[0]);
                string text = Lang.T(line).Substring(2).Replace("{M}", "Marsteh").Replace("{B}", "Brunizin.X").Replace("{J}", "Joaopvbr");
                if (i > 0) delay += 2000 + Rng.Next(6001);
                bool last = i == script.Length - 1;
                int d = delay;
                Task.Delay(d).ContinueWith(_ =>
                {
                    try { SayArea(p, name, text); } catch (Exception) { }
                    if (last) mood.Arguing = false;
                });
            }
        }
    }
}
