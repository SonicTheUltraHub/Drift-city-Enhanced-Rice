using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Rice
{
    public class Config
    {
        const string configPath = "server.json";

        // Fecha o servidor sozinho quando o jogo fecha (DriftCity.exe fechado por CloseWithGameSeconds segundos).
        public bool CloseWithGame = true;
        public int CloseWithGameSeconds = 10;
        // Fecha o jogo quando o servidor fecha ("sair", o X da janela do servidor ou Ctrl+C), CloseGameDelaySeconds depois.
        public bool CloseGameWithServer = true;
        public int CloseGameDelaySeconds = 2;

        public bool DebugMode = true;            // mostra mais detalhes no console do servidor (bom para achar erros)
        public string PublicIP = "127.0.0.1";    // endereco que o servidor passa para o jogo (127.0.0.1 = este PC; nao mude jogando sozinho)

        public ushort AuthPort = 11005;          // porta do login (o jogo conecta aqui primeiro)
        public ushort LobbyPort = 11011;         // porta da selecao de corredor (lobby)
        public ushort GamePort = 11021;          // porta do jogo (inventario, lojas, missoes, chat)
        public ushort AreaPort = 11031;          // porta da area (carros andando na cidade)
        public ushort RankingPort = 11078;       // porta do ranking

        // Idioma do servidor (console, chat do jogo e menu): "en" = ingles (padrao) ou "pt" = portugues.
        public string Language = "en";

        // ---- Perfis / progressao (modo offline) ----
        // Quantidade total de corredores (slots) da conta, contando o perfil "upado".
        public int MaxPlayerProfiles = 3;
        // Perfil "upado" (nivel alto, carro forte, bilionario): fica sempre no 1o slot.
        public string UppedProfileName = "RiceAdmin";
        // Impede excluir o perfil upado pelo jogo.
        public bool ProtectUppedProfile = true;
        // Carro do perfil upado: 88 = BMW Patrol (carro de policia), grade 9 (V9).
        public int UppedCarID = 88;
        // false (padrao): a conta admin e montada uma vez so (carro, pecas, inventario, missoes); depois o que voce fizer
        // com ela fica. true: monta de novo toda vez que o servidor abre (tira os carros comprados, refaz o inventario).
        public bool UppedRefreshEveryStart = false;
        public int UppedCarGrade = 9;
        // Pecas equipadas nesse carro (2 de cada: velocidade, aceleracao, colisao, booster).
        // Variantes "_b" = Sorched (vermelhas); sem sufixo = normal; "_g" = outra cor.
        public string[] UppedPartIds = { "i_s_000020_b", "i_a_000020_b", "i_c_000020_b", "i_b_000020_b" };

        // ---- Perfil upado: garagem e inventario ----
        // BMW Patrol (carro de policia, id 88): recebe as mesmas pecas do Shadow. 0 = desligado.
        public int UppedPoliceCarID = 0;
        // "extras" = so os carros de UppedExtraCarIDs (padrao; evita travar em Loading data).
        // "dealership" = carros da concessionaria + BMW Patrol + Shadow (cerca de 99 carros).
        // "all" = todo carro que tem chave no jogo (cerca de 364; a garagem pode ficar pesada).
        public string UppedCarScope = "extras";
        // Carros extras sempre incluidos (alem da concessionaria).
        public int[] UppedExtraCarIDs = { };
        // Conta admin com um carro so (o UppedCarID, de policia): os outros carros do perfil upado saem da garagem.
        public bool UppedOnlyOneCar = true;
        // Opcoes no carro do admin (fileiras de docks U): Turbo Start (start booster) e Drift Booster. Vazio = nenhuma.
        public string[] UppedCarAssists = { "a_00002", "a_00247" };
        // Inventario: paginas preenchidas (a seguinte fica vazia) e espacos por pagina.
        // As chaves dos carros nao contam nesses espacos.
        public int UppedInvenPages = 1;
        public int UppedInvenPageSize = 24;
        // Quantidade dos itens empilhaveis (maximo 99).
        public int UppedStackSize = 99;
        // Expansoes de inventario/garagem enviadas ao cliente para o perfil upado (valores de teste).
        public int UppedInvenExpansion = 6;
        public int ShopMaxBuysPerVisit = 15;   // loja de carros: maximo de compras seguidas sem sair (0 = sem limite)
        public int UppedGarageExpansion = 8;   // andares extras da garagem (1F sempre aberto; 8 = abre ate 9F)
        // Enche o tanque de todos os carros do corredor ao entrar no jogo.
        public bool AutoRefuelOnEnter = true;
        // Multiplica o tamanho do tanque (e o combustivel enviado ao jogo). Quanto maior, mais o carro dura.
        // O console mostra "tanque cheio dura ~X min": ajuste este valor para chegar nas horas que quiser.
        public int FuelCapacityMultiplier = 1;

        // Tanque minimo enviado ao jogo, para qualquer carro (carros iniciais tem tanque pequeno).
        // Use 0 para desligar.
        public int FuelMinCapacity = 0;
        // Multiplica a "eficiencia" do carro enviada ao jogo (tabela do carro x este valor). 80 = o carro deve gastar ~80x menos.
        // Se o consumo AUMENTAR em vez de diminuir, use 0.0125 (= 1/80).
        public float FuelEfficiencyMultiplier = 15f;

        // ---- Posicao inicial de corredor novo (Station 1) ----
        // Coordenadas onde o corredor aparece na PRIMEIRA vez. Para mudar: va ate o local desejado,
        // olhe a linha "SaveCarPos" no console e copie os valores de pos (x, y, z).
        public int StartCity = 1;
        public float StartPosX = -3400.537f;
        public float StartPosY = 1257.549f;
        public float StartPosZ = 85.575f;

        // ---- Combos ----
        // A cada N combos, o jogador ganha ComboRewardMito.
        public int ComboRewardEvery = 20;
        public long ComboRewardMito = 50;
        public bool ComboRewardScale = false;     // true = o mito multiplica pelo marco (x1, x2...); false = sempre ComboRewardMito
        public int ComboRewardExp = 2;            // exp a cada marco de combo (20, 40, 60...)
        public int ComboExpPerCombo = 0;          // (antigo, nao usado)

        // Itens extras do combo (a cada marco de ComboRewardEvery): Booster Protector F/D alternados.
        // Mittron Enhancer: combo >= ComboEnhancerNAt = N; >= ComboEnhancerSAt = S; >= ComboEnhancerHAt = H;
        // >= ComboEnhancerUAt = "U" (nao existe enhancer U nos dados do jogo: vai o ComboEnhancerUItem, H DELUXE).
        public bool ComboItemRewards = true;
        public bool NeoCityEnter = true;
        public bool NeoHpEnabled = true;                       // HP maximo da Neo City = durabilidade total x NeoHpPerDurability
        public double NeoHpPerDurability = 10;                 //   + nivel x NeoHpPerLevel + NeoHpBase
        public double NeoHpPerLevel = 40;
        public int NeoBossRegenCount = 0;                      // Neo City: o servidor faz o chefe voltar N vezes (0 = o jogo mesmo pede de novo)
        public int NeoBossRegenSeconds = 10;                   //   ... depois de N segundos (tabela do jogo: 10 vezes, 10 s)
        public int NeoHuvRegenCount = 0;                       // HUVs normais: idem (0 = o jogo pede)
        public int NeoHuvRegenSeconds = 30;                    //   ... depois de N segundos
        public double NeoHuvExpPerLevel = 10;                  // Neo City: exp por HUV destruido = nivel do HUV x isto (HUV 60 = 600)
        public double NeoHuvMitoPerLevel = 8;                  // mito por HUV destruido = nivel do HUV x isto (HUV 60 = 480)
        public double NeoBossRewardMultiplier = 5;             // chefe: x isto
        public double NeoDropChance = 0.2;                    // chance de um HUV deixar cair arma/armadura/Amplifier/Fuse do seu grau
        public double NeoDropBossChance = 0.5;                 // chance no chefe
        public bool NeoChapterBonusItem = true;          // Neo City: fim do capitulo da 1 item da Neo do seu nivel (tela CHAPTER CLEARED)
        public bool NeoBots = true;                         // Neo City: cria os bots que moram so la (ficam parados pela base/lobby e entram na sua party)
        public int NeoBotsCount = 20;                       // quantos bots da Neo City (ate 20)
        public bool NeoDomeLearn = false;                   // base ROAD: false = os bots so usam o caminho gravado (neo_dome_points); true = continua aprendendo por onde voce anda
        public float NeoLobbyRadius = 250f;                 // ate quantos metros de onde voce aparece na Neo City os bots ficam parados
        public bool NeoBotsHelp = true;                     // bots da sua party atiram nos HUVs das missoes da Neo City
        // Durabilidade dos equipamentos da Neo City (arma, Amplifier, Fuse, armadura): o maximo vem da tabela (500 a 800).
        // Na Neo City cada peca montada perde NeoDurabilityPerMinute por minuto e a arma perde NeoDurabilityPerShot por tiro.
        // No 0 a peca sai do carro e so volta depois de reparar (Repair Kits da Neo City, na Drift Shop).
        public bool NeoDurabilityEnabled = true;
        // Drinks da Drift Shop (Drift Item): EXP drink = +DrinkExpBonus de exp, MITO drink = +DrinkMitoBonus de mito (fora as
        // recompensas de quest), Item Drink = cada item que cai do HUV vem DrinkItemDrops vezes. 0.3 = 30%.
        public double DrinkExpBonus = 0.30;
        public double DrinkMitoBonus = 0.50;
        public int DrinkItemDrops = 2;
        public double NeoDurabilityPerMinute = 1.0;
        public double NeoDurabilityPerShot = 0.2;
        public double NeoDurabilityPerHit = 1.0;            // armadura: perde isto a cada dano tomado na Neo City
        public float NeoBotEngageRange = 200f;              // Neo City: a party so vai atras dos HUVs a ate N m de voce
        public double NeoBotHpBase = 2500;                  // Neo City: vida do bot da party = isto + nivel x NeoBotHpPerLevel + armadura x 2
        public double NeoBotHpPerLevel = 75;
        public double NeoHuvDamagePerLevel = 6;             // dano de cada tiro do HUV no bot = nivel do HUV x isto (chefe x3), menos a armadura
        public int NeoBotRespawnSeconds = 20;               // bot destruido volta depois de N s, com a vida cheia
        public double NeoBaseRegenPercent = 5;              // Neo City: na base ROAD a vida dos bots volta N% por segundo
        public double NeoBotKitPercent = 75;                // bot usa um kit que voce deu quando alguem da party ficar abaixo de N% da vida
        public double NeoBotKitSeconds = 6;                 // intervalo minimo entre kits dos bots
        public double NeoBotStandoff = 15;                  // Neo City: bot da party COM arma para a N m do HUV que persegue e atira de la
        public double NeoBotHoldDistance = 35;              // Neo City: o bot com arma vai atras do HUV e so para (mirando) quando chega a N m dele
        public bool NeoBotHoldToShoot = true;               // Neo City: bot com arma e com HUV no alcance para o carro, vira para o HUV e atira
        public double NeoHuvHpPerLevel = 70;                // vida estimada do HUV (nivel x isto, chefe x5) quando o seu jogo ainda nao informou
        public double NeoBotRamDistance = 5;                // bot SEM arma da Neo City vai para cima do HUV e so tira vida encostado (ate N m)
        public bool NeoBotKillsCountForYou = true;          // Neo City: o tiro do bot vai com o seu nome: o HUV que ele destroi conta no objetivo da missao
        public bool NeoBotShowShots = true;                 // Neo City: desenhar o tiro do bot (molde: o seu ultimo tiro de arma do carro)
        public float NeoHuvShootRange = 60f;                // Neo City: HUV/HUB atira no bot da party a ate N m (o bot so perde vida pelos HUVs)
        public float NeoHuvRamRange = 6f;                   // Neo City: HUS (o que bate) tira vida do bot encostando (N m)
        public float NeoBotTeleportDistance = 350f;         // Neo City: bot da party a mais de N m de voce aparece atras de voce (0 = desliga)
        public float NeoBotHitRange = 85f;                  // distancia (m) do HUV para o bot atirar (ele atira de longe, sem ir bater)
        public double NeoBotHitSeconds = 1.2;               // um tiro a cada N s por bot (bot mais habilidoso atira mais rapido)
        public int NeoBotDamage = 300;                     // dano do tiro do bot antes de voce acertar um HUV (x nivel da arma/80)
        public double NeoBotDamageFactor = 0.8;             // dano do tiro do bot = seu dano medio nos HUVs x (arma dele / sua arma) x isto
        //                      // dano de cada tiro (x nivel do bot / 80 x habilidade)
        public double NeoBotKillShare = 0.5;                // HUV destruido por um bot: voce ganha esta parte da exp/mito
        public bool AssistSpeedAsMph = true;                   // pecas de velocidade (+N): N vale como mph no Assist Option (sem converter de km/h)
        public double SpeedBonusPerPoint = 0;                  // velocidade maxima extra (aparece no Assist Option): mph por ponto (0 = desligado; o escondido e o Cliente/ferramentas/multiplicador_pontos.py)
        public int SpeedBonusFromPoints = 0;                   //   ... contando so os pontos acima deste numero
        public bool QuietSyncPackets = true;                   // nao escreve no console os pacotes de sincronia (209/555/556/725)
        public bool NeoHuvSpawnAll = true;
        public bool NeoHuvOwnerIsPlayer = true;                // Neo City: voce controla os HUVs (patrulham, perseguem e atiram)                     // Neo City: libera todos os HUVs pedidos (inclusive o chefe)
        public bool NeoTurretOwnerIsPlayer = true;             // torres da Neo City controladas por voce (atiram e levam dano)
        public double NeoHpBase = 0;                       // Neo City: responde o "Enter City" (3720) com o 3721 NeoCityLoad
        // ---- Producao (Production Skill Book) ----
        public long AlchemyBookPrice = 50000;                  // !!livro: preco de cada Skill Book
        public double AlchemyKitPriceMultiplier = 1.0;         // !!kit: preco dos Manufacturing Kits (1200 / 700 / 2000) x isto
        public double AlchemyChanceBonus = 0;                  // + pontos de % na chance de cada receita (45% na tabela)
        public double AlchemyExpMultiplier = 1.0;              // exp de fabricacao x isto
        public bool ComboGetItemAnimation = true;             // combo de 20: animacao "Get Item" com a voz (pacote 741)
        public string ComboPowderItem = "i_n_00096";          // Mittron Crystal Powder (Item Exchange das cidades)
        public double ComboPowderChance = 0.3;                 // chance em cada marco de combo (20, 40...)
        public string[] ComboComponentItems = { "i_n_00007", "i_n_00008", "i_n_00009", "i_n_00029" };   // Components H (acel, vel, dura, booster)
        public double ComboComponentChance = 0.3;
        public string ComboAutoDriveItem = "i_n_00097";        // Auto-Driver
        public double ComboAutoDriveChance = 0.3;
        public int ComboDamageCount = 3;                       // Damage Booster N/S/H (pelo seu nivel) x3
        public double ComboDamageChance = 0.3;
        public int ComboEnhancerNAt = 100;
        public int ComboEnhancerSAt = 200;
        public int ComboEnhancerHAt = 300;
        public int ComboEnhancerUAt = 400;
        public string ComboEnhancerUItem = "i_n_00045_deluxe";
        public int ComboEnhancerUAbove = 400;     // (antigo, nao usado)

        // ---- Recompensas de nivel (a cada 10 niveis) ----
        // Niveis 10-30: grau N; 40-70: grau S; 80+: grau U. Cada pacote: mito + 4 docks (A) + 4 pecas do grau.
        public bool LevelRewardsEnabled = true;
        public long LevelRewardMitoN = 50000;
        public long LevelRewardMitoS = 50000;
        public long LevelRewardMitoU = 100000;
        public long LevelRewardMitoStep = 50000;    // pacote de nivel: N x (nivel / 10) mito (0 = usa LevelRewardMitoN/S/U)
        public int LevelRewardEnhancers = 10;        // pacote de nivel: N Mittron Enhancers do grau do pacote (N, S; U = H)
        // Carro entregue ao chegar no nivel 100: 276 = Shadow Xmas (Lamborghini Reventon). 0 = desligado.
        public int Level100CarSort = 276;

        // ---- Loja de carros ----
        // Libera a compra no servidor de carros com condicao (evento/gacha/etc.). A lista mostrada na loja vem do cliente.
        public bool DealershipUnlockAll = true;
        // Carros das categorias Xmas, BT, ST e MINI so podem ser comprados a partir deste nivel.
        // Carros de cupom (SMALL) tambem ficam liberados.
        public int SpecialCarsMinLevel = 100;

        // ---- Part Shop ----
        // Combine com Seed Part: sorteio do dock (A mais comum ... ultima letra mais rara).
        public bool DockDrawEnabled = true;
        // Grava no dock sorteado os pontos brancos (UpgradePoint) e roxos (Upgrade). Experimental.
        public bool DockStorePoints = true;
        // Convert (Parts + Converting Solution): sorteia 2 opcoes aleatorias na peca.
        public bool ConvertEnabled = true;

        // ---- Trafego da cidade ----
        // Carros de trafego movidos pelo servidor (Moon Palace, Koinonia, Cras, Oros). Quantidade por cidade e alcance (m).
        // Clima (640 Cmd_Weather, 1-4): ciclo automatico. WeatherOrder = sequencia de ceus, WeatherMinutes = duracao
        // de cada um. Descubra qual numero e qual ceu com !!clima 1..4 no chat.
        public bool WeatherEnabled = true;
        public bool WeatherCycle = true;
        public int WeatherDefault = 1;
        public int[] WeatherOrder = { 1, 4, 2 };          // 1 limpo, 4 fim de tarde, 2 nublado (3 = chuva)
        public int[] WeatherMinutes = { 15, 15, 15 };
        public double WeatherRainChance = 0.3;    // chance de chover no meio de cada fase
        public int WeatherRainMinutes = 5;
        public bool CityTrafficEnabled = true;
        public int CityTrafficCars = 450;   // carros por cidade (era 300)
        public float CityTrafficRange = 320f;
        public double CityTrafficHitSeconds = 20;   // carro do trafego batido: fica por conta do jogo N s e volta longe (0 = desliga)

        // ---- HUV (perseguicao) ----
        // Nivel do HUV = nivel do jogador x HuvLevelFactor. Quantidade por chamado e multiplicador de exp/mito.
        public double HuvLevelFactor = 1.0;
        public int HuvCountMin = 1;
        public int HuvCountMax = 4;
        public double HuvRewardMultiplier = 1.0;
        // Entregas por dificuldade (Missions.DeliveryTier): facil/normal/dificil/especial/evento com exp, mito e peca;
        // ouro 100%, prata 2/3, bronze 1/3. Cada cidade seguinte (Koinonia, Cras, Oros) +DeliveryCityBonus.
        public bool DeliveryTierRewards = true;
        public bool DeliveryPartRewards = true;
        public double DeliveryCityBonus = 0.02;
        public double DeliveryUppedGold = 0.5;          // especial/evento com ouro: chance da peca vir upada
        public double DeliveryUppedSilver = 0.2;        // ... com prata
        public Dictionary<string, string> DeliveryTierOverride = new Dictionary<string, string>();   // id da entrega -> facil/normal/dificil/especial/evento
        public double DeliveryRewardMultiplier = 1.0; // entregas (Delivery Service): multiplica exp/mito da medalha
        public bool PartyBotsHelpDeliveries = false;  // entregas: false = os bots da party so te seguem (voce pega os pontos)
        public bool PartyPatrolOffer = false;          // false = no posto de patrulha sai a patrulha normal, nao a "em party" (que nao mostra os pontos)
        public bool PartyBotsHelpMissions = true;     // patrulhas/entregas: bots da party vao aos pontos mais perto e pegam
        public double MissionBotTeleportSeconds = 0;  // bot que nao chega ao ponto em N s pula para la (0 = nunca)
        public bool MissionBotRoads = true;           // missao: o bot vai dirigindo pelas ruas ate o ponto (com os atributos do carro)
        public float MissionBotReach = 8f;
        public float MissionBotDriveRange = 60f;      // missao: ponto mais longe que isso -> o bot aparece la (nao anda reto pelo mapa)            // distancia (m) para o bot pegar o ponto
        public bool TradeBotsRefuse = true;           // troca: bots as vezes recusam (party, amigos e crew sempre aceitam)
        public double PartyPatrolSeconds = 300;       // patrulha em party: tempo limite (s)
        public double RushCountdown = 10;             // Rush Time: segundos de contagem depois de entrar na zona
        public bool RushWaitAtSpot = true;            // Rush Time: os corredores esperam no lugar do Rush (aprendido na 1a vez) ate voce chegar
        public int RushBots = 12;                     // Rush Time: corredores (party + bots da cidade) que entram com voce
        public double RushBotDamage = 0.4;            // (antigo, nao usado: veja RushBotBossHit / RushBotHuvHit)
        // Bots batem no HUV/boss com o dano normal do carro (durabilidade do carro + pecas + nivel x 61-100%), como o jogador.
        public bool BotRealDamage = true;
        // Batida no HUV da cidade: chance de cair item (protetor F/D, Speed/Accel/Dura Enhancer, peca do nivel), 1 a cada N s.
        public double HuvHitDropChance = 0.35;
        public double HuvHitDropSeconds = 3;
        // ... e no HUV e no Rush: chance desse item ser Mittron Enhancer ou Seed Part (N; S/H a partir do nivel da 1a peca S/H)
        public double HuvDropMittronChance = 0.30;
        public double BotHitScale = 1.0;              // multiplicador desse dano
        public double RushBotBossHit = 0.0015;        // Rush: cada golpe de bot tira N da vida maxima do boss (x forca do carro do bot)
        public double RushBotHuvHit = 0.018;           // ... e dos HUVs que o boss solta
        public double RushBotNearHuv = 40;            // Rush: depois que um HUV cai, a party te segue; bot a menos de N m de um HUV vai nele
        public double RushPlayerDamage = 1.0;         // Rush: multiplicador do seu dano
        public double RushPlayerMinDamage = 50;       // Rush: se o jogo mandar dano 0, conta N
        public bool RushHitDrops = true;              // Rush: cada golpe seu pode dar uma peca do seu nivel para baixo
        public double RushHitDropSeconds = 1.5;       // ... no maximo uma a cada N s
        public double RushHitUppedChance = 0.30;
        public double RushRentChance = 0.10;          // Rush: chance da peca do golpe ser uma de aluguel (RENT)
        public bool RushShowBotDamage = true;         // Rush: mostra o dano dos bots como no HUV
        public double RushPowerRadius = 25;           // Rush: poder do boss pega os bots a ate N m de voce quando voce e jogado
        public double RushBotPowerEvery = 25;         // Rush: o boss acerta um bot perto dele sozinho, em media a cada N s (0 = nunca)      // ... com N de chance de vir upada (+1 a +9) e com Convert
        public long RushClockShift = 0;               // Rush Time: ajuste (s) da hora mandada ao cliente
        public float PartyChaseTeleportDistance = 400f;  // HUV: bot a mais de N m do HUV pula para tras dele
        public double PartyChaseCatchUp = 1.8;         // HUV: velocidade x quando esta atras do HUV (para alcancar e bater)
        public double PartyChaseSpeed = 1.25;          // HUV: velocidade e aceleracao x da party na perseguicao (mais rapida, depende menos do pulo)
        public float PartyChaseJoinDistance = 120f;    // HUV/Rush: a party te segue ate voce chegar a N m do HUV/boss; dai vai para cima
        public double RushBotJumpSeconds = 60;         // Rush: o bot so pula para tras do boss depois de N s sem bater (ou longe)
        public float RushBotJumpDistance = 100f;       // Rush: "longe" do boss = mais de N m
        public double RushBotBehind = 5;               // Rush: o bot pula para N m atras do boss
        public double RushBotHitSlowSeconds = 2;       // Rush: depois de bater no boss, o bot fica devagar N s e volta com tudo
        public double RushBotJumpAir = 1.6;            // Rush: poder do boss -> o bot fica N s no ar
        public double RushBotJumpHeight = 4;           // ... subindo ate N m
        public double RushBotJumpRest = 3;             // ... e depois de cair fica parado N s
        public double RushBossRadius = 4.5;            // Rush: "raio" do boss (do centro ate a beirada); o bot nao entra nele
        public double RushBotEdgeGap = 0.5;            // Rush: o bot anda ate N m da beirada do boss para bater
        public float PartyHoldRadius = 8f;             // party parada na formacao: so volta a andar quando voce sai de N m dela
        public float PartyKeepAway = 3f;               // party: nunca chega a menos de N m de voce (nao te empurra parado)
        // lojas do Driver Dome (marcadores do mapa parkinglot_a.nif do jogo): os bots que andam vao ate a porta e entram
        public Dictionary<string, float[]> DomeShops = new Dictionary<string, float[]>
        {
            { "leilao", new[] { 0.2f, -129.2f } }, { "concessionaria", new[] { -12.9f, 117.1f } }, { "battle zone", new[] { 107.6f, 59.6f } },
            { "garagem", new[] { -89.7f, -44.5f } }, { "part shop", new[] { -89.1f, 48.0f } }, { "crew center", new[] { 94.5f, -54.9f } },
        };
        public double DomeShopChance = 0.12;           // a cada ponto do caminho, chance do bot decidir ir a uma loja
        public double DomeShopWaitSeconds = 3;
        public double BotGoalShopStay = 1.5;           // meta cumprida: depois de comprar fica N s na porta e volta         // parado na porta antes de entrar (no leilao: espera e compra)
        // saidas do lobby para a cidade (posicoes por onde voce saiu): os bots saem por elas e voltam depois
        public List<float[]> DomeExits = new List<float[]> { new[] { -316.1f, -0.5f }, new[] { 311.2f, -1.0f }, new[] { -161.7f, 274.5f }, new[] { 162.4f, -187.2f } };
        public double DomeExitChance = 0.03;           // a cada ponto, chance do bot sair do lobby
        public double DomeExitStayMin = 60;            // segundos fora do lobby (foram para a cidade deles)
        public double DomeExitStayMax = 300;
        public float BotLocalChatRange = 150f;          // bot a ate N m de voce fala no chat local (balaozinho); mais longe, no geral
        public double DomeShopStayMin = 5;             // segundos parado na porta da loja depois da compra (nao some)
        public double DomeShopStayMax = 15;
        public double DomeDrivingShare = 0.4;          // Driver Dome: parte dos bots que anda pelo seu trajeto (o resto fica parado)
        public bool BotLicenses = true;
        public bool BotLicensePassive = false;         // false = licenca so sobe quando o bot participa de algo
        public double BotActivityChance = 0.2;         // bot sem meta: chance por minuto de participar de algo (missao, HUV, Rush, corrida) e ganhar
        public double BotGoalActivityChance = 0.5;     // bot com meta: chance por minuto de participar (ele corre atras)
        public double BotActivityScale = 0.4;          // ganho de exp/mito dos bots quando participam (1 = como antes; menor = mais devagar)                // licencas dos bots evoluem: Rookie no comeco, depois o que eles conseguem
        public double BotLicenseSpeed = 1.0;           // ... velocidade do progresso (por minuto)
        public double BotLicenseNeed = 2.0;            // ... meta = N x (grade da proxima / 1000) pontos (Class E 6, Class C 20, No.9 140)
        public double BotLicenseSwapChance = 0.01;
        public double BotLicenseCatchUp = 1.0;         // ... bot muito abaixo do que o nivel permite aprende N vezes mais rapido (1 = igual)
        public double BotSpoilerChance = 0.7;          // chance do bot ter aerofolio
        public int BotNeonLevel = 10;                  // bots acima desse nivel tem neon (abaixo, nao)     // ... nivel 31+: chance por minuto de trocar a terceira licenca
        public bool CrewBotsTell = true;               // bots da sua crew contam no chat da crew o que estao fazendo
        public double PartyDomeErrandMinutes = 3;
        public double PartyDomeErrandIdle = 15;        // ... so quando voce esta parado no lobby ha N s      // Driver Dome: a cada ~N min um bot da party vai a uma loja e volta (0 = nunca)
        public double DomeDriveSpeedMin = 15;          // Driver Dome: velocidade (m/s) dos bots andando
        public double DomeDriveSpeedMax = 35.7;        // Driver Dome: limite 80 mph
        public double UnderCityStageFallbackSeconds = 12; // Undercity: sem a etapa 1 do cliente em N s, o servidor manda (0 = nao)
        // Interacoes dos bots no chat (BotChatter)
        public double BotRaceStartSeconds = 5;        // desafio de corrida de bot: segundos depois que voce entra ate a largada
        public bool BotRaceAutoStart = true;          // desafio: a corrida comeca sozinha (false = voce aperta Iniciar)
        public int BotRaceBonusExp = 100;             // desafio aceito: exp extra
        public int BotRaceBonusMito = 1000;           // desafio aceito: mito extra
        public double BotRaceOpenSeconds = 60;        // desafio: a sala do bot espera N s voce entrar; depois os bots correm sozinhos
        public double PartyBotRaceLeaderShare = 0.6667;  // bot da party que correu na Battle Zone sem voce: o lider leva 2/3 do premio dele
        public double BotRaceMinSeconds = 120;        // ... corrida sem voce: dura entre N ...
        public double BotRaceMaxSeconds = 300;        // ... e N s (eles somem da porta da Battle Zone e voltam no fim)
        public double AuctionHoldSeconds = 180;       // leilao: o bot espera voce por N s quando voce se interessa
        public double ChatResetMinutes = 0;           // a cada N minutos o chat e "reiniciado" (linhas vazias + dica do !!help); 0 = nunca
        public int ChatResetLines = 60;               // quantas linhas vazias empurram as falas antigas para fora
        public double HuvPartyBonusEach = 0.05;       // HUV: +5% da recompensa por membro da party
        public double HuvPartyBonusMax = 0.20;        // HUV: no maximo +20% (4 membros)
        public double BotHelpByBotChance = 0.5;       // outro bot topa ajudar o bot com meta (andam juntos, meta anda 70% mais rapido)
        public double BotTradeRewardShare = 0.25;     // voce deu na troca o que o bot queria: 25% do valor da meta ...
        public long BotTradeRewardMax = 500000;       // ... no maximo N mito
        public double BotHelpAskChance = 0.6;         // bot com meta chama alguem para a party ajudar
        public double BotHelpRewardShare = 0.15;      // ajudou: recompensa = 15% do valor da meta (mito) ...
        public long BotHelpRewardMax = 300000;        // ... no maximo N mito
        public bool PartyTalk = true;                 // party conversa com voce: discussao (Joaopvbr/Marsteh/Brunizin.X), sugestoes e elogios
        public double PartyArgueSeconds = 180;        // parado N s com 2+ dos tres na party: eles discutem
        public double PartySuggestSeconds = 300;      // intervalo minimo entre sugestoes da party
        public bool BotChatter = true;                // bots conversam no chat (corrida, leilao, HUV, Rush, Undercity, trocas entre eles)
        public bool BotOffersWhenBusy = false;        // false = bots nao chamam para party/corrida/leilao enquanto voce esta em missao, entrega, HUV, Rush...
        public double BotOfferGapMin = 60;   // 1 minuto entre chamadas            // intervalo minimo (s) entre ofertas no chat (party, corrida, leilao, crew, ajuda)
        public double BotOfferGapMax = 75;           // ... e maximo
        public double BotOfferSeconds = 60;   // 1 minuto para responder; depois o proximo bot chama
        // bot vem ate voce na rua e pede para trocar, correr ou ajudar (chat local; o da sua crew tambem no chat da crew)
        public bool BotApproach = true;
        public double BotApproachGapMin = 180;
        public double BotApproachGapMax = 420;
        public double BotApproachPauseSeconds = 120;   // enquanto ele esta com voce, as outras chamadas param          // quanto tempo cada oferta fica valendo para responder (sim/bora)
        public double BotChatterSeconds = 90;         // intervalo medio (s) das conversas
        // carros que o cliente 2016 nao lista na loja (e desenha errado / pode fechar o jogo)
        public int[] UnsupportedCars = { 43, 49, 64, 67, 68, 74, 85, 352 };     // "Support" 0 no VehicleDisplay (o jogo desenha um tanque) + Monster CT (fechou o jogo)
        public bool OptionParts = true;               // Option Parts (op_*) nos quadrados das pecas do carro
        public bool DockingParts = true;              // Docking Parts (dock_*) nas pecas do carro
        public bool PartyFollowReplay = true;
        public bool PartyChaseReplay = false;         // HUV: true = repete o caminho do HUV; false = dirige atras dele (curvas pelo trajeto do HUV)         // party: repete os seus movimentos com atraso (false = dirige sozinho)
        public double PartyFollowDelay = 0.6;         // party (replay): atraso em segundos por posicao na fila

        // ---- Battle Zone ----
        // Corredores IA (botao de IA no assento da sala). Recompensa por corrida (com pelo menos 2 corredores):
        // exp e mito = valor x pontos de colocacao (1o lugar = numero de corredores, ultimo = 1).
        public bool BattleAiEnabled = true;
        public double BattleBotWaitSeconds = 600;     // bot que entrou numa sala da Battle Zone vai ate a porta dela e espera la ate N s
        public double BattleBotRaceSeconds = 420;     // na largada ele some (esta correndo) por ate N s; no resultado volta
        // Dificuldade das IAs: nivel alvo = nivel do jogador x BattleAiDifficulty (0.1 = facil ... 1.0 = igual ao jogador),
        // no maximo BattleAiMaxLevel (1-99), variando +-BattleAiLevelSpread.
        public double BattleAiDifficulty = 0.6;
        public int BattleAiMaxLevel = 65;
        public int BattleAiLevelSpread = 6;
        // Carros que a IA nao usa (54 = Nevera/Enzo Ferrari: o bot ficava parado).
        public int[] BattleAiExcludedCars = { 54 };
        public int[] PoliceCars = { 85, 88 };
        public double BattleFinishSeconds = 10;             // Battle Zone: depois do primeiro na chegada, a corrida acaba em N s (ou quando todos chegam)
        public bool BattleCompleteBonus = true;             // paga o Complete Bonus Reward (estrelas) para quem termina a corrida
        public bool BattleBotsAsPlayers = true;             // na sala de espera os bots aparecem como jogador (nome, nivel, carro); na corrida viram IA
        public bool BattleBotsJoinRoom = true;              // bots entram sozinhos na sala que voce cria (voce tira clicando no assento)
        public double BattleBotJoinSeconds = 12;            // entra mais gente a cada 2 ate N segundos
        public int BattleBotJoinMax = 3;                    // de 1 ate N bots de uma vez               // Leo Patrol TW/USA: so o admin tem; nenhum bot (cidade, corrida, troca, leilao)
        // Sala da Battle Zone: depois da corrida manda a sala inteira de novo ao voltar (a contagem fica certa: 8/8, nao 16/8).
        public bool BattleRoomResync = true;
        // Paciencia: bot que entrou sozinho sai da sala se voce demorar para largar (entre 0,6x e 1,6x N segundos; 0 = nunca).
        public double BattleBotPatienceSeconds = 150;
        // No fim da corrida, chance de cada bot (que nao e da sua party) sair da sala quando voce volta para ela.
        public double BattleBotLeaveAfterRaceChance = 0.2;
        // Espectadores: com a sala cheia, bots ficam assistindo (ate BattleSpectatorsMax, chance BattleSpectatorChance a cada
        // tentativa), narram a corrida no chat da sala (no maximo uma fala a cada ~BattleNarratorSeconds) e entram quando abre vaga.
        public bool BattleSpectators = true;
        public int BattleSpectatorsMax = 3;
        public double BattleSpectatorChance = 0.5;
        public double BattleNarratorSeconds = 8;
        // Posicao das IAs no grid (flag do cliente BattleZone.FrontAi): 0 = ao lado do jogador e visiveis na apresentacao.
        public int BattleFrontAi = 0;
        // Evento especial: "natal" (neve, arvore de Natal, roupa de Papai Noel), "valentine", "halloween",
        // "auto" (padrao: pelo mes do PC, cada evento dura 1 mes: outubro Halloween, dezembro Natal, fevereiro Valentine)
        // ou "nenhum" (como o jogo veio). Vale ao entrar no jogo (relogue para trocar).
        public string Evento = "auto";
        // Eventos do jogo ligados/desligados (nome -> 1/0), ex.: {"Christmas": 1, "SnowEffectOn": 1, "ChristmasTree": 1}.
        // Nomes em Static.EventFlags. Vazio = como o jogo veio.
        public Dictionary<string, int> Events = new Dictionary<string, int>();
        // Id do servidor no LoadCharAck. Com 0 o cliente nao calcula a Vehicle Performance Info
        // (mph, tempo, crash, boost do inventario: cliente 0x466876 testa o id guardado em 0x53c805).
        public uint LoadCharServerId = 2016;
        // Ao sair de uma sala de time, remonta o carro para tirar a cor do time (pacote 1061, experimental).
        public bool BattleTeamColorReset = true;
        // Corrida de time: pintura das IAs (formato do jogo 0x83BBGGRR). Time vermelho = assentos pares, azul = impares.
        public uint BattleTeamRedColor = 0x830000FFu;
        public uint BattleTeamBlueColor = 0x83FF0000u;
        // Crew: nivel minimo e custo para criar.
        public int CrewCreateLevel = 20;    // (com o mod Escada de crews: nivel 1)
        public long CrewCreateFee = 10000;

        // Bots (jogadores simulados): perfis em bots.json, crews de bots em crews.json (criados na 1a vez).
        public bool BotsEnabled = true;
        public int BotsCount = 60;
        // Bots so evoluem com voce: na sua party (rapido) ou na sua crew (devagar, so nivel). Os outros ficam parados.
        public bool BotsFrozen = true;
        // Moradores das cidades: CityBotsPerCity bots por cidade, espalhados pelas ruas da cidade inteira (nao em volta de
        // voce) e nao somem quando voce se afasta. Faixa de nivel de cada cidade (min, max): Moon Palace, Koinonia, Cras, Oros.
        public bool CityWholeCity = true;
        public bool CityAllDriving = true;
        // Escada de crews (desafio mano a mano): perto de um membro da crew da vez por DuelNearSeconds (a DuelNearMeters m)
        // ele chama para correr. Ganhou: ele sai da crew dele e entra na sua. Sem membros: o chefe te chama e fica com a sala
        // na Battle Zone ate perder (premio: carro dele, BossMitoBase x chefes vencidos, +1 andar na garagem).
        // Membros na sua crew doam mito ou peca a cada CrewGiftMinutes-Max min (mais nivel, mais premio).
        public bool CrewLadder = false;              // mod Escada de crews (ou ligue em Menu.bat > Mods)
        public bool CrewLeaderShare = true;
        public int CrewSlotsStart = 3;                // vagas da crew para bots chamados (recrutar/amizade); -1 = sem limite
        public int CrewSlotsPerBoss = 3;              // +vagas por chefe da escada vencido
        public double CrewTeamCallMinutes = 8;        // a cada ~N min um bot da sua crew chama para a dupla contra a crew da vez (0 = nunca)
        public float MeetDistance = 3f;               // bot que vem ate voce (troca/presente) para a N m           // lider da crew ganha metade do mito que os membros (bots e seus slots) ganham
        public double DuelNearSeconds = 10;
        public double DuelAutoLeaveSeconds = 30;      // mano a mano: s depois do resultado voce sai sozinho da sala
        public float DuelNearMeters = 25f;
        public long BossMitoBase = 50000;
        public double CrewGiftMinutes = 12;
        public double CrewGiftMaxMinutes = 18;
        // lado de cada crew na cidade: {x0, x1, y0, y1} em fracao do mapa (0 = menor x/y, 1 = maior). Se o lado sair
        // trocado no jogo, troque os numeros aqui.
        public Dictionary<string, float[]> CrewRegions = new Dictionary<string, float[]>
        {
            { "Rookies", new[] { 0f, 0.5f, 0f, 1f } }, { "MoonFlow", new[] { 0.5f, 1f, 0f, 1f } },
            { "MitoKings", new[] { 0f, 1f, 0f, 0.5f } }, { "KoiGhost", new[] { 0f, 1f, 0.5f, 1f } },
            { "CrasRacers", new[] { 0.5f, 1f, 0f, 1f } }, { "CrasElite", new[] { 0f, 0.5f, 0f, 1f } },
            { "OrosLegends", new[] { 0.5f, 1f, 0.5f, 1f } }, { "LegendsDC", new[] { 0f, 1f, 0f, 1f } }
        };            // moradores todos andando (false = parte estaciona, CityDrivingShare)
        public int CityBotsPerCity = 30;
        public int[] CityLevelBands = { 1, 16, 15, 30, 30, 50, 50, 99 };
        // grupos de bots da mesma cidade (os de crew so com a propria crew): a cada CityGroupGapMin-Max s um grupo de 2 a
        // CityGroupMax sai junto: HUV (sirene, mais rapido), passeio ou Battle Zone (vai ao posto, para CityPostWaitSeconds,
        // entra, fica CityBattleMinMinutes-Max min e volta pelo mesmo posto ate o lugar de origem).
        public double CityGroupGapMin = 60;
        public double CityGroupGapMax = 150;
        public int CityGroupMax = 4;
        public double CityGroupHuvChance = 0.4;
        public double CityGroupBattleChance = 0.3;
        public double CityPostWaitSeconds = 3;
        public double CityBattleMinMinutes = 2;
        public double CityBattleMaxMinutes = 5;
        public double CityHuvSpeedFactor = 1.6;
        // postos da Battle Zone por cidade {x, y, z, direcao} (alem dos aprendidos em battle_posts.json quando voce entra
        // na Battle Zone pela rua) e ponto fixo de cada crew na cidade (nome da crew -> {x, y}; sem = sorteado)
        public Dictionary<int, List<float[]>> CityBattlePosts = new Dictionary<int, List<float[]>>();
        public Dictionary<string, float[]> CrewSpots = new Dictionary<string, float[]>();
        public bool BattleAiOnlyBots = true;          // Battle Zone: IA so com os nossos bots (carro e visual deles)
        public double BotCrewExpScale = 0.1;           // exp dos bots da sua crew (0.1 = 10% do normal)
        // Bots da party: a cada PartyBotGrowMinutes (ou quando voce ganha exp, no maximo 1x a cada PartyBotGrowMinGapSeconds)
        // sobem PartyBotGrowLevels nivel(is) (ate o seu nivel + PartyBotLevelAbove), chance de peca melhor, carro do V do
        // nivel e licencas (x PartyBotLicenseBoost).
        public double PartyBotGrowMinutes = 5;
        public int PartyBotGrowMinGapSeconds = 60;
        public int PartyBotGrowLevels = 1;
        public int PartyBotLevelAbove = 3;
        public double PartyBotPartChance = 0.5;
        public double PartyBotLicenseBoost = 3;
        // Mostra os bots como "online" (na cidade) na lista de membros da crew.
        public bool BotsAppearOnline = true;
        // Chance de cada bot ficar offline a cada entrada (0 = todos online; 0.34 = cerca de 1/3 offline, como antes).
        public double BotsOfflineChance = 0;
        // Bots respondem no chat da crew quando voce escreve no canal "team".
        public bool BotsCrewChat = true;
        // Bonus por bot na party nas recompensas de HUV (0.1 = +10% por bot).
        public double PartyBotBonus = 0.1;
        // Bots na rua: os da party seguem voce (atraso em segundos entre cada um); Marsteh e Brunizin.X estacionados.
        public bool CityBotsEnabled = true;
        public bool CityBotsSellers = true;
        public int CitySellersCity = 4;                // vendedores (Brunizin.X, Marsteh) so nesta cidade (4 = Oros; 0 = todas)
        public double CitySellersChance = 0.35;        // ... e cada um aparece com essa chance quando voce entra
        public bool CityParkBots = false;              // false = sem bots estacionados na cidade (so andando)
        public double CityBotFollowDelay = 1.2;
        public float CityBotFollowGap = 5f;       // party: para a N metros de voce (x posicao na fila)
        public float CityBotSideGap = 2.5f;       // party: desvia N metros para o lado ao parar
        public int CityBotHeadingMode = 0;        // 0-3: ajuste da rotacao dos carros dos bots (se ficarem virados errado)
        public bool CityDrivingBots = true;       // bots andando pelas ruas
        public double CityDrivingShare = 0.7;     // parte dos bots da cidade que anda (o resto estaciona)
        public double CityDriveSpeedMin = 12;     // metros por segundo (12 = 43 km/h)
        public double CityDriveSpeedMax = 20;
        public int DomeParkedBots = 50;          // bots no lobby (Driver Dome), no maximo (contando os vendedores)
        public float CityBotMinDistance = 3f;     // party: nunca fica a menos de N m de voce (se encostar, se afasta)
        public float CityOvertakeSide = 3.5f;     // bots andando: desvio lateral para ultrapassar
        public bool PartyChaseHuv = true;         // numa perseguicao de HUV, a party vai atras do HUV
        public double PartyChaseDelay = 0.8;      // carregando: segue o ponto do caminho do HUV de N s atras
        public double PartyChaseCharge = 6;       // segundos seguindo o HUV (carregando o booster)
        public double PartyChaseAttack = 2.5;     // segundos atacando (vai com tudo em cima do HUV)
        public double PartyChaseBoost = 1.6;      // velocidade x no ataque
        public int DomeLearnLimit = 4000;   // pontos do Driver Dome aprendidos por onde voce anda (mais pontos = bots andam por mais lugares)
        public bool UppedAllQuests = true;           // perfil upado com todas as missoes principais feitas
        public bool UppedAllLicenses = true;         // perfil upado com todas as licencas
        public bool LicenseRaceCountsBoth = true;    // licencas: toda corrida (tambem contra bots) conta como individual E de time
        public int LicenseRewardHour = 10;            // recompensa das licencas todo dia a partir desta hora (relogio do PC; -1 = desliga)
        public double VShopPermanentMultiplier = 2.0; // drift shop: item sem preco "para sempre" = maior periodo x N
        public int VShopCcToMito = 1000;              // drift shop: sem CC suficiente, cada CC vale N mito
        public bool PartyBotDrift = true;
        public double PartyChaseTeleportSeconds = 10; // HUV: bot que nao encosta no HUV em N s pula para tras dele (0 = nunca)
        public double CityBotFarSnapSeconds = 8;      // seguindo: longe do lugar dele por N s, pula para o lugar            // party: derrapa e faz fumaca onde voce derrapou
        public float PartyHuvContact = 4f;           // distancia (centro a centro, m) em que o bot "encosta" no HUV e conta o golpe
        public bool CityHuvHits = true;              // golpes da party no HUV da cidade
        public double CityHuvHitSeconds = 1.2;       // intervalo minimo entre golpes de um bot
        public double CityHuvHitDamage = 0.05;       // dano por golpe (fracao da durabilidade do HUV)
        // Tutorial do proprio jogo (Apprentice): corredor novo nasce com o Birdie RTI de treino (tipo 95), faz a corrida
        // de treino do Battle Zone e no fim escolhe o carro inicial (o de treino sai da garagem).
        public bool ApprenticeTutorial = true;
        public int ApprenticeCarType = 95;
        public double CityBotSnapSeconds = 4;         // voce parado: se o bot nao se arrumar em N s (ou ficar colado), pula para o lugar dele (0 = nunca)
        public double PartyChaseKeepSeconds = 4;      // HUV continua "ativo" ate N s sem movimento novo (evita a party ficar indo e voltando)
        // Undercity (Tatiana ou /ucm /uck /ucc /uco). Vai sozinho com sua party de bots.
        public bool UnderCityEnabled = true;
        public bool UnderCityFee = true;                     // cobra a taxa de entrada
        public int[] UnderCityFees = { 600, 1200, 2100, 4200 };   // Moon Palace, Koinonia, Cras, Oros
        public double UnderCityRewardMultiplier = 1.0;
        public int UnderCityStartPhase = 1;                  // teste: 2 pula a Missao 1, 3 tambem a 2, 4 vai direto ao BossHUV
        public int UnderCityBarrierPlayers = 2;              // Missao 2: HUBs barreira como numa party de N (2 = 5/4/3)
        public int UnderCityMission2ExtraSeconds = 0;        // tempo extra na Missao 2
        public double UnderCityBotHubSeconds = 9;            // Missao 1: cada bot da party destroi 1 HUB a cada ~N s (0 = nao)
        public int UnderCityHubsLeftForYou = 10;             // os bots sempre deixam pelo menos N HUBs para voce
        public double UnderCityBotHitSeconds = 3;            // Missoes 3/4: cada bot perto de um HUV bate a cada ~N s (0 = nao)
        public float UnderCityBotHitRange = 4f;             // metros do HUV para o golpe contar
        public bool UnderCityBotHitAnywhere = false;         // true = os bots batem mesmo sem estar perto (se nao acharem o HUV)
        public double UnderCityBotDamage = 0.08;             // dano de cada golpe de bot (fracao da vida do HUV)
        public double UnderCityBotBossDamage = 0.006;        // no BossHUV
        public double UnderCityBotKillShare = 0.5;           // voce ganha esta parte da exp/mito dos HUVs que os bots pegam
        public int UnderCityBetaClass = 4;                   // classe (4 = A) a partir da qual a peca do set e Beta
        public string UnderCityEnhancer = "i_n_00003";       // Mittron Enhancer
        public int[] UnderCityEnhancers = { 1, 1, 2, 3, 3 }; // quantos por classe D, C, B, A, S          // Driver Dome: para de aprender pontos novos depois de N (fica fixo)
        public bool BotVisuals = true;            // pecas visuais nos bots (menos Marsteh, Brunizin.X e seus corredores)
        public int PlayerCrewLevel = 1;           // cor da placa das crews criadas por voce: 0 verde, 1 azul, 2 rosa, 3 laranja            // bots estacionados no Driver Dome (onde voce ja passou)
        public int CityParkedBots = 12;
        public int MarstehColor = 24;             // pintura (paleta do jogo 0-28): 24 Carmine (vermelho)
        public int BrunizinColor = 22;            // 22 Black pearl (preto)
        public int BotColorByte = 3;              // onde fica a cor no carro (3 ou 0) — mude se as cores nao aparecerem           // outros bots estacionados pela cidade
        public float CityParkedRadius = 600f;     // ate quantos metros de onde voce entrou
        public float CityBotViewRange = 450f;     // distancia em que os estacionados aparecem
        public bool CityBotsDebugMove = true;     // registra um movimento do jogador no log (ajuda a calibrar)
        // Combo da party: chance de cada bot da party somar +1 a cada combo seu; mito a cada N combos da party.
        public double PartyBotComboChance = 0.5;
        public int PartyComboRewardEvery = 20;
        public long PartyComboRewardMito = 0;
        public bool PartyComboRewards = false;      // false = o combo da party nao da recompensa (so conta)
        // Outros corredores da sua conta na party ganham exp/mito junto (1.0 = 100% do que voce ganhou).
        public double AltRewardShare = 1.0;
        public double PartyBotExpShare = 0.5;          // bots da party (nao os seus corredores) ganham essa parte da sua exp/mito
        public bool PartyBotExpTable = true;           // ... e mostram no chat da party: exp p/ o proximo nivel e progresso das licencas
        public double BotLicenseActivity = 2.0;        // ... pontos de licenca por atividade feita com voce
        public double BotExpPerLevel = 40;             // exp do bot para subir do nivel N = max(100, N x isso)
        // Personagens (rostos) usados pelos bots, em rodizio. Ajuste se algum numero nao existir no seu cliente.
        public int[] BotAvatarIds = { 1, 2, 3, 4 };

        // Leilao vivo: bots anunciam itens e compram os seus.
        public bool AuctionEnabled = true;
        public int AuctionBotStock = 80;          // quantos anuncios de bots ficam a venda
        public int AuctionCarStock = 15;          // carros de bots a venda (alem dos bots fixos Marsteh e Brunizin.X)
        // Preco dos carros conforme o estado: km rodados (ate AuctionCarMaxKm tira ate AuctionCarKmDiscount)
        // e combustivel (tanque vazio tira ate AuctionCarFuelDiscount). 0.12 = 12%.
        public int AuctionCarMaxKm = 250000;
        public double AuctionCarKmDiscount = 0.12;
        public double AuctionCarFuelDiscount = 0.08;
        public int AuctionFeePercent = 3;         // taxa de registro (perdida), como no jogo original
        public int AuctionDays = 7;               // anuncio expira depois de N dias
        // Chance (por minuto) de um bot comprar o SEU anuncio. Comparado a loja e ao anuncio mais barato do leilao:
        // abaixo da loja = Bargain (~3 min), abaixo do leilao = Cheap (~5 min), ate 1.5x = Fair (~17 min),
        // ate 2.5x = Pricey (~1 h), ate 5x = Expensive (~8 h), acima disso VeryExpensive (cai quanto mais caro).
        public double AuctionBuyChanceBargain = 0.35;
        public double AuctionBuyChanceCheap = 0.20;
        public double AuctionBuyChanceFair = 0.06;
        public double AuctionBuyChancePricey = 0.015;
        public double AuctionBuyChanceExpensive = 0.002;
        public double AuctionBuyChanceVeryExpensive = 0.0004;
        // Item que vale mais que isso tem menos compradores: a chance cai (10x o valor = metade da chance).
        public double AuctionRareItemValue = 100000;
        // Chance (por minuto) de um anuncio de bot sumir (outro bot comprou) e ser trocado por outro.
        public double AuctionBotTurnover = 0.01;
        public int BattleExpPerPlace = 120;
        public int BattleMitoPerPlace = 400;
        // Cartas "?" no fim da corrida: cada carta aberta da um item aleatorio (ate o seu nivel).
        public bool BattleRewardCards = true;
        // Modify (peca + Mittron Enhancer): sobe o nivel +1 com +1..3 pontos. Taxa de mito = ModifyFeePerLevel x (nivel+1).
        public bool ModifyEnabled = true;
        // Responde Modify/Convert com um pacote de confirmacao (eco do pedido) para o Part Shop nao ficar bloqueado. Palpite.
        public bool PartShopSendAcks = true;
        public bool PartQualityRoll = true;            // pecas de desempenho (loja, Rush, entregas, RENT) sorteiam a qualidade
        public int[] PartQualityRange = { 4, 10, 16, 28 };   // ... de 0 ate N pontos a mais sobre o padrao: N, S, H, U
        public int ModifyFeePerLevel = 100;
        // Part Shop, venda: +SellBonus mito por item; +SellQualityMito por ponto de qualidade (peca/dock/option); +SellUpgradeMito por nivel de up.
        public int SellBonus = 0;
        // preco final de venda por item (sem o SellBonus)
        public Dictionary<string, int> SellPriceOverride = new Dictionary<string, int>
        {
            {"i_n_00043", 350}, {"i_n_00044", 500}, {"i_n_00045", 800}, {"i_n_Seed_N", 200}, {"i_n_Seed_S", 500}, {"i_n_Seed_H", 750}
        };
        public double AuctionConvertedChance = 0.3;     // leilao: chance de option/dock anunciado ja vir com Convert
        // leilao, precos: itens = preco da loja x AuctionItemPriceMultiplier; +AuctionLevelBonus por nivel/100 da peca
        // (nivel 100 = 2x com 1.0); +AuctionUpgradeBonus por +N; +AuctionQualityBonus por ponto de qualidade;
        // Convert x AuctionConvertedMultiplier; carros = preco da loja x AuctionCarPriceMultiplier
        public double AuctionItemPriceMultiplier = 1.5;
        public double AuctionLevelBonus = 1.0;
        public double AuctionUpgradeBonus = 0.4;
        public double AuctionQualityBonus = 0.05;
        public double AuctionConvertedMultiplier = 2.0;
        public double AuctionCarPriceMultiplier = 1.0;
        public bool AuctionCarShopPrice = true;       // carros dos bots no leilao = preco da loja (so o desconto de km/combustivel)
        // leilao: chance de cada anuncio novo de bot ser um cupom de carro (Coupon Book); preco = preco do carro x fator
        public double AuctionCouponChance = 0.04;
        public double AuctionCouponPriceFactor = 3.0;
        public int OptionQualityRange = 6;              // Option Part: 0 ate N pontos a mais ao ganhar/comprar
        public int SellQualityMito = 50;
        public int SellUpgradeMito = 100;
        // Pontos do Dock (somam direto no tipo dele: Speed, Accel, Crash ou Boost), sorteados pela letra.
        // Por grau N, S, H, U: letra A sorteia de DockPointMin ate DockPointMin + DockPointSpread; a ultima letra de
        // DockPointMax ate DockPointMax + DockPointSpread; as do meio ficam no caminho. Mais facil sair perto do minimo.
        public bool DockPointRoll = true;
        // Livro de cupons: guarda os contadores do cliente (coupons.json), marca/abre/entrega o cupom e a loja vende o
        // carro de cupom gastando o item do cupom.
        public bool CouponsEnabled = true;
        // Docks do pacote de nivel: os 4 (Speed, Accel, Crash, Booster) na mesma fileira do carro dao +DockRowBonus
        // em cada atributo e a opcao sorteada no pacote (Turbo Start ou Drift Booster).
        public bool DockRowEnabled = true;
        public int DockRowBonus = 50;
        public int[] DockPointMin = { 1, 3, 6, 10 };
        public int[] DockPointMax = { 6, 14, 24, 36 };
        public int[] DockPointSpread = { 2, 4, 6, 8 };
        // Cada letra soma N pontos sobre a anterior (A = DockPointMin, B = +N, C = +2N...). 0 = usa DockPointMax.
        public int DockPointStep = 6;

        // Recompensa de missao que nao existe na tabela (nao deve acontecer): evita travar o servidor.
        // Missoes secundarias (rq_*): o jogo mostra o DOBRO da tabela (ex.: 280 exp / 1600 mito contra 140 / 800).
        public int SecondaryQuestRewardMultiplier = 2;

        public int UnknownQuestExp = 100;        // exp de uma quest que nao esta na tabela do servidor
        public int UnknownQuestMito = 300;       // mito de uma quest que nao esta na tabela do servidor
        // Se true, qualquer usuario/senha digitado no login entra na conta offline.
        public bool AutoLogin = true;
        // Duas contas no AutoLogin: qualquer nome entra na conta NORMAL (NormalAccount, sem o perfil upado). Para entrar
        // na conta ADMIN (a que tem o perfil upado), digite AdminAccount como usuario no login (no Start.bat)
        // ou deixe AdminLogin = true.
        public string NormalAccount = "jogador";
        public string AdminAccount = "admin";
        public bool AdminLogin = false;
        // Login com outro nome: cria a conta na hora (vazia) e guarda os corredores dela. Os outros corredores da conta
        // viram bots (amigos, party). Nome vazio = NormalAccount.
        public bool AutoCreateAccounts = true;
        // Reenvia a lista de corredores ao cliente logo apos criar um corredor.
        public bool RefreshListAfterCreate = true;

        // Estado inicial de um perfil novo.
        public int StartLevel = 1;
        public long StartMito = 1000;
        public int StartCarID = 1; // 1 = Kicker (V1)

        // Nivel minimo do personagem para COMPRAR carros de cada tier (V1..V9).
        public int[] CarTierLevels = { 0, 10, 20, 30, 40, 50, 60, 75, 80 };

        // Economia de energia: menos carros do trafego e bots que o jogo precisa desenhar e simular (o DriftCity.exe e
        // quem mais gasta). Desligada por padrao; ligue com "EconomiaEnergia": true no server.json.
        public bool EconomiaEnergia = false;
        public void ApplyPowerSaving()
        {
            if (!EconomiaEnergia) return;
            CityTrafficCars = Math.Min(CityTrafficCars, 220);
            CityTrafficRange = Math.Min(CityTrafficRange, 240f);
            CityBotViewRange = Math.Min(CityBotViewRange, 300f);
            DomeParkedBots = Math.Min(DomeParkedBots, 15);
            CityParkedBots = Math.Min(CityParkedBots, 8);
            Log.WriteLine("Economia de energia ligada: {0} carros no trafego (ate {1} m), bots ate {2} m, {3} no Driver Dome, {4} estacionados.",
                          CityTrafficCars, CityTrafficRange, CityBotViewRange, DomeParkedBots, CityParkedBots);
        }

        public static Config Load(string path = "config.json")
        {
            Config config;

            if (File.Exists(configPath))
            {
                Log.WriteLine("Config file exists, loading.");

                string json = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<Config>(json);
            }
            else
            {
                Log.WriteLine("Config file could not be found, using defaults.");
                config = new Config();
            }
            // mods: arquivo na pasta do servidor liga o mod (e pode trazer ajustes dele)
            const string ladderMod = "mod_escada_de_crews.json";
            if (File.Exists(ladderMod))
            {
                try { JsonConvert.PopulateObject(File.ReadAllText(ladderMod), config); } catch (Exception ex) { Log.WriteError(ladderMod + ": " + ex.Message); }
                config.CrewLadder = true;
                Log.WriteLine("Mod ligado: Escada de crews ({0}).", ladderMod);
            }
            // LendasDC virou LegendsDC (mod antigo ou server.json antigo)
            if (config.CrewRegions != null && config.CrewRegions.ContainsKey("LendasDC"))
            {
                config.CrewRegions["LegendsDC"] = config.CrewRegions["LendasDC"];
                config.CrewRegions.Remove("LendasDC");
            }
            config.ApplyPowerSaving();
            return config;
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(configPath, json);
        }
    }
}
