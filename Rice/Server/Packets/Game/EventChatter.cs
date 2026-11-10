using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    // Os bots falam do evento especial do momento (Natal, Halloween, Valentine), cada um do jeito dele (personalidade),
    // e os outros reagem. Quando voce fala do evento no chat (feliz natal, halloween...), alguns bots respondem.
    public static class EventChatter
    {
        static readonly Random Rng = new Random();
        static string Pick(string[] s) { lock (Rng) return s[Rng.Next(s.Length)]; }
        static int Next(int n) { lock (Rng) return Rng.Next(n); }

        // evento ativo agora ("natal", "halloween", "valentine" ou null)
        public static string Current()
        {
            string e = Static.EventoAtivo(RiceServer.Config.Evento, DateTime.Now);
            return e == "natal" || e == "halloween" || e == "valentine" ? e : null;
        }

        // falas que abrem a conversa: evento -> personalidade -> frases ({n} = nome de quem fala, {p} = jogador)
        static readonly Dictionary<string, Dictionary<string, string[]>> Open = new Dictionary<string, Dictionary<string, string[]>>
        {
            {"natal", new Dictionary<string, string[]> {
                {"amigavel", new[] { "Feliz Natal, pessoal! Moon Palace ta linda com essa neve.", "Ho ho ho! Quem quer dar uma volta no meio da neve?",
                                     "Ja montaram a arvore de Natal? A minha ta cheia de pecas de presente kkk", "Natal com a galera do Drift City nao tem preco. Feliz Natal!",
                                     "Essa musica de Natal no lobby nao sai da minha cabeca..." }},
                {"competitivo", new[] { "Meu presente de Natal vai ser ganhar de todo mundo na Battle Zone.", "Neve na pista? Melhor ainda, quero ver quem segura o drift.",
                                        "Papai Noel me trouxe um booster novo. Quem quer correr?", "Feliz Natal! Mas na corrida nao tem presente, so derrota pra voces." }},
                {"comerciante", new[] { "Promocao de Natal! Pecas com desconto, chama no PV.", "Natal chegando e eu com o estoque cheio. Quem quer presente?",
                                        "Trocando pecas pra fechar o ano. Feliz Natal!", "Papai Noel nao trouxe? Eu vendo, chama no PV." }},
                {"calado", new[] { "feliz natal", "neve...", "bonita a cidade assim", "ho ho ho" }},
            }},
            {"halloween", new Dictionary<string, string[]> {
                {"amigavel", new[] { "Feliz Halloween, galera! Doces ou travessuras?", "Alguem mais achou a cidade meio assombrada hoje?",
                                     "Vou fantasiado de HUV esse Halloween kkk", "Halloween e corrida de madrugada, combinacao perfeita!" }},
                {"competitivo", new[] { "Halloween e o mes de dar susto: vou passar voces na reta final.", "Quem tem medo de perder? Me chama pra Battle Zone.",
                                        "Os HUVs tao mais assustadores que voces na pista.", "Travessura e o que eu vou fazer com o seu tempo de volta." }},
                {"comerciante", new[] { "Promocao de Halloween: pecas assombradas de baratas kkk chama no PV.", "Doce ou travessura? Prefiro mito. Vendendo pecas!",
                                        "Halloween chegou e eu com estoque de arrepiar. PV!" }},
                {"calado", new[] { "boo", "feliz halloween", "...assustador", "doces?" }},
            }},
            {"valentine", new Dictionary<string, string[]> {
                {"amigavel", new[] { "Feliz Valentine, pessoal! Quem vai dar um passeio com o par?", "Mes do amor! Vou levar meu carro pra jantar kkk",
                                     "Valentine e o melhor mes pra correr em dupla.", "Ja deram chocolate pra alguem hoje?" }},
                {"competitivo", new[] { "Meu unico amor e o primeiro lugar.", "Valentine? Vou partir o coracao de quem correr comigo.",
                                        "Quem ama, corre. Quem perde, chora. Bora Battle Zone?", "Amor de verdade e um drift perfeito na curva." }},
                {"comerciante", new[] { "Presente de Valentine? Tenho pecas perfeitas pro seu par. PV!", "Promocao do mes do amor: duas pecas, preco de uma kkk",
                                        "Valentine chegou: troco chocolate por mito." }},
                {"calado", new[] { "feliz valentine", "<3", "chocolate?", "..." }},
            }},
        };

        // respostas dos outros bots: evento -> frases
        static readonly Dictionary<string, string[]> React = new Dictionary<string, string[]>
        {
            {"natal", new[] { "Feliz Natal, {n}!", "Feliz Natal pra voce tambem!", "Ho ho ho!", "Kkkk feliz Natal!", "A neve ta demais mesmo.",
                              "Boas festas, {n}!", "Ja quero meu presente kkk", "Natal na pista e outra coisa!" }},
            {"halloween", new[] { "Feliz Halloween, {n}!", "Booo!", "Doce! Doce!", "Kkkk travessura!", "Que medo, {n}...", "Bora uma corrida assombrada?" }},
            {"valentine", new[] { "Feliz Valentine, {n}!", "<3", "Que fofo, {n} kkk", "Eu so amo meu carro mesmo.", "Chocolate pra todo mundo!", "Feliz dia do amor!" }},
        };

        // o jogador falou do evento no chat
        static readonly Regex PlayerSays = new Regex(@"\b(natal|christmas|xmas|ho ho|noel|boas festas|halloween|doces? ou travessuras?|trick or treat|boo+|valentine|namorad|dia do amor|chocolate)\b",
                                                     RegexOptions.IgnoreCase);
        static readonly Dictionary<string, string[]> Answer = new Dictionary<string, string[]>
        {
            {"natal", new[] { "Feliz Natal, {p}!", "Feliz Natal pra voce tambem, {p}!", "Ho ho ho, {p}! Boas festas!", "Valeu, {p}! Feliz Natal!",
                              "Feliz Natal! Bora uma corrida de Natal, {p}?" }},
            {"halloween", new[] { "Feliz Halloween, {p}!", "Booo! Feliz Halloween, {p}!", "Doce ou travessura, {p}?", "Kkkk feliz Halloween, {p}!" }},
            {"valentine", new[] { "Feliz Valentine, {p}!", "<3 Feliz Valentine, {p}!", "Valeu, {p}! Feliz mes do amor!", "Chocolate pra voce, {p} kkk" }},
        };

        static string Fill(string s, string n, string p) { return s.Replace("{n}", n ?? "").Replace("{p}", p ?? ""); }

        // conversa sobre o evento (chamado pelo BotChatter); false = nao tem evento agora
        public static bool Talk(Bots.BotProfile a, List<Bots.BotProfile> others, Action<int, Bots.BotProfile, string> say)
        {
            string ev = Current();
            if (ev == null || a == null) return false;
            var byP = Open[ev];
            string[] lines;
            if (!byP.TryGetValue(a.Personality ?? "", out lines)) lines = byP["amigavel"];
            say(0, a, Fill(Lang.T(Pick(lines)), a.Name, ""));
            int delay = 2000;
            foreach (var b in others.Take(Next(4)))
            {
                delay += 1500 + Next(3000);
                string[] l2;
                // quem e calado responde curto; os outros reagem a quem falou
                string line = b.Personality == "calado" && byP.TryGetValue("calado", out l2) ? Lang.T(Pick(l2)) : Fill(Lang.T(Pick(React[ev])), a.Name, "");
                say(delay, b, line);
            }
            return true;
        }

        // o jogador falou do evento: 1 a 3 bots respondem
        public static void OnPlayerChat(string playerName, string message, List<Bots.BotProfile> bots, Action<int, Bots.BotProfile, string> say)
        {
            string ev = Current();
            if (ev == null || string.IsNullOrWhiteSpace(message) || !PlayerSays.IsMatch(message) || bots == null || bots.Count == 0) return;
            int delay = 0;
            foreach (var b in bots.OrderBy(x => Next(1000)).Take(1 + Next(3)))
            {
                delay += 1200 + Next(2500);
                say(delay, b, Fill(Lang.T(Pick(Answer[ev])), b.Name, playerName));
            }
        }
    }
}
