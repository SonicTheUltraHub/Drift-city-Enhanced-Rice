# Enhanced Rice (Offline Aprimorado)

Modo offline melhorado para o **cliente US 2016 do Drift City**, feito em cima do emulador Rice (C#, .NET 4.8). A cidade
fica viva com jogadores simulados (bots) que correm, trocam, entram na sua party e crew, conversam e jogam os modos com voce.
English guide: `README.md`.

## Guia rapido
**Precisa:** o seu Drift City (Alpha / US) com `DriftCity.exe`, `Data` e `Data_US`; Windows com .NET 4.8; Visual Studio.

**Compilar e instalar**
1. Abra o `Rice.sln` e compile. O projeto **Instalador** e o principal e compila o Rice junto.
2. No fim, a pasta `Pacote` fica so com o `Install Enhanced Rice.exe` (o servidor vai dentro dele).
3. Coloque o `Install Enhanced Rice.exe` do lado do `DriftCity.exe` (ou digite a pasta do jogo) e abra.
4. Escolha o idioma e `1) Instalar / atualizar`. Ele confere cada arquivo (md5), aplica **so as modificacoes** em cima dos
   seus arquivos, troca o `DriftCity.exe` pelo que abre sem o launcher, guarda backup em `backup_instalador\<data>` e
   coloca o servidor (`Rice`), `Start.bat`, `Menu.bat` e os leia-me. Tambem arruma os carros e a loja para o **seu**
   cliente (qualquer um, como o do openDC): V1 a V9 e os especiais depois do V9, com os nossos V, pontos e precos; carro
   sem modelo sai da loja. Ele pergunta antes: da para deixar a loja do cliente como esta (os bots usam os carros
   dela). `3) Arrumar os carros` faz so isso. `2) Desfazer` volta o backup.

**Jogar:** `Start.bat`. No login, `admin` entra na conta admin (RiceAdmin: nivel alto, bilionario, um carro de policia
todo upado com Turbo Start e Drift Booster); qualquer outro nome cria uma conta nova (sem ser admin). No chat: `!!help`.

**Menu (`Menu.bat`, com o servidor fechado):** 1 Saves, 2 Bots, 3 Crews, 4 Configuracoes do servidor, 5 Backup,
6 Pasta dos saves, 7 Evento especial, 8 Mods (Escada de crews), 9 Idioma, G Graficos do jogo.

**Eventos especiais:** no Automatico (padrao) cada evento dura o mes: outubro Halloween, dezembro Natal (neve, arvore,
roupa de Papai Noel, musica de Natal em loop no lobby), fevereiro Valentine. Os bots falam do evento e respondem quando
voce fala dele no chat. Pascoa nao tem visual no jogo.

**Idioma:** ingles por padrao (servidor, chat do jogo, menu e instalador). Para portugues: `Menu.bat > 9) Idioma`, a
opcao Portugues no instalador ou `"Language": "pt"` no `Rice\server.json`.

**Creditos:** emulador Rice de **amPerl** (com permissao); formatos de pacote da documentacao do openDC (Xerender13).

## Historico de atualizacoes

**Atualizacao 202 — Turbo Start pela Option Part:**
- O "PassiveAssist" da Drift Shop (Atualizacao 200) nao ligou o Turbo Start / Drift Booster: os itens postos saem.
- O jogo so liga o Turbo Start pela propria Option Part (campo `partassist` da tabela de itens do cliente), nao pela
  opcao do dock. Agora a fileira de docks completa com Turbo Start da uma vez a Option Part **Turbo Launch A**
  (`op_B_0006`) e ela ja vai no quadrado de boost da fileira (109/110) se estiver livre; senao fica no inventario.
  Use **Drift + frente com o carro parado**. Vale para o carro do RiceAdmin.
- Drift Booster: nenhuma Option Part do jogo tem; so vem de fabrica em alguns carros (`car_assist` a_00247/a_00248:
  Bugatti Veyron, RUF CTR3, Pagani Zonda Roadster, Koenigsegg CCR, New GT40...). Por isso a fileira que era de
  "Drift Booster" agora da uma **Option Part de tempo de booster** pelo grau dos docks: N = Booster Extension C
  (+0,3 s), S = Booster Limit B (+0,5 s), H/U = Hyper Converting Tuning Booster (+0,7 s), no quadrado de boost da
  fileira (se estiver livre). O dock dessa fileira nao mostra mais "Drift Booster". O admin ganha a de +0,7 s.

**Atualizacao 201 — Espectadores aparecem na sala:**
- Os bots assistindo agora aparecem em **"Spectators: N"** no topo da sala (pacote 823 ObserverCount, DriftCity.exe
  0x51d190: u16 = quantos assistem). Antes so falavam no chat e o contador ficava 0.
- Cada sala sorteia quantos espectadores aceita: de 1 ate `BattleSpectatorsMax` (agora 5).
- Quando abre uma vaga, o espectador entra de 2 a 10 s depois (antes entrava na hora); com mais vagas, um de cada vez.

**Atualizacao 200 — CC no menu, sala da Battle Zone, espectadores, bots online, Drift Booster e pacote 3725:**
- **Menu.bat > Saves:** agora muda o **CC** (moeda dourada). O CC e da CONTA (vale para todos os corredores dela):
  na tela da conta, `C) Mudar CC`; na tela do corredor, `7) Mudar CC`. A lista de contas mostra o CC de cada uma.
- **Battle Zone - sala 16/8 depois da corrida:** o jogo conhecia o bot por mais de um numero (o da sala e o de IA da
  corrida) e, ao voltar para a sala, ele saia so com um: ficava duplicado (16/8, 15/8...). Agora sai com todos e a sala
  inteira e mandada de novo quando voce volta (`BattleRoomResync`).
- **Paciencia dos bots:** bot que entrou sozinho na sua sala vai embora se voce demorar para largar (um de cada vez,
  `BattleBotPatienceSeconds`, 150 s em media). No fim da corrida, cada bot tem 20% de chance de sair quando voce volta
  para a sala (`BattleBotLeaveAfterRaceChance`). Bots da sua party nao saem.
- **Espectadores:** com a sala cheia, ate 3 bots ficam assistindo (`BattleSpectators`, `BattleSpectatorsMax`,
  `BattleSpectatorChance`). Eles narram a corrida no chat da sala ("Que ultrapassagem linda do X em cima do Y!",
  "Z ficou muito pra tras...", quem lidera, quem cruzou primeiro) e, quando abre uma vaga, entram no lugar.
  A narracao segue os portoes da pista; no maximo uma fala a cada ~8 s (`BattleNarratorSeconds`).
- **Bots online:** agora TODOS os bots ficam online (antes cerca de 1/3 ficava offline a cada entrada).
  `BotsOfflineChance` (0 = todos online; 0.34 = como antes).
- **Drift Booster / Turbo Start das fileiras de docks (pacote de nivel):** a opcao aparecia no dock mas nao funcionava.
  O jogo so liga essas opcoes pelo item "PassiveAssist" da Drift Shop (categoria 41) no carro. Agora, com a fileira
  completa, o carro ganha o PassiveAssist (DriftBooster / TurboStart) junto; desfazendo a fileira, ele sai. Vale tambem
  para o carro de policia do RiceAdmin. (Teste: Shift + frente depois do drift.)
- **Pacote 3725 (0xE8D) "unknown packet":** o jogo manda sozinho um valor do carro (soma de dois atributos, ex.: 1310)
  quando ele muda, sem esperar resposta. Agora o servidor recebe e guarda (aparece no log uma vez por valor).

**Atualizacao 199 — Nome:** o projeto passa a se chamar so **Enhanced Rice** (sem "V1"); o zip e a pasta do projeto
agora sao `Enhanced Rice`. Codigo fonte tambem no GitHub (SonicTheUltraHub/drift-city-enhanced-rice).

**Atualizacao 198 — Saves: conta admin montada uma vez so e copia dos saves ao instalar:**
- A conta admin (RiceAdmin) era montada de novo TODA vez que o servidor abria: tirava os carros que voce comprou (ficava
  so a viatura), mexia no inventario e nas missoes. Agora e montada uma vez so por corredor (marca em
  `save/perfil_upado.txt`); depois o que voce fizer com ela fica. `UppedRefreshEveryStart: true` volta ao jeito antigo.
  (Na primeira abertura depois desta atualizacao ela ainda e montada uma ultima vez.)
- O instalador nunca mexeu nos saves (`Rice\save`, `mundo`, `mapas`, `server.json`), mas agora faz uma copia deles antes
  de instalar: `backup_instalador\<data>\saves_antes_de_instalar.zip` (para restaurar, extraia dentro da pasta Rice com
  o servidor fechado). Apagar a pasta do jogo e extrair de novo apaga os saves: para atualizar, instale por cima.

**Atualizacao 197 — Aviso do tutorial para o corredor novo:**
- O tutorial do jogo (Apprentice) nao abre sozinho: o corredor novo nasce no Driver Dome com o carro de treino (TEST
  DRIVER) e a corrida de treino guiada e o Lap Time do Battle Zone (aba 4); no fim ele escolhe o carro inicial. Como nada
  avisava, parecia que o tutorial nao tinha comecado. Agora o chat explica isso ao entrar com um corredor no tutorial.

**Atualizacao 196 — "Pasta em uso" ao apagar/mover a pasta do jogo:**
- O PowerShell escondido do `Start.bat` (espera o jogo fechar para guardar a configuracao de video) rodava de dentro da
  pasta do jogo e deixava ela "em uso" ate o jogo fechar. Agora ele roda a partir da pasta TEMP, com o caminho completo
  dos arquivos. Outras causas: Rice.exe, Menu ou o jogo ainda abertos (veja no Gerenciador de Tarefas).

**Atualizacao 195 — Fechar o servidor fecha o jogo; tela cheia segue o menu:**
- Fechar o servidor ("sair", o X da janela dele ou Ctrl+C) fecha o jogo 2 s depois (`CloseGameWithServer`,
  `CloseGameDelaySeconds`; Menu.bat > Configuracoes > Rede). Fechar pelo Gerenciador de Tarefas nao avisa o jogo.
- Tela cheia: o `Start.bat` tinha `TELACHEIA=1` ("tela cheia sempre"), entao o jogo abria em tela cheia mesmo com a opcao
  desligada no menu, e ao fechar o jogo gravava "tela cheia" no goption.ini (o menu mostrava ligada). Agora
  `TELACHEIA=0`: o jogo abre como estiver no Menu.bat > G > 2 Tela cheia.

**Atualizacao 194 — Instalador reconhece o Init_us.agt "nosso" (sem o aviso de versao diferente):**
- Depois do ajuste de carros, o `Data_US\Init_us.agt` de um cliente com algum carro sem modelo fica com a loja um pouco
  diferente da nossa (md5 diferente), e toda instalacao avisava "versao diferente". E um arquivo mexido pelo erro da
  Atualizacao 190 nunca mais era reconhecido.
- Agora o instalador compara tabela por tabela (`Instalador\modificacoes\init_familia.lst`, gerado por
  `Cliente/ferramentas/init_familia.py`), sem contar as tabelas de carros/loja/cupons:
  - todas iguais as nossas: conta como "ja igual" (o ajuste de carros cuida da loja);
  - todas nossas ou de uma versao original conhecida (mexido por instalacao anterior): troca pelo nosso inteiro
    (`Init_us.agt.completo`, com backup) e o ajuste de carros roda de novo;
  - outro arquivo: fica como esta, como antes.

**Atualizacao 193 — Instalador: erro "A matriz de destino nao era longa o suficiente" ao arrumar os carros:**
- O cabecalho do `Init_us.agt` conta 45 tabelas, mas a 45a e lixo (aponta para 128 MB num arquivo de 880 KB). O leitor do
  instalador aceitava essa entrada; ao gravar a ultima tabela de verdade ele achava que tinha espaco ate la e escrevia
  alem do fim do arquivo. Agora para na primeira entrada invalida (como as ferramentas em Python) e nunca escreve alem do
  fim. Nada era gravado quando dava o erro.

**Atualizacao 192 — V1:** o projeto passa a se chamar **Enhanced Rice V1** (o zip e a pasta do projeto agora sao
`Enhanced Rice V1`).

**Atualizacao 191 (mesma versao) — O servidor fecha sozinho quando o jogo fecha:**
- Depois que o jogo (`DriftCity.exe`) abre, o servidor fica de olho: se o jogo ficar fechado por 10 s
  (`CloseWithGameSeconds`), ele salva os bots e fecha (o resto ja e gravado na hora). Se o jogo nunca abrir, o servidor
  continua aberto. `CloseWithGame: false` desliga (Menu.bat > Configuracoes > Rede).

**Atualizacao 190 (mesma versao) — Instalador: modelos de carro dentro da pasta Patch:**
- No cliente "Latest alpha" os modelos dos carros novos ficam nos pacotes da pasta `Patch`, com o nome
  `Data\vehicle\player\pc_...` (nos de `Data` e so `vehicle\player\...`). O ajuste de carros so procurava o segundo jeito:
  achava "207 carros sem modelo", tirava eles da loja e o servidor deixava de usar (ShopCars.json e carros_cliente.json).
- Agora acha os dois jeitos (389 modelos nesse cliente). E tem uma trava: se a busca nao achar 70% dos carros da nossa
  loja, ela e considerada falha e nenhum carro sai da loja por falta de modelo.
- Quem instalou com o erro: atualize com o instalador novo (ele refaz a loja e as duas listas do servidor).

**Atualizacao 189 (mesma versao) — Menu explica cada opcao:**
- O `Menu.bat` agora mostra, em cinza, embaixo de cada opcao, o que ela faz; cada tela (saves, conta, corredor, bots,
  bot, crews, configuracoes, evento, graficos, mods, backup) comeca com uma explicacao curta de como usar.
- Configuracoes do servidor: cada assunto tem uma explicacao e TODAS as 461 opcoes tem explicacao (antes 120 ficavam
  em branco: o comentario de um grupo de opcoes so valia para a primeira; `config_ajuda.py` agora passa para o grupo
  inteiro, e as portas/IP ganharam explicacao). Tudo tambem em ingles (`res/lang/en.json`).

**Atualizacao 188 (mesma versao) — Armadura da Neo City gasta com o dano tomado:**
- A armadura montada perde `NeoDurabilityPerHit` (1) de durabilidade a cada dano que voce toma na Neo City. O servidor ve
  o dano pela vida do carro no movimento que o jogo manda (0x21D, estado do carro @0x3a, vida @+0x2e): caiu = um dano.
- O desgaste continua sendo aplicado a cada 1 minuto (o do tempo, os tiros e os danos do minuto juntos).

**Atualizacao 187 (mesma versao) — Drinks da Drift Shop funcionando e peca da Neo City ja vem cheia:**
- **EXP drink**: +30% de exp jogando (`DrinkExpBonus`). **MITO drink**: +50% de mito nos premios jogando (`DrinkMitoBonus`;
  nao vale em venda, leilao nem reembolso). Os dois nao valem na recompensa de quest, como o jogo diz.
- **Item Drink**: cada item que cai do HUV (perseguicao na cidade e Neo City) vem em dobro (`DrinkItemDrops`); o segundo
  aparece no chat com "[Item Drink]".
- Vale enquanto o drink estiver no seu Drift Item (inventario da Drift Shop).
- **Durabilidade Neo**: a peca que voce ganha (HUV, loja, leilao...) ja nasce com a durabilidade maxima; antes ficava
  1/maximo ate o proximo minuto.

**Atualizacao 186 (mesma versao) — Som de motor dos carros novos em qualquer cliente:**
- Os carros que o cliente nao tem som (no openDC: 210-217, 302-304, 308-310 e do 314 em diante) ficavam mudos. O
  `som_motor_carros_novos.bat` antigo so funcionava em `Data_US\audio\engine`; no openDC os sons ficam em
  `Data\audio\engine`.
- Agora o instalador (no fim do "1) Instalar / atualizar" e no "3) Arrumar os carros, a loja e o som de motor") copia o
  som de um carro parecido (`Instalador\modificacoes\sons_motor.lst`, o mesmo `Cliente\sons_motor_doadores.lst`) para
  `idle<carro>.wav` e `mid<carro>.wav` na pasta de sons que o cliente tiver. So cria o que falta; o "2) Desfazer" apaga.

**Atualizacao 185 (mesma versao) — Durabilidade dos equipamentos da Neo City:**
- O jogo ja mostra "Durability : atual/maximo" nas pecas da Neo City (arma, Amplifier, Fuse, armadura) e nao deixa montar
  peca com durabilidade 0; o servidor nao usava. Agora:
  - o maximo vem da tabela de itens (`neostats`: 500, 600, 700 ou 800, sobe com o grau); peca nova comeca cheia;
  - na Neo City cada peca montada perde `NeoDurabilityPerMinute` (1) por minuto e a arma perde `NeoDurabilityPerShot`
    (0,2) por tiro (aplicado a cada minuto). Aviso no chat com 10% e quando quebra;
  - no 0 a peca sai do carro e so volta depois de reparar;
  - reparo com os kits da Drift Shop: Weapon/Amplifier/Fuse/Armor Repair Kit 10/25/100 (+durabilidade na peca montada
    desse tipo) e NEO Parts Repair Kit (todas as pecas da Neo, ate as quebradas, em 100%). Duplo clique no kit.
  - `!!durabilidade` mostra a durabilidade das pecas montadas. `NeoDurabilityEnabled: false` desliga.
  - Fica salvo no item (ItemHealth) e em `save/<conta>/<corredor>/neo_durability.json` (quais ja comecaram cheias).

**Atualizacao 184 (mesma versao) — Instalador aplica as tabelas do jogo em clientes diferentes (openDC):**
- Os pacotes que modificamos (`Patch\patch.003/008/009`, `Data_US\World.agt`, `Init`) nao existem do mesmo jeito em
  outros clientes: no openDC fica tudo em `Data` (`Data\World\data.agt`, `Data\Init.agt`) e com outros nomes dentro.
- Agora o instalador procura cada tabela ORIGINAL que modificamos pelo CONTEUDO (md5), em todos os pacotes de `Data`,
  `Data_US` e `Patch` (e subpastas), e troca pela nossa. Tabela diferente da original nao e tocada. So roda quando o
  cliente tem arquivos que o instalador nao conhece; backup em `backup_instalador`.
  - No openDC: lista de entregas (patch.003), itens da loja da Neo City (patch.008), tabela de itens com os precos da
    Part Shop (patch.009) e pecas visuais por carro da Drift Shop (Init). As missoes instantaneas (World.agt) e as 39
    pecas extras da Drift Shop sao de outra versao no openDC e ficam as dele.
  - Ferramentas: `Cliente/ferramentas/tabelas_referencia.py <originais>` gera `Instalador\modificacoes\tabelas.lst` e
    os `tab_*.gz`; `aplicar_tabelas.py <pasta do jogo>` faz o mesmo que o instalador.
- Musica de Natal: no openDC o `music.txt` fica em `Data\audio\bgm` (nao no Data_US): o instalador agora aplica la.
- Emblemas das crews (`Data\GameUI\teammark`): cliente sem essa pasta nao aparece mais como erro (o servidor manda os
  emblemas).

**Atualizacao 183 (mesma versao) — Turbo Start / Drift Booster no carro (admin e fileiras de docks):**
- Desde a Atualizacao 79 a opcao da fileira de docks (Turbo Start / Drift Booster) ia no TERCEIRO espaco de opcao do
  dock de velocidade, e no carro de policia do RiceAdmin ela nao funcionava. Agora vai no PRIMEIRO espaco (quando ele
  esta livre; se tiver uma opcao de Convert, continua no terceiro) e ja e acertada ao entrar no jogo.

**Atualizacao 182 (mesma versao) — Arrumar carros: todos liberados como no nosso cliente:**
- No nosso cliente todos os carros estao liberados: so o nivel e o cupom travam. No do openDC, 249 carros tinham
  `Req Cond` = `EVENT` (so de evento, nao da para comprar). O ajuste de carros (instalador, `adaptar_carros.py`) agora
  copia tambem `Req Cond`, `MakeType` e `closestage` do VehicleDisplay: no openDC o VehicleDisplay dos carros fica
  identico ao nosso.
- Copia tambem a tabela de cupons de carro (Coupon Book: `carros_Cupons.ref`): a linha do mesmo carro e cupom fica
  igual a nossa (Impact, Compact e Monster na linha V5, FondoX na V6...).

**Atualizacao 181 (mesma versao) — Menu.bat nao abria:**
- O aviso "Nao achei o Rice.exe (o Menu.bat fica na pasta do jogo)" (Atualizacao 177) tinha parenteses dentro de um bloco
  `( ... )` do .bat: o `)` fechava o bloco antes da hora e a janela fechava na hora. Texto sem parenteses agora.

**Atualizacao 180 (mesma versao) — Instalador arruma os carros e a loja de QUALQUER cliente:**
- Cada cliente do Drift City vem de um jeito. O do openDC (Data_US vazio, tudo no `Data\Init.agt`) tem todos os modelos,
  mas a loja dele lista so 203 carros e quase todos os carros novos estao como V9, no fim, misturados.
- Agora o instalador (no fim do "1) Instalar / atualizar", ou sozinho no novo "3) Arrumar os carros e a loja para este
  cliente") olha o cliente e arruma com base na nossa loja (`Instalador\modificacoes\carros_*.ref`):
  - le os modelos de carro que o cliente tem (pacotes de `Data`, `Data_US` e `Patch`);
  - na tabela ativa (`Data_US\Init_us.agt`, ou `Data\Init.agt` se o Data_US nao tiver) copia o V, nivel pedido, pontos,
    preco de cada V e a posicao na loja dos carros que o cliente tem; liga carros desligados que tem modelo;
  - loja: V1 a V9 e os especiais (ST, BT, CT, MINI, Xmas) depois do V9; carro que so aquele cliente tem vai no fim do V
    dele; carro sem modelo sai da loja;
  - grava so o que mudou, com backup em `backup_instalador` (o "2) Desfazer" volta). No nosso cliente nao muda nada.
- Quando a loja do cliente e diferente da nossa, o instalador PERGUNTA: `1) Arrumar a loja` (recomendado) ou
  `2) Deixar a loja como esta` - nao mexe nos carros do jogo e os bots usam os carros da loja DAQUELE cliente
  (`Rice\res\ShopCars.json` feito da loja dele, com o V que ele mostra).
- Servidor: o instalador grava `Rice\res\ShopCars.json` (a loja, os bots usam) e `Rice\res\carros_cliente.json`
  (carros que faltam); bots, leilao e trocas nao usam carro que o cliente nao tem.
- Ferramentas: `Cliente/ferramentas/carros_referencia.py` gera os `.ref` e o `ShopCars.json` a partir do
  `Cliente/Init_us.agt` (rode de novo quando mudar carros ou precos); `adaptar_carros.py <pasta do jogo>` faz o mesmo
  que o instalador (`--testar` so mostra).

**Atualizacao 179 (mesma versao) — Leilao: bots compram o seu anuncio conforme o preco:**
- Antes o bot so comparava com o valor de referencia e, acima de 2,5x, nunca comprava. Agora compara com a **loja** (preco
  de compra; cupom = preco do carro) e com o **anuncio mais barato do mesmo item no leilao**:
  - abaixo da loja: vende em poucos minutos (~3 min, `AuctionBuyChanceBargain`);
  - abaixo do leilao: pouco tempo (~5 min, `Cheap`); ate 1,5x: ~17 min (`Fair`); ate 2,5x: ~1 h (`Pricey`);
  - ate 5x: ~8 h (`AuctionBuyChanceExpensive`); acima disso ainda pode vender, cada vez mais raro (`AuctionBuyChanceVeryExpensive`).
- Item muito valioso tem menos compradores: acima de `AuctionRareItemValue` (100000) a chance cai (10x o valor = metade).
- Ao anunciar, o chat diz a faixa do preco (ex.: "Preco justo: deve vender em menos de 1 hora") e o console mostra a chance.

**Atualizacao 178 (mesma versao) — Loja de carros: FondoX entre os V6:**
- A loja de carros ordena pela coluna "Display" do `VehicleDisplay.xlt` (V5 72-91, V6 93-114, V7 115-137). O FondoX (agora
  V6) estava com 121, no meio dos V7; foi para 102, logo depois do Fondo, e os carros entre as duas posicoes andaram uma
  casa (sem repetir numero). Feito por `Cliente/ferramentas/cupons_v.py` (AFTER) junto com a mudanca dos cupons; o instalador leva o Init_us.agt novo.

**Atualizacao 177 (mesma versao) — Menu.bat so na pasta do jogo:**
- O `Menu.bat` nao vai mais para dentro da pasta `Rice` (compilacao e instalador); fica so na pasta do jogo, do lado do
  `Start.bat`. Ao instalar/atualizar, o instalador apaga a copia antiga de `Rice\Menu.bat`.

**Atualizacao 176 (mesma versao) — Coupon Book: cada carro na linha do V dele:**
- O livro mostrava Monster (V5) na linha V4, Impact (V5) na V6 e Compact (V5) na V8. Agora os tres ficam na linha V5 (a
  linha vem da coluna "car grade" da tabela de cupons do Init_us.agt). O FondoX vira V6 (o carro e a linha do livro; o nivel
  pedido 50, os pontos do V6 e o preco do cupom na linha V6).
- Compact (estava sem pontos): durabilidade e boost 20 pontos acima de velocidade e aceleracao, na soma de cada V
  (V5: 80 / 80 / 100 / 100; V8: 155 / 155 / 175 / 175; V9: 190 / 190 / 210 / 210).
- Feito em `Data_US/Init_us.agt` com `Cliente/ferramentas/cupons_v.py` e no servidor (`res/VehicleList.json` e
  `res/VehicleUpgrade.json`). O instalador leva a modificacao nova do Init_us.agt.

**Atualizacao 175 (mesma versao) — Instalador "Install Enhanced Rice" e sem limpeza do chat:**
- O instalador agora se chama **Install Enhanced Rice.exe** (sai em `Pacote\Install Enhanced Rice.exe` ao compilar; o projeto
  continua na pasta `Instalador`).
- A limpeza do chat (a cada 15 minutos, 60 linhas vazias e a dica do !!help) foi desligada: `ChatResetMinutes` = 0.

**Atualizacao 174 (mesma versao) — Cartas dos bots, LegendsDC, amPerl e Xerender13, multiplicador 1,5x:**
- Cartas da corrida: o jogo encerra as IAs quando voce chega e elas nao mandam a chegada delas, entao nenhum bot pegava
  carta. Agora os bots que ainda estavam correndo quando o primeiro chegou tambem pegam (quem desistiu antes, nao), e pegam
  logo depois de voce escolher a sua (ou 6 s depois da tela das cartas, se voce nao escolher).
- A crew LendasDC virou **LegendsDC**, so com o RiceAdmin (lider) e o bot **amPerl** (nivel 100, Shadow Xmas V9, pecas
  Sorched Ouranos). Joaopvbr, Marsteh e Brunizin.X sairam da crew e desceram para o nivel 90, com as pecas do nivel 90
  (Gaia). No mod Escada de crews a ultima crew e a LegendsDC e o chefe dela e o amPerl. Saves antigos sao convertidos.
- Bot novo **Xerender13** (nivel 88, N1 V9, competitivo, sem crew, pecas do nivel 85).
- Bots novos (amPerl e Xerender13) ainda nao correm como IA na Battle Zone: o nome deles nao existe na tabela de IA do jogo.
- Multiplicador escondido dos pontos (DriftCity.exe) agora 1,5x nos quatro (velocidade, durabilidade, aceleracao, boost);
  antes 2,35 / 1,5 / 2 / 1,5. DriftCity.exe novo (md5 eab9ae30...), tambem no instalador.

**Atualizacao 173 (mesma versao) — Voltar ao lobby no meio de uma entrega:**
- Voltar para o Driver Dome com uma entrega em andamento deixava o carro no vazio (so ceu e chao preto): o jogo desiste da
  entrega e o servidor respondia com a posicao do posto (787), que e da cidade, e o carro ia para la dentro do lobby. Agora o
  servidor espera a troca de area e so leva o carro ao posto se voce continua na cidade. Desistir na cidade continua igual.

**Atualizacao 172 (mesma versao) — Rice nao fecha sozinho:**
- Um erro sem tratamento numa thread do servidor (timers dos bots, trafego, cidade...) fechava o Rice.exe na hora (por
  exemplo ao voltar para a selecao de corredor) e o jogo voltava para a selecao de servidor. Agora o erro vai para o
  `erros.log` (pasta do Rice.exe, com o caminho completo do erro), aparece em vermelho no console e o servidor continua
  (`legacyUnhandledExceptionPolicy` no Rice.exe.config).
- Um Enter sem querer na janela do Rice fechava o servidor. Agora so fecha digitando `sair` (ou `exit`).

**Atualizacao 171 (mesma versao) — Tamanho da interface no menu:**
- `Menu.bat` > `G) Graficos do jogo` > `9) Tamanho da interface`: Grande (1366x768), Media (1600x900) ou Pequena (1920x1080),
  sempre em tela cheia. O jogo nao tem escala de interface (os menus como Dealership, Battle Zone e Crew Center tem tamanho
  fixo de 1024x768); em tela cheia o monitor estica a resolucao menor para a tela inteira, entao tudo fica maior.

**Atualizacao 170 (mesma versao) — Resolucao alta cortada (escala de tela do Windows):**
- Com a escala de tela do Windows em 125% (ou mais), o jogo em resolucao alta (ex.: 1920x1080) ficava com zoom e cortado
  (sem minimapa, velocimetro e barra de baixo): o Windows dizia ao jogo que a tela era 1536x864. O `Start.bat` agora marca
  o DriftCity.exe como "DPI alto" para o seu usuario (o mesmo que Propriedades > Compatibilidade > Alterar configuracoes
  de DPI alto > Aplicativo), mantendo as outras opcoes de compatibilidade. `Start.bat` com fim de linha do Windows (CRLF).

**Atualizacao 169 (mesma versao) — Graficos do jogo no menu:**
- `Menu.bat` > `G) Graficos do jogo`: resolucao (lista ou LARGURAxALTURA), tela cheia, antisserrilhado, sombras, distancia de
  visao, detalhes (carro, cenario, efeitos, texturas) e qualquer item da secao [Graphic] do `Data\Init\goption.ini`.
- Grava tambem no `goption_salvo.ini` (a copia que o Start.bat coloca de volta antes de abrir o jogo). Backup na primeira vez
  (`goption.ini.antes_do_menu`) e opcao para voltar. Feche o jogo antes (ele grava por cima quando fecha).

**Atualizacao 168 (mesma versao) — Multiplicador por V tirado:**
- O teste do multiplicador dos pontos pelo V do carro nao funcionou no jogo e saiu: o DriftCity.exe voltou a ser o de antes
  (md5 95d7f18c...) e todo carro usa o multiplicador fixo de sempre (na epoca: velocidade 2,35x, durabilidade 1,5x,
  aceleracao 2x, boost 1,5x).

**Atualizacao 167 (mesma versao) — Ingles e portugues:**
- Idioma novo `Language` no server.json: `en` (ingles, padrao) ou `pt`. Vale para o console do servidor, o chat do jogo
  (sistema e bots), o menu e o instalador. Troque em `Menu.bat > 9) Idioma`; o instalador pergunta o idioma ao abrir.
- As traducoes ficam em `Rice/res/lang/en.json` (portugues -> ingles, ~2.200 textos, incluindo a ajuda das configuracoes).
  O codigo continua em portugues; o que o servidor ainda nao conhece aparece em portugues.
- `README.md` (guia em ingles) e guia rapido em portugues no comeco deste LEIA-ME; `Start.bat`, `Menu.bat` e o leia-me dos
  mods com ingles e portugues. No menu, "Digite SIM/APAGAR" aceita YES/DELETE no ingles.

**Atualizacao 166 (mesma versao) — Admin so com o carro de policia, eventos automaticos e bots falando do evento:**
- Conta admin (RiceAdmin): um carro so, o BMW Patrol (policia, V9), com as pecas upadas e duas fileiras de docks U: uma
  com Turbo Start (start booster) e outra com Drift Booster. Os outros carros do RiceAdmin saem da garagem ao abrir o
  servidor (`UppedOnlyOneCar`; opcoes em `UppedCarAssists`).
- Evento especial comeca no automatico (`Evento: "auto"`) e cada evento dura o mes inteiro: outubro Halloween, dezembro
  Natal, fevereiro Valentine. No menu: Nenhum, Natal, Valentine, Halloween ou Automatico.
- Bots falam do evento do momento, cada um do seu jeito (amigavel, competitivo, comerciante, calado), e os outros reagem.
  Se voce falar do evento no chat ("feliz natal", "halloween", "valentine"...), 1 a 3 bots respondem.

**Atualizacao 165 (mesma versao) — DriftCity.exe vai dentro do instalador:**
- O instalador leva o DriftCity.exe inteiro (o do mini-Rice ja com as modificacoes do Offline, abre sem o launcher e sem o
  GameGuard). Qualquer DriftCity.exe da pasta (o original protegido, o do mini-Rice ou outro) e trocado, com backup em
  `backup_instalador`. Nao precisa mais procurar o exe do mini-Rice. Os outros arquivos do jogo continuam so com as diferencas.

**Atualizacao 164 (mesma versao) — Eventos especiais, mods no menu e compilar so o instalador:**
- Evento especial: `Menu.bat` > `7) Evento especial` (ou "Evento" no server.json): `natal` (neve, arvore de Natal e roupa de
  Papai Noel), `valentine`, `halloween`, `auto` (pela data do PC: Natal 1/dez-6/jan, Valentine 1-14/fev, Halloween 20/out-2/nov)
  ou nenhum. Liga os flags de evento do proprio cliente; vale ao entrar no jogo. O cliente nao tem visual de Pascoa.
- Musica de Natal (RocknRoll Rudolf) do lobby fica em loop (na cidade tocam as musicas normais): o instalador muda o Data_US/audio/bgm/music.txt (coluna
  TurnOver das faixas xmasevent = a propria faixa; antes trocava para a musica normal da cidade quando acabava).
- Mods: `Menu.bat` > `8) Mods` instala, liga, desliga, volta ao padrao ou desinstala a Escada de crews. O mod original agora fica em
  `Rice/res/mods` (a pasta `Mods` da raiz saiu).
- Instalador e o primeiro projeto da solucao (o que abre ao compilar/F5). Ele compila o Rice junto e, no fim, so sobra
  `Pacote\Instalador.exe` (a pasta montada do servidor e apagada, porque ele ja vai dentro do instalador).

**Atualizacao 163 (mesma versao) — Lista de pacotes vai junto no instalador:**
- O antigo Rice/Packets.txt virou Rice/res/packets.lst (nomes dos pacotes para o log). Ele e copiado na compilacao para res/ e entra no pacote do instalador, entao vai tudo de uma vez.
- Se mesmo assim faltar, o Rice sobe normal (antes fechava com "Nao foi possivel localizar o arquivo Packets.txt").
- O instalador agora leva o servidor inteiro: tudo o que a compilacao gera na pasta do Rice.exe (exe, dlls, res, x86, amd64, packets.lst e qualquer arquivo novo). Ficam de fora so os saves, a configuracao (server.json) e os arquivos de mundo/mapas de quem testou na pasta bin.
- Start_Admin.bat removido: o instalador traz so o Start.bat (no login, digite "admin" para a conta admin).

**Atualizacao 162 (mesma versao) — Pastas organizadas, menu de edicao e instalador:**
- **Pastas do servidor (pasta do Rice.exe):** na primeira vez que o servidor abrir, os arquivos soltos vao sozinhos para o lugar novo:
  - `save/` - `db.sdf` (contas, corredores, carros, itens) e, para cada conta, uma pasta `save/<conta>/<corredor>/` (ate 3 corredores) com o que e daquele corredor: amigos bots, licencas, cupons, Drift shop, alquimia, caixas, docks de nivel e Arbeit.
  - `mundo/` - bots, crews, leilao e escada de crews (o que e do servidor inteiro).
  - `mapas/` - pontos aprendidos (Driver Dome, base ROAD, tiros da Neo City, Rush).
  - `backups/` - os backups feitos pelo menu.
- **Menu (`Menu.bat` na pasta do jogo, ou `Rice.exe menu`):** com o servidor fechado:
  - Saves: ver contas e corredores; criar, renomear e apagar conta; renomear e apagar corredor; mudar nivel, mito e o V dos carros. Apagar a conta admin (ou o perfil upado) faz ela voltar do padrao ao abrir o servidor: nivel 123, carro de policia V9 e as pecas upadas.
  - Bots: procurar, mudar nome, nivel, carro, V, personalidade, cidade, habilidade e mito; apagar um bot ou recriar todos.
  - Crews: nome (os bots da crew acompanham), descricao e nivel minimo.
  - Configuracoes: todas as do servidor, por categoria e com explicacao (`res/config_ajuda.json`, gerado por `Cliente/ferramentas/config_ajuda.py`). So o que voce muda vai para o `server.json`; "P" volta ao padrao.
  - Backup: faz um .zip de save, mundo, mapas e server.json, e restaura um backup (antes guarda os saves de agora).
- **Instalador.exe (um arquivo so, sem os arquivos do jogo):** ao compilar a solucao (Rice.sln), o Visual Studio monta `Pacote\Instalador.exe` (~2 MB + servidor). Ele NAO leva os arquivos do jogo: so as **modificacoes** (as diferencas, na pasta `Instalador\modificacoes`, 1,6 MB no total, embutidas no .exe). Quem instala precisa ter o Drift City dele.
  - Coloque o `Instalador.exe` do lado do `DriftCity.exe` e abra. Ele confere cada arquivo do jogo (md5), mostra o que vai ser modificado, pede confirmacao, guarda backup em `backup_instalador\<data>` e aplica a modificacao em cima do arquivo original do usuario. Tambem coloca o servidor (pasta `Rice`), `Start.bat`, `Menu.bat` e o LEIA-ME. Os saves de quem ja joga nao sao tocados.
  - Arquivos modificados: `DriftCity.exe` (so o desempacotado), `Data\Init.agt`, `Data_US\Init_us.agt`, `Data_US\World.agt`, `Patch\patch.003`, `patch.008`, `patch.009` e os emblemas `Data\GameUI\teammark\01-10`.
  - No login: "admin" entra na conta admin; qualquer outro nome cria uma conta nova na hora (sem ser admin).
  - `DriftCity.exe`: so e modificado se for o desempacotado (o do DC Offline / mini-Rice, ou outro sem a protecao, do mesmo tamanho: ai so os 33 bytes que o projeto muda). O protegido (que so abre pelo launcher) fica como esta.
  - Opcao 2 desfaz a ultima instalacao (volta os arquivos e tira os novos; a pasta Rice com os saves fica).
  - Mudou algum arquivo do Cliente? Gere as diferencas de novo: `python3 Cliente/ferramentas/gerar_patches.py "<pasta do jogo limpo>"` (aceita mais de uma pasta/versao; ver o comeco do arquivo).
- O `Start_AutoLogin.bat` saiu (o login automatico nao funcionava); no lugar dele vem o `Start.bat`, que abre o servidor e o jogo e voce digita o login.
- A conta normal so e criada para levar corredores antigos da conta admin (se apagar ela pelo menu, nao volta sozinha).

**Atualizacao 161 (mesma versao) — Battle Zone: bots aparecem como jogador na sala:**
- Na sala de espera os bots (os que entram sozinhos, os da party, os do desafio e os do botao de IA) aparecem no slot igual a um jogador: nome, nivel, carro e crew, em vez de "A.I.".
- Ao carregar a corrida eles viram IA no mesmo assento (o seu jogo pilota, como antes); so quando voce sai da pista e volta para a tela da sala (302 LeaveArea, depois da tabela de resultado) eles voltam a aparecer como jogador (se o jogo nao avisar, depois de 90 s).
- Cartas do fim da corrida: os bots so escolhem as cartas deles quando a corrida acaba (todos chegam ou fim dos 10 s), nao mais junto com a sua.
- So pega carta quem cruzou a linha de chegada antes da corrida acabar (voce e os bots); quem nao terminou fica sem carta.
- Expulsar continua funcionando (botao de expulsar ou clique no assento). `BattleBotsAsPlayers` = false volta ao "A.I.".

**Atualizacao 160 (mesma versao) — Battle Zone: fim da corrida e Complete Bonus Reward:**
- **Fim da corrida:** quando voce cruzava a chegada, o seu jogo dava "desistiu" para todas as IAs e a corrida acabava na hora. Agora, depois do primeiro na chegada, a corrida espera todos chegarem ou a contagem de 10 s (`BattleFinishSeconds`), como no jogo; quem nao chegou aparece com "To Goal".
- **Complete Bonus Reward (estrelas, Exp e Mito):** o jogo so contava jogadores de verdade, entao com voce sozinho a tabela ficava zerada. Patch no `DriftCity.exe` para contar os bots da sala (ferramenta `Cliente/ferramentas/bonus_ia.py`; `--desfazer` volta o original). Ja troquei o do seu PC; o original ficou como `DriftCity.exe.antes_bonus`.
- O servidor paga esse bonus para quem termina, com a mesma conta do jogo (Beginner = Bronze x3; Pro = Prata/Ouro x4/x6/x8/x16; estrelas pelo numero de corredores), somado na exp/mito da tabela de resultado e avisado no chat. `BattleCompleteBonus` = false desliga.

**Atualizacao 159 (mesma versao) — Battle Zone: corrida travada corrigida, bots entram na sua sala:**
- **Corrida travada (bots parados num canto, voce "1/16" em ultimo):** veio da 158, que mandava as IAs uma segunda vez antes do GO; o jogo perdia o controle delas. Desfeito: volta como antes (os bots aparecem na largada).
- **Bots entram sozinhos na sala que voce cria:** a cada ~12 s (`BattleBotJoinSeconds`) entra mais um bot online que cabe na regra da sala (Beginner/Pro x4...x16) e fala no chat da sala. `BattleBotsJoinRoom` = false desliga.
- **Tirar bot da sala:** o botao de expulsar do assento (o jogo manda o RoomBanish 470, que o servidor nao respondia) agora tira o bot. Quem voce tirou nao volta sozinho para aquela sala.
- **Entrada dos bots:** entram de 1 a 3 de uma vez (`BattleBotJoinMax`), a cada 2 ate 12 s (`BattleBotJoinSeconds`), ate a sala encher.

**Atualizacao 158 (mesma versao) — Battle Zone: arenas, apresentacao, cartas, modo equipe e carro de policia:**
- **Salas dos bots nas abas certas:** as salas de desafio agora aparecem so em Beginner ou Pro (nao mais em Mission), com as regras do jogo:
  - Beginner: nivel 1-30 e carro V1-V4.
  - Pro, pelo EXP Reward: x4 nivel 11+ e V1-V4; x6 nivel 31+ e V1-V6; x8 nivel 51+ e V1-V8; x16 nivel 71+ e V1-V9.
  - O bot so abre sala numa arena em que ele cabe, e so entram bots que cabem nela. Voce tambem: fora da regra, a sala nao deixa entrar e o chat diz por que.
  - Na sala que voce cria, os bots da party (e os que voce poe pelo botao de IA) seguem a regra da aba/EXP da sala; quem nao cabe avisa na party.
  - O mano a mano da escada de crews continua abrindo sempre (Pro pelo V do carro do bot).
- **Apresentacao antes do GO:** o servidor manda as IAs de novo quando o mapa ja carregou, para os bots aparecerem no grid antes da largada.
- **Cartas do fim da corrida:** agora saem pecas, options e docks do grau do seu nivel (1-30 N, 31-59 S, 60-89 H, 90+ U), das mais altas que voce ja pode usar; as vezes um item de uso. Os bots tambem pegam pelo nivel deles.
- **Modo equipe:** cores corrigidas (time azul com carro azul, vermelho com vermelho) e o placar final soma os pontos de cada time (1o lugar = mais pontos, quem nao termina = 0).
- **Carro de policia (Leo Patrol):** so o admin. Nenhum bot tem, compra, troca ou corre com ele; quem tinha trocou por um carro da loja do mesmo V (`PoliceCars`).
- **Correcao:** na atualizacao 157 o `Data_US\Init_us.agt` do seu PC foi trocado por uma versao velha por engano. Ja coloquei o arquivo certo de novo (igual ao `Cliente\Init_us.agt` do projeto).

**Atualizacao 157 (mesma versao) — Drift Shop (V do carro e sticker) e pistas de IA da Battle Zone:**
- **Drift Shop > Tuning, subir o V:** o servidor nao respondia o pedido (91), por isso nada acontecia. Agora sobe 1 V por vez, cobrando o preco do V novo da tabela do jogo (`upgrademito`, ex. Kicker V2 = 28.000 mito) e pedindo o mesmo nivel da concessionaria. Fica salvo no banco e o jogo atualiza os atributos na hora.
- **Drift Shop > Sticker > Register Sticker:** a janela travava porque o jogo esperava a resposta da checagem do nome (93/94) e do envio da imagem (71/72). Agora o nome e sempre aceito e o registro termina (offline nao tem servidor de imagens: a imagem fica so no seu jogo).
- **Battle Zone:** voltou como o original: o icone de IA so aparece nas 16 pistas que tem o caminho dos bots gravado (Moon Palace). As outras pistas continuam na lista para correr sem bots, sem o icone de IA. Ja troquei o `Data_US\Init_us.agt` do seu PC; o `Cliente\Init_us.agt` do projeto tambem esta assim. Ferramenta: `python3 Cliente/ferramentas/pistas_ia.py --original` (sem `--original` liga o icone em todas de novo).

**Atualizacao 156 (mesma versao) — Neo City: bots voltam para perto de voce e alcance 85 m:**
- Bot da party que ficar a mais de 350 m de voce (preso atras de um HUV, num canto...) aparece de novo atras de voce e volta a te seguir. Config `NeoBotTeleportDistance` (0 desliga).
- Alcance de tiro dos bots voltou para 85 m (`NeoBotHitRange` = 85).
- Quando um bot destroi um HUV, o chat mostra quanto exp e mito voce ganhou (e quanto a party ganhou).
- Na Neo City os bots nao pulam mais para tras do HUV: vao dirigindo ate ele e atiram de onde estao.
- Os bots nao atiram mais no ponto de respawn: o HUV so vira alvo depois que o seu jogo ja colocou ele no mapa.
- Os bots nao perdem mais vida ao atirar. Agora so os HUVs tiram vida deles: cada HUV ataca quem estiver mais perto dele (voce ou um bot). HUV/HUB atira ate 60 m (`NeoHuvShootRange`), a cada 1,5-2,5 s; o HUS so tira vida batendo (encostando, `NeoHuvRamRange` = 6 m).
- Os bots da party tambem ganham exp e mito pelos HUVs destruidos: quem destruiu ganha o premio inteiro, os outros `NeoBotKillShare` (50%). Quando e voce que destroi, cada bot ganha 50%. Eles sobem de nivel e fica salvo.

**Atualizacao 155 (mesma versao) — Neo City: bots com o dano REAL da arma:**
- O dano do bot agora sai da tabela de armas do proprio jogo (Init\WeaponList, extraida para `Rice/res/neo_weapons.json`, 191 armas U Cannon/Railgun/Wave/Mine). E o mesmo "Weapon Attack" que o painel NEO Performance mostra, sem nerf nem buff: ex. U Cannon 108 = 11623-13018 por tiro, U Cannon 98 = 7748-8678.
- Cada arma usa os valores dela: dano base + aleatorio, chance de critico (x2), rajada (Railgun 3 balas), tempo de recarga (Cannon 1,2 s, Wave 0,6 s...), alcance (Cannon 200 m; Wave/Mine so de perto, pela area da explosao) e precisao (armas teleguiadas/automaticas sempre acertam, as outras pela % de precisao).
- Com duas armas, o bot usa a mais forte que alcanca o HUV. Sem arma continua batendo (dano de batida pelo nivel).
- `NeoBotDamage`/`NeoBotDamageFactor`/`NeoBotHitSeconds` nao sao mais usados nos tiros com arma.

**Atualizacao 154 (mesma versao) — Neo City: dano dos bots calculado e HUB "preto flutuante" corrigido:**
- **Dano calculado**: o tiro do bot agora tira o dano medio dos SEUS tiros nos HUVs (que o jogo calcula com a sua arma e a defesa do HUV), ajustado pelo nivel da arma dele / nivel da sua arma, x `NeoBotDamageFactor` (0.8), com variacao de +-15%. Antes era um valor fixo (900 x nivel), bem mais forte que o seu. Antes de voce acertar o primeiro HUV: `NeoBotDamage` (300) x nivel da arma/80.
- **HUB preto flutuante**: o tiro do bot que (pela vida estimada) zerava o HUV ia com vida 0 e o jogo deixava o HUB "morto" (preto, flutuando) mas ainda andando e atirando. Agora o tiro do bot nunca manda vida 0: o jogo destroi o HUV sozinho quando o dano acaba com a vida dele, e a missao conta pelo 3820 (como na 153).
- **HUB que renasce**: o HUB da missao volta sempre com o numero 1; o servidor continuava achando que ele estava destruido e os bots nao atacavam o novo. Agora cada HUB novo comeca do zero.

**Atualizacao 153 (mesma versao) — Neo City: o HUB da missao destruido pelos bots conta; bots vao atras e param perto:**
- **HUB da missao**: o objetivo "Destroy the Patrol HUB" e o HUB, que tem o numero 1 - o mesmo numero do seu carro. O servidor descartava esse numero ao ver quem destruiu (achava que era voce), entao o HUB destruido pelos bots nunca era contado. Agora usa o numero do destruido (@2 do 206) e manda o progresso (3820). Os HUVs comuns (escolta, sem grupo) nao sao mandados.
- **Movimento**: o bot com arma vai atras do HUV na velocidade normal (e ja atira no caminho, ate 100 m) e so para mirando quando chega a `NeoBotHoldDistance` (35 m). Parado, so larga se o HUV passar de 1,6x isso ou sumir por 2,5 s. Antes ele parava a 100 m e ia chegando devagar.

**Atualizacao 152 (mesma versao) — Neo City: HUV dos bots conta na missao (3820), bots param de verdade, alcance 100 m:**
- **Conta na missao**: o seu jogo so manda o progresso (3819) quando VOCE destroi o HUV, e o objetivo so sobe com a resposta do servidor (3820). Agora, quando um bot destroi um HUV/HUB, o servidor manda o 3820 com a missao em que voce esta (do 3815/3819) e o grupo/nivel do HUV: o objetivo sobe (ex.: Destroy the Patrol HUB). O console mostra "HUV ... do bot contado na missao".
- **Param de verdade**: o bot fica parado mirando ate 2,5 s depois de perder o alvo (o alvo some e volta entre um tique e outro e ele andava devagar).
- **Alcance**: `NeoBotHitRange` 100 m (antes 60). Eles so largam o alvo e voltam a te seguir se voce passar de `NeoBotEngageRange` 200 m (antes 120).
- Os bots agora tambem ganham credito pelos HUBs (numero baixo), nao so pelos HUVs.

**Atualizacao 151 (mesma versao) — Neo City: bots param para atirar e o HUV deles conta na missao:**
- **Param e miram**: bot com arma e com HUV no alcance (`NeoBotHitRange`) para o carro onde esta, vira para o HUV e atira, em vez de continuar te seguindo (`NeoBotHoldToShoot`). Sem alvo (ou se voce se afastar mais de `NeoBotEngageRange`), volta a te seguir.
- **Conta no objetivo**: o jogo so conta o HUV no objetivo (ex.: Destroy the Patrol HUB 1/3) quando recebe o golpe final com vida 0 e com o seu nome. Agora o servidor acompanha a vida de cada HUV (a que o seu jogo informa nos seus tiros, menos os tiros dos bots; sem informacao, `NeoHuvHpPerLevel` 70 x nivel, chefe x5) e manda o tiro do bot que zera assim: o HUV e destruido e conta para voce.

**Atualizacao 150 (mesma versao) — Neo City: bots com arma atiram, sem arma batem:**
- **Equipamento Neo**: so os 20 bots da Neo City ja vem com o equipamento do nivel deles. Os outros (Joaopvbr, Marsteh, Brunizin.X...) comecam sem: voce da na troca, e fica salvo.
- **Com arma x sem arma**: bot com arma da Neo City para a `NeoBotStandoff` (15 m) do HUV e atira; bot sem arma vai para cima do HUV e so tira vida encostado (`NeoBotRamDistance` = 5 m).
- **HUV que renasce**: o HUV destruido ficava uns segundos na lista e os bots ficavam presos nele (e nao atiravam em mais nenhum). Agora o destruido sai na hora e eles passam para o proximo, inclusive o que acabou de renascer.
- **Vida da party**: a vida do bot da party vai no carro dele em qualquer lugar (cidade, Driver Dome, base, missao) - a lista da party nao mostra mais a sua vida neles.
- **Tiro desenhado de novo (`NeoBotShowShots` = true)**: o molde agora e so o seu tiro de arma do carro (Cannon, Railgun, Wave), que vai com o seu carro; o da Mine vai como objeto (era ele que desenhava um HUV) e nao e mais usado. O tiro do bot sai do carro DELE, com a arma DELE, na direcao do HUV. Atire uma vez com Cannon/Railgun/Wave para gravar o molde.

**Atualizacao 149 (mesma versao) — Neo City: HUV dos bots conta na missao, equipamento salvo:**
- **Equipamento salvo**: o Joaopvbr, o Marsteh e o Brunizin.X perdiam o equipamento da Neo City que voce deu toda vez que o servidor abria (as pecas deles eram refeitas do zero). Agora o equipamento Neo e os kits ficam.
- **Conta na missao**: o tiro do bot vai com o seu nome (`NeoBotKillsCountForYou` = true), entao o HUV/HUB que ele destroi conta no objetivo (ex.: Destroy the Patrol HUB 0/3). Voce continua ganhando `NeoBotKillShare` da exp/mito.
- **Atacam tambem os que acabaram de aparecer**: HUV que ainda nao andou (acabou de nascer) e HUB parado nao mandam posicao; agora os bots usam o ponto onde ele apareceu e atiram nele tambem.
- **Colam no HUV**: `NeoBotStandoff` agora e 6 m (antes 18): eles chegam bem perto do HUV que estao perseguindo, sem bater.
- **Tiro desenhado desligado** (`NeoBotShowShots` = false): o jogo desenhava o tiro do bot como um carro de HUV voando. O dano continua; so nao aparece o projetil.

**Atualizacao 148 (mesma versao) — Neo City: tiro dos bots voando, kits para a party, vida na base:**
- **Tiro dos bots**: o tiro (555) leva o movimento do PROJETIL, nao do carro. Antes ia o carro do bot parado (aparecia um objeto em cima dele por um instante); agora o projetil sai do carro do bot e voa ate o HUV com a velocidade do seu tiro.
- **Tiro salvo**: o seu ultimo tiro fica salvo em `neo_fire.json`; os bots ja atiram (com o desenho) mesmo antes de voce atirar na missao.
- **Seu kit vale para a party**: ao usar o Personal Recovery Kit (ex.: 200), cada bot da party tambem ganha essa vida.
- **Kits para os bots**: na troca o bot aceita kits de recuperacao (a pilha toda). Na Neo City, quando um bot da party ficar abaixo de `NeoBotKitPercent` (75%), um bot que tenha kit usa um (cura a party) e avisa no chat da party quantos restam. Intervalo minimo `NeoBotKitSeconds` (6 s).
- **Vida na base ROAD**: na base a vida dos bots volta sozinha, `NeoBaseRegenPercent` (5%) por segundo; bot destruido que estava esperando volta na hora.

**Atualizacao 147 (mesma versao) — Neo City: a party vai atras dos HUVs e atira de perto:**
- Com HUV a ate `NeoBotEngageRange` (120 m) de voce, os bots da party saem de perto de voce e vao atras deles (um HUV para cada, os menos perseguidos primeiro, para atacar todos), mas param a `NeoBotStandoff` (18 m) do HUV e atiram de la, sem encostar. Sem HUV perto, voltam a te seguir.
- Corrigido: no tiro do bot o carro dele virava o HUV por um instante (o molde do seu tiro leva o modelo do alvo); agora o carro do bot fica o dele.
- Os bots agora atiram tambem nos HUBs/HUVs de numero baixo (antes so nos de numero dado pelo servidor).

**Atualizacao 146 (mesma versao) — Neo City: party atira de longe, tiro aparece, vida certa na base:**
- **Atiram de longe**: na missao a party nao vai mais bater nos HUVs. Ela continua te seguindo e cada bot atira no HUV mais perto dele, ate `NeoBotHitRange` (agora 60 m), so nos HUVs a ate `NeoBotEngageRange` (120 m) de voce.
- **Tiro visivel**: o servidor guarda o seu ultimo tiro (pacote 555 VehicleFire, que o jogo manda quando voce atira) e usa de molde para mandar o tiro do bot, saindo do carro dele e mirando no HUV. Voce precisa atirar uma vez na missao para os tiros dos bots aparecerem. O console mostra os 3 primeiros tiros seus ("seu tiro (555)") para conferir o formato.
- **Vida na base ROAD**: na base os bots tambem mostram a vida deles (antes so na missao).
- **Sem recuperar sozinho**: a vida do bot nao volta mais aos poucos; so volta cheia quando ele e destruido e reaparece, ou no comeco de um capitulo novo.

**Atualizacao 145 (mesma versao) — Neo City: bots com vida e equipamento proprios, HUVs atirando neles, troca de equipamento Neo:**
- **Vida propria dos bots da party**: o carro do bot levava o seu estado (por isso a vida dele era igual a sua). Agora o servidor poe a vida dele no estado do carro: `NeoBotHpBase` (2500) + nivel x `NeoBotHpPerLevel` (75) + armadura x 2.
- **Os HUVs atiram de volta**: o HUV que o bot esta atacando tira vida dele (nivel do HUV x `NeoHuvDamagePerLevel` = 6, chefe x3, menos a armadura). Vida zerada: o bot e destruido, some e volta em `NeoBotRespawnSeconds` (20 s) com a vida cheia (aviso no chat). Fora de combate a vida volta aos poucos. (O tiro do HUV no bot e calculado pelo servidor; o jogo ainda nao mostra a animacao do HUV mirando nele.)
- **Equipamento da Neo City nos bots**: cada bot da Neo City ja vem com 2 armas de tipos diferentes, armadura, Amplifier e Fuse do nivel dele. A arma deixa o tiro dele mais forte e a armadura aumenta a vida e diminui o dano dos HUVs.
- **Troca**: os bots aceitam arma, armadura, Amplifier e Fuse da Neo City (ate o nivel dele + 9). Arma: 2 lugares (a do mesmo tipo troca a dele desse tipo); os outros: 1. Ele equipa e te devolve o que tinha.

**Atualizacao 144 (mesma versao) — Neo City: caminho da base ROAD fixo:**
- Os 1409 pontos da base ROAD que voce gravou andando (neo_dome_points.json) agora vem dentro do projeto (`res/neo_dome_points.default.json`): se o arquivo for apagado ou o servidor for instalado do zero, os bots da Neo City usam esse caminho.
- O servidor parou de gravar a base enquanto voce anda: os bots so andam/estacionam onde voce ja tinha passado. Para voltar a gravar: `NeoDomeLearn` = true. O Driver Dome normal continua como estava.

**Atualizacao 143 (mesma versao) — Neo City: a party nao fica mais pulando pelo mapa:**
- Na Neo City o seu jogo manda a posicao de todos os HUVs do mapa, e a party ia atras de qualquer um (pulava de um lado para o outro e destruia HUVs longe de voce). Agora ela so vai atras dos HUVs a ate `NeoBotEngageRange` (120 m) de voce; sem HUV perto, volta a te seguir.

**Atualizacao 142 (mesma versao) — Neo City: base ROAD reconhecida de verdade:**
- A base ROAD nao e a area 0 (Driver Dome): o jogo entra numa area de cidade (1) com a cidade 6 (saindo de um capitulo) ou 0 (entrando no jogo). E o trafego via a posicao e trocava a cidade para Moon Palace (no console: "esta na cidade 1 (pela posicao; antes 6)"), entao nada da Neo City funcionava la.
- Agora o servidor marca a base na entrada da area (console: "Added player ... to area 1 (base ROAD da Neo City)"), nao troca mais a cidade pela posicao la dentro, nao poe trafego na base e trata a base como um Driver Dome: os 20 bots da Neo City aparecem estacionados (e andando, depois que a base tiver pontos conhecidos). Console: "Rua: Base ROAD (Neo City) - ...".
- Se voce sair para uma cidade de verdade (carro a mais de 700 m do centro do mapa), a marca sai sozinha.
- Corrigida a mensagem "Rua: Driver Dome - ..." do console (os numeros estavam fora de ordem).

**Atualizacao 141 (mesma versao) — Neo City: bots no lobby (base ROAD) e 3 crews:**
- Os 20 bots da Neo City agora ficam **no lobby da Neo City (base ROAD secret base)**, do mesmo jeito que o Driver Dome normal: uns estacionados, outros andando. O jogo trata a base como um Driver Dome (area 0) da cidade 6; o servidor ve isso e mostra so os bots da Neo City la (e nunca eles no Driver Dome normal).
- A base ROAD tem os pontos dela, salvos em `neo_dome_points.json` (o Driver Dome normal continua em `dome_points.json`). Na primeira vez os bots vao aparecendo conforme voce anda pela base; depois que a base tiver mais de 20 pontos conhecidos, parte deles passa a andar pela base.
- **Nas missoes nao aparecem mais bots parados/no trafego**: la so a sua party (que te segue e atira nos HUVs).
- **3 crews dos bots da Neo City**: ROADGuard, TimeWarp e MitronX (6 bots em cada; 2 sem crew). Ficam fora da escada de crews.

**Atualizacao 140 (mesma versao) — Neo City: 20 bots proprios e party ajudando nas missoes:**
- **20 bots novos que moram so na Neo City** (NeoRaven, CyberKai, Plasmatica, RailRita, OrosGhost, HuvSlayer, NeoBlitz, Voltix, TurretKill, Quantum, NeoNinja, ChronoX, Synthwave, NeoLuna, Overclock, Neutrino, ByteRider, Ciborgue, HexaCore, NeoFalcao), nivel 70 a 99. Nao aparecem nas outras cidades, no Driver Dome, no Rush nem no chat da rua.
- Eles ficam parados pela base da Neo City: os lugares sao pontos da rua por onde voce ja passou perto de onde apareceu (ate `NeoLobbyRadius` = 250 m). Ande um pouco pela base e eles vao aparecendo.
- Chame para a party como os outros bots: quem entra sai do lugar e passa a te seguir.
- **Nas missoes**, os bots da party vao atras dos HUVs e atiram de ate `NeoBotHitRange` (40 m), um tiro a cada `NeoBotHitSeconds` (1,2 s), dano `NeoBotDamage` (900) x nivel/80 x habilidade. O jogo soma o dano no HUV; quando a vida acaba ele e destruido.
- HUV destruido por um bot: voce ganha `NeoBotKillShare` (50%) da exp/mito dele, e o bot ganha exp. No fim do capitulo o console mostra quantos HUVs os bots destruiram.
- Config: `NeoBots`, `NeoBotsCount`, `NeoLobbyRadius`, `NeoBotsHelp`, `NeoBotHitRange`, `NeoBotHitSeconds`, `NeoBotDamage`, `NeoBotKillShare`.

**Atualizacao 139 (mesma versao) — Neo City: menos itens caindo dos HUVs:**
- O item do HUV era sorteado a cada aviso de "HUV destruido" (206), que chega varias vezes e tambem para HUVs que nao foram voce que destruiu: na pratica caia item em ~70% dos HUVs. Agora o sorteio e feito uma vez so, no golpe em que VOCE destroi o HUV.
- Chance: `NeoDropChance` 0.2 (20%) nos HUVs normais; chefe continua `NeoDropBossChance` 0.5.

**Atualizacao 138 (mesma versao) — Neo City: mito certo e tela CHAPTER CLEARED preenchida:**
- **Mito "resetando"**: ao destruir um HUV o servidor mandava a sua EXP total no lugar do MITO total, e o jogo trocava o mito da tela por esse numero. O mito de verdade (no servidor) nunca foi perdido: agora o pacote vai no formato certo (mito ganho, mito total, exp ganha) e a tela volta a mostrar o valor certo no proximo HUV destruido (ou ao entrar de novo no jogo).
- Fim de quest (3807): exp e mito estavam trocados de lugar; agora o jogo soma o mito certo.
- **Tela CHAPTER CLEARED**: agora mostra tempo, quests falhadas, vezes que o carro foi destruido, nota (S/A/B/C/D), seu nivel, nome, HUV/HUB/HUS destruidos, exp e mito ganhos no capitulo.
- Nota: comeca em S e cai 1 por quest falhada, 1 por vez que o carro foi destruido e 1 se passar de 30 min.
- Premio do capitulo: 1 item da Neo City do seu nivel (arma, armadura, Amplifier ou Fuse) aparece na coluna Reward e vai para o inventario. Config `NeoChapterBonusItem` (true).

**Atualizacao 137 (mesma versao) — Neo City: cada quadrado abre a sua missao:**
- **Sempre abria a Operation 3:** no 3801 o jogo le @0 como o capitulo (indice na tabela NC_quest: Chapter 1-4 = 0-3, Operation 1-3 = 4-6) e @12 como a linha de balanceamento por jogadores (0 = sozinho). O servidor mandava @0 = 6 (o numero da cidade) = Operation 3, e o capitulo em @12 (por isso, fora do quadrado 0, os inimigos nem vinham). Agora vai o capitulo escolhido em @0 e 0 em @12.
- Recompensa das quests pela tabela US (capitulo + quest).
- 3902 (tirar os HUVs de roteiro da etapa que acabou) agora tem resposta (3903).
- **Capitulo completo** (3802) agora tem resposta (3803): o jogo marca o capitulo como feito e mostra o fim.

**Atualizacao 136 (mesma versao) — teste dos capitulos da Neo City:**
- Comando de teste **!!neocap <n>**: na proxima entrada na Neo City o servidor manda o capitulo n (em vez do item da lista) para descobrir qual numero abre cada capitulo/operacao. `!!neocap` sem numero desliga.
- **!!neoquest <n>**: ao aceitar no "!", o servidor inicia a quest n da lista interna do jogo (o jogo sempre pede a 0). `!!neoquest` sem numero desliga.

**Atualizacao 135 (mesma versao) — pecas de Crew (party) voltam a contar:**
- Party Engine Speeder / Party Engine Skirt (Crew Maximum Speed) agora somam no Maximum Speed Increase do seu carro, e Party Crash Guard (Crew Durability) soma na durability. Antes o servidor so conhecia o "speedup" normal.
- **Velocidade das pecas em mph (AssistSpeedAsMph):** o numero da peca vale direto em mph no Assist Option, sem conversao (Party Engine Speeder C +3 = +3,0 mph, antes +1,9).
- Multiplicador escondido (exe): accel volta para 2,0x (speed 2,35x, dura 1,5x, boost 1,5x).

**Atualizacao 134 (mesma versao) — multiplicador escondido dos pontos (no DriftCity.exe):**
- No exe (0x4929b0) cada atributo vira "pontos efetivos" = (Total + extras) x taxa x 0.001, e e isso que a fisica do carro usa. **Cliente\DriftCity.exe** (copie para a pasta do jogo) ja vem com cada ponto valendo **2,35x** em speed, **2,0x** em accel e **1,5x** em durability e boost: nada aparece no Assist Option e o Total do painel nao muda. Ferramenta para mudar/voltar: `python3 Cliente/ferramentas/multiplicador_pontos.py <DriftCity.exe> 1.2 1.2 1.2 1.2` (speed dura accel boost; `1 1 1 1` = normal). Backup: DriftCity.exe.antes_multiplicador.
- SpeedBonusPerPoint (o +54,3 do Assist Option) voltou a 0.

**Atualizacao 133 (mesma versao) — velocidade maxima pelos pontos:**
- Tirado o bonus de % nos pontos (da 132). No lugar: **SpeedBonusPerPoint** (0,1 mph por ponto de speed) soma na "Maximum Speed" do carro, contando so os pontos acima de **SpeedBonusFromPoints** (0). Ex.: 542 de speed = +54 mph. Vai no campo "Maximum Speed Increase" (o mesmo das pecas de assist); o carro so chega se o motor/marchas deixarem.

**Atualizacao 132 (mesma versao) — Neo City sem inimigos na 2a vez + bonus de pontos:**
- **Missao sem inimigos:** o jogo so pede os HUVs (198) na primeira vez que a missao abre na sessao. O servidor guardava e reenviava, mas pela posicao na lista (3800); a mesma Operacao vinha por outro item (0 e 4) e ele nao achava. Agora guarda pela missao (cidade + quest do 3804).

**Atualizacao 131 (mesma versao) — Neo City: exp e mito dos HUVs:**
- **Exp/mito "infinitos" ao destruir HUV:** o golpe no HUV (205) que volta do servidor tem os campos de exp ganha, exp total e mito ganho, que o servidor preenche. Ele devolvia o pedido como veio e ali ficavam as posicoes do carro (numeros enormes/aleatorios). Agora o servidor so paga no golpe que destroi o HUV, uma vez por HUV: exp = nivel do HUV x NeoHuvExpPerLevel (10), mito = nivel x NeoHuvMitoPerLevel (8), chefe x NeoBossRewardMultiplier (5). Ex.: HUV nivel 60 = 600 exp e 480 mito.

**Atualizacao 130 (mesma versao) — Neo City: ajustes do combate:**
- Os HUVs e o chefe voltam sozinhos: o proprio jogo, depois de destruido, avisa (3904) e pede um novo (198). O servidor agora responde o 3904 (3903) e deixou de fazer o regen dele (NeoBossRegenCount/NeoHuvRegenCount = 0), que dobrava os HUVs.
- 3826 (carro destruido na Neo City) agora tem resposta (3827, a mensagem do jogo).

**Atualizacao 129 (mesma versao) — Neo City: HUVs de patrulha:**
- Os HUVs de patrulha (HMG, APS, RPG, MRL) nao apareciam. No jogo cada HUV e identificado por um numero e tem um "dono" (quem controla: patrulha, persegue e atira); quem dava os dois era o servidor. Na 124 todos iam com o mesmo numero (1) e sem dono. Agora cada HUV ganha um numero proprio e o dono e voce (NeoHuvOwnerIsPlayer). O console mostra o 1o HUV de cada grupo em hex para conferir.

**Atualizacao 128 (mesma versao) — Neo City: loja, itens dos HUVs e o chefe que volta:**
- **Loja (Cliente\Patch\patch.008 — copie para a pasta Patch do jogo):** todas as armas (Cannon, Railgun, Wave, Mine), armaduras (Light/Heavy), Amplifiers e Fuses da Neo City, N/S/H/U/R, agora estao a venda na loja da Neo City, com o preco da tabela do jogo (239 itens). Ferramenta: Cliente/ferramentas/loja_neo.py (backup em Patch\backup).
- **HUVs deixam cair itens:** ao destruir um HUV, chance de cair uma arma/armadura/Amplifier/Fuse do grau do seu nivel (NeoDropChance 12%, chefe NeoDropBossChance 50%), com a animacao "Get Item".
- **O chefe volta:** a Operacao 3 pede 5 ★HUV-T01 mas so aparecia 1. Pela tabela do jogo (HuvTable) o chefe volta 10 s depois de destruido, ate 10 vezes; os HUVs normais voltam em 30 s, 2 vezes. Agora o servidor faz isso. Config: NeoBossRegenCount/Seconds, NeoHuvRegenCount/Seconds.

**Atualizacao 127 (mesma versao) — Neo City sem delay:**
- Na missao o jogo manda varias vezes por segundo a mira das torres e das armas (556), a posicao do carro (555) e o estado da arma (209). Cada um era escrito no console como "unknown packet" e isso atrasava o servidor (delay e tiros falhando). Agora eles sao ignorados em silencio (sozinho nao precisam de resposta). Config: QuietSyncPackets.

**Atualizacao 126 (mesma versao) — Battle Zone: modo espectador, etapa 1:**
- O servidor agora registra no console ("Espectador: ...") o que o jogo manda quando voce pede para assistir uma corrida (OBS): na lista de salas ou com o botao OBS dentro da sala. E a base para montar o modo de assistir os bots.

**Atualizacao 125 (mesma versao) — Battle Zone: tabela final:**
- **Colocacoes bugadas no RESULT:** quem nao terminou a corrida (R) ia todo com a mesma colocacao (a ultima) e o jogo escrevia um em cima do outro: sobravam linhas vazias e so um "R" no fim. Agora cada um ganha a sua colocacao depois dos que terminaram (9o, 10o, ...) com a distancia ate a chegada ("To Goal"). A recompensa continua a mesma.

**Atualizacao 124 (mesma versao) — Neo City, etapa 7 (HUVs e chefe):**
- **Os HUVs nao apareciam (so os HUBs):** cada HUV pedido vem com um campo de "liberado" em 0 e o jogo so cria os que vem com valor > 0. Os HUBs vinham com 1; os HUVs (HMG, APS, RPG, MRL) e o chefe **★HUV-T01** da Operacao 3 vinham com 0. Agora o servidor libera todos. Config: NeoHuvSpawnAll.

**Atualizacao 123 (mesma versao) — Neo City, etapa 6 (combate):**
- **Torres:** agora quem controla as torres e voce (o jogo original dava o controle ao lider da party). Sem dono elas ficavam paradas, sem atirar e sem levar dano. Config: NeoTurretOwnerIsPlayer.
- **Golpes e HUVs destruidos:** o jogo manda o golpe no HUV (205), HUV destruido (206), mira (207), aggro (208) e explosao (3919) e espera o mesmo pacote de volta (o servidor repassa para a party). Agora o servidor devolve.
- Tudo aparece no console com "Neo City:".

**Atualizacao 122 (mesma versao) — Neo City, etapa 5 (inimigos):**
- **HUVs inimigos:** ao comecar a missao o jogo pede os HUVs (198) e esperava a resposta 199 para criar eles. Agora o servidor responde e os HUVs da missao aparecem.
- **Torres** (3905 -> 3906): as torres inimigas da Neo City tambem sao criadas.
- O console mostra "Neo City: HUVs inimigos pedidos" com a quantidade.

**Atualizacao 121 (mesma versao) — Neo City, etapa 4 (quests):**
- **"!" das quests:** quando o carro entra no ponto da quest o jogo manda 3815 e esperava a resposta 3818 (sem ela a conversa nao abria). Agora o servidor responde.
- **Aceitar quest** (3804 -> 3805), **quest completa** (3806 -> 3807, paga exp/mito/itens da tabela NC_quest: a Q2 "Familiar Faces" da as armas Plasma), **progresso da missao** (3819 -> 3820), **missao completa** (3821 -> 3822), **falhou** (3810 -> 3811) e **desistir** (3812 -> 3813).
- Tudo sai no console com "Neo City:" para a proxima etapa (HUVs inimigos, recompensa do capitulo).

**Atualizacao 120 (mesma versao) — Neo City, etapa 3:**
- Entrar na missao funciona (area 84 = Neo Oros).
- **EXIT:** o jogo manda 3824 e o servidor agora responde 3825 (voce sai da Neo City).
- **So a sua party:** na Neo City nao aparecem moradores, vendedores nem trafego; so os bots da sua party vao com voce.
- Ainda faltam (proximas etapas): quests e o "!", armas e HP dentro da missao, HUVs da Neo City. O console registra
  o pacote 3815 que o jogo manda la dentro.

**Atualizacao 120 (mesma versao) — Neo City, etapa 3:**
- Ja da para entrar na Neo City (area 84 = Neo Oros).
- **So a sua party entra:** dentro da Neo City (areas 84-87) nao aparecem moradores, vendedores, bots estacionados
  nem trafego; os bots da party continuam te seguindo. O servidor tambem nao troca mais a sua cidade "pela posicao"
  la dentro (antes achava que voce estava em Cras).
- **EXIT:** o jogo manda 3824 e o servidor agora responde 3825 (NeoChapterGiveUpAck) com o seu serial: o jogo sai da
  Neo City.
- Ainda faltam: o "!" das quests (o jogo manda 3815 sem parar; registrado no console), HP e armas dentro da Neo City,
  HUVs e recompensas.

**Atualizacao 119 (mesma versao):**
- **Copie o `Cliente\Init_us.agt` para `Data_US`.**
- **Pistas com IA no Battle Zone:** so 16 pistas (todas de Moon Palace) tinham o icone de IA; Koinonia, Cras e Oros
  nao tinham nenhuma, por isso essas cidades apareciam sem pista para correr com os bots. Agora as 93 pistas normais
  das 4 cidades tem o icone.
- **Neo City:** o 3721 levava o tempo no campo que o jogo usa como numero da cidade (6 = Neo Oros), entao ele nao
  carregava o mapa. Corrigido. Se em alguma pista os bots ficarem parados (a pista nao tem o caminho da IA gravado),
  avise qual. Os desafios que os bots abrem continuam nas 16 pistas originais. Ferramenta:
  `Cliente/ferramentas/pistas_ia.py`.

**Atualizacao 118 (mesma versao):**
- **HP da Neo City (barra "HP 0 / 0"):** o teste mostrou que o HP maximo e o campo 0 do pacote de atributos (o jogo
  enche a barra sozinho ate ele). Agora o servidor manda: durabilidade total x `NeoHpPerDurability` (10) + nivel x
  `NeoHpPerLevel` (40) + `NeoHpBase` (0). `NeoHpEnabled` liga/desliga.
- **Neo City, etapa 2:** depois do 3721 o jogo pede 3800 (comecar capitulo: cidade e capitulo); o servidor agora
  responde 3801 (NeoChapterStartAck).

**Atualizacao 117 (mesma versao) — Neo City, etapa 1:**
- "Enter City" na lista de capitulos da Neo City: o jogo manda 3720 (NeoCitySuggest) e esperava o servidor mandar
  3721 (NeoCityLoad); agora o servidor responde (sozinho, sem party para aceitar). `NeoCityEnter` liga/desliga.
- O console mostra mais bytes dos pacotes desconhecidos (96) e linhas "Neo City:", para as proximas etapas
  (capitulos, quests, HUVs e HP da Neo City).
- Teste da barra de HP da Neo City (0 / 0): `!!stat testeint` (campos do pacote de atributos) e `!!ext <a> <b>`
  (pacote 762 ExtendUpdate). O que mudar na barra diz qual campo e o HP.

**Atualizacao 116 (mesma versao) — Producao (Production Skill Book):**
- A janela de producao agora funciona (pacotes 3700-3710 achados no DriftCity.exe):
  1. `!!livro` lista os Skill Books; `!!livro <nome do item>` compra um por **50.000 mito** (`AlchemyBookPrice`).
  2. Clique duas vezes no Skill Book no inventario: a receita aprende e acende na janela.
  3. Junte os materiais e o kit que a receita pede (`!!kit 1|2|3 [qtd]`: Normal 1.200, Small 700, Large 2.000) e
     fabrique na janela. Chance e exp da tabela do jogo (45%, +100 exp no acerto / +10 na falha nas receitas Low...).
  4. A exp sobe o nivel de fabricacao (Low 0 / Mid 8.610 / High 66.420 / Highest 221.430), que libera as receitas
     de cima. `!!producao` mostra o nivel e as receitas.
- Opcoes: `AlchemyBookPrice`, `AlchemyKitPriceMultiplier`, `AlchemyChanceBonus`, `AlchemyExpMultiplier`. Dados em
  `res/AlchemyRecipes.json` (embutido; `Cliente/ferramentas/producao_receitas.py` refaz). Progresso em alchemy.json.
- Os livros ficam no comando `!!livro` (a lista do Part Shop vem de um arquivo do jogo que ainda nao sei editar).

**Atualizacao 115 (mesma versao):**
- **Animacao "Get Item" ao bater no HUV e no Rush:** quando cai um item ao bater no HUV (perseguicao) ou no boss/HUVs
  do Rush, o servidor manda o 741 com o item e o jogo mostra o "Get Item" com a voz (como no combo).
- **Bots no Rush:** os HUVs pequenos que o boss solta eram tratados como se fossem do tamanho do boss: os bots ficavam
  na distancia da beirada do boss e nunca encostavam neles. Agora so o boss usa essa distancia; nos pequenos os bots
  encostam e batem como num HUV normal.

**Atualizacao 114 (mesma versao):**
- **Combo (a cada 20):** o Booster Protector sai sempre; os extras saem cada um com 30% de chance: Mittron Crystal
  Powder, um Component H, Auto-Driver e 3 Damage Booster do seu grau (N ate o nivel 30, S 31-60, H 61+). Opcoes:
  `ComboPowderChance`, `ComboComponentChance`, `ComboAutoDriveItem`/`ComboAutoDriveChance`,
  `ComboDamageCount`/`ComboDamageChance`.
- **Components H:** clicando duas vezes num Component com 10 iguais, eles viram a peca H incompleta (Crude Speed /
  Accel. / Dura. / Boost Part; complete com Mittron Enhancer no Modify). Com menos de 10, o chat mostra quantos faltam.
  Quando o combo te da o 10o, eles viram a peca sozinhos.
- **Animacao "Get Item":** no combo de 20 o servidor manda o 741 (TrafficItemEventAck) com o item ganho; o jogo
  mostra o "Get Item" e toca a voz. `ComboGetItemAnimation` (true) liga/desliga.

**Atualizacao 113 (mesma versao):**
- **"Tentar de novo" nas entregas e missoes:** ao apertar Sim, o jogo manda 786 (QuestPosition) e espera a posicao
  do posto; o servidor nao respondia, entao o carro ficava onde estava. Agora responde com a posicao do ultimo posto
  que voce usou e o jogo te leva de volta para tentar de novo. O console mostra "QuestPosition (tentar de novo)".

**Atualizacao 112 (mesma versao):**
- **Combo (a cada 20):** alem do que ja dava, 60% de chance de ganhar 1 **Mittron Crystal Powder** (para as trocas do
  Item Exchange das cidades) e 60% de chance de ganhar 1 **Component H** (Speed, Accel., Dura. ou Booster; 10 do
  mesmo tipo viram uma peca H incompleta). Opcoes: `ComboPowderItem`, `ComboPowderChance`, `ComboComponentItems`,
  `ComboComponentChance`.

**Atualizacao 111 (mesma versao):**
- **Bot da party na Battle Zone (corrida sem voce):** quando a corrida acaba ele conta no chat da party a colocacao e
  o que ganhou, e voce (lider) leva 2/3 do premio dele (`PartyBotRaceLeaderShare` 0.6667). Ele some de onde estava,
  reaparece no mesmo lugar quando a corrida acaba e vem dirigindo ate voce (nao teleporta). Se voce mudou de area,
  ele aparece um pouco atras no seu caminho.
- **Progresso das licencas:** o servidor nao mandava progresso nenhum (tudo 0). Agora conta e mostra na janela:
  corridas da Battle Zone (tambem contra bots; `LicenseRaceCountsBoth` = cada corrida conta como individual e de
  time), pista de cada cidade, HUV (e por cidade, e em party), Rush, patrulha (e em party), entregas (medalhas e ouro
  de cada entrega), Undercity, missoes secundarias, leilao (anunciar/vender), upgrade de carro e tempo dirigindo na
  cidade. Quando as condicoes batem, a licenca chega na hora (aviso no chat). `!!licencas` mostra as mais perto.
  O progresso comeca do zero agora. Ainda nao contam: "Quest Completed" das cidades e principal, OMD/ROO, badges.
  Regras em `res/LicenseRules.json` (embutido no Rice.exe; `Cliente/ferramentas/licencas_regras.py` refaz).
- **Recompensa de nivel:** as 4 pecas agora sao as melhores que voce ja pode equipar no nivel (10 = Boxer, 20 =
  Spector, 30 = Riddle, 40 = Winner, 50 = Ghost, 60 = Hermes, 70 = Presto, 80 = Enigma, 90 = Gaia, 100 = Ouranos),
  e nao mais a ultima peca N/S/U. Os docks e enhancers seguem o grau que voce pode equipar: 10-30 N, 40-50 S,
  60-80 H, 90+ U (antes o nivel 80 ganhava docks U, que pedem 90).

**Atualizacao 110 (mesma versao):**
- **Copie o `Cliente\Init_us.agt` para `Data_US`** (de novo).
- Upgrade dos carros especiais (ST, BT, Xmas): o V1 deles tambem pedia nivel 90 na tabela de carros, e o jogo olha o
  nivel do V atual do carro antes de deixar upar. Agora o V1 pede 1 (comprar na loja continua no 90+, pelo servidor).

**Atualizacao 109 (mesma versao):**
- **Copie o `Cliente\Init_us.agt` para `Data_US`** (de novo).
- Upgrade dos carros especiais (ST, BT, Xmas) ganhos na gacha: o jogo tambem olha o "Req Level" do proprio carro
  (tabela VehicleDisplay, 90 nesses carros) para deixar usar o V2..V9 Car Upgrade. Agora e 1; o nivel de cada V vem
  do V (V2 10 ... V9 80), e o servidor continua cobrando nivel 90+ (`SpecialCarsMinLevel`) para COMPRAR esses carros.

**Atualizacao 108 (mesma versao):**
- **Copie o `Cliente\Init_us.agt` para `Data_US`** (de novo; ja inclui a tabela de IA da Atualizacao 105).
- Carros especiais (ST, BT, Xmas) ganhos na gacha/evento antes do nivel 90 agora podem ser upados: o V2..V9 deles
  pede o nivel normal do V (V2 10, V3 20, V4 30, V5 40, V6 50, V7 60, V8 75, V9 80). Antes o jogo pedia nivel 90 em
  todos os V e mostrava "Your current level is not high enough" (o aviso e do proprio jogo, por isso nada no log).
  Comprar na loja continua no 90 (o V1 nao mudou). Ferramenta: `Cliente/ferramentas/especiais_upgrade.py`.

**Atualizacao 107 (mesma versao):**
- **V9 no perfil dos bots (User Information):** a pasta `Rice\res` ao lado do Rice.exe estava com os .json originais
  de 2016 (VehicleList, VehicleUpgrade, ItemClient, UseItemClient, AssistClient, QuestClient). Por isso o servidor
  achava que a GSIX coop era V9 (na loja nova e V2), e os precos de carro e outras mudancas nao valiam. **Sempre copie
  os .json da pasta `Rice\res` do projeto para a pasta `res` ao lado do Rice.exe.** Ao abrir, o console avisa se a
  pasta estiver desatualizada.
- **Bot da party na Battle Zone:** quando ele aceita uma corrida (desafio no chat ou a sua sala), para de te seguir e
  fica correndo ate a corrida acabar, mesmo se voce sair da sala e voltar para a cidade. No fim ele volta e avisa no
  chat da party (com a colocacao, quando correu sem voce).
- **Progresso da party:** quando voce ganha exp, a mensagem de cada bot da party aparece (uma depois da outra). Antes
  saiam todas no mesmo instante e o jogo mostrava so uma.

**Atualizacao 106 (mesma versao):**
- Teste do V do carro dos bots no Battle Zone: ao entrar ou criar uma sala, o console mostra "Sala: seu UserInfo ... |
  CarAttr ..." (os dados do seu carro que o jogo manda), para descobrir onde vai o V e mandar o V certo dos bots.

**Atualizacao 105 (mesma versao) — nome certo dos bots no Battle Zone:**
- **Copie o `Cliente\Init_us.agt` para `Data_US`** (troque o antigo).
- O cliente conta as linhas da tabela de IA a partir de 0, e o servidor mandava o numero da linha a partir de 1: cada bot
  aparecia com o nome da linha de baixo (Brunizin.X aparecia como Joaopvbr; outro aparecia como "Miko"). Corrigido.
- Agora cada um dos 125 bots tem uma linha so dele na tabela de IA, com o nome igual ao do servidor (40 bots ganharam
  linha nova: CtrlAltPerde, OCaraDoLag, MonaSemSobra, PaoDeQueijo...). Antes varios dividiam a mesma linha e
  apareciam com o nome de outro bot ou de um piloto original (Yuha, Karlos...).
- Ao abrir, o servidor acerta sozinho o bots.json (console: "Bots: N bot(s) com a linha nova da tabela de IA").
- Ferramenta: `Cliente/ferramentas/nomes_ia.py` refaz a tabela se entrarem bots novos.

**Atualizacao 104 (mesma versao):**
- **Assist Option - Maximum Speed Increase:** as options com assist de velocidade maxima (ex.: Engine Speeder B,
  a_00032 = +2 km/h = +1,2 MPH) e os Convert "speedup" agora somam no campo certo do pacote de atributos (campo 12,
  km/h). O painel "Assist Option" do inventario passa a mostrar o valor (ex.: + 1.2 mph).
- Achado com `!!stat teste`: campos 8-11 = pontos extras no "Total" (speed, durability, accel, boost); campo 12 =
  Maximum Speed Increase. Booster Charging Speed, Speed Decrease After Crash e Boost Time ainda faltam (`!!stat testeint`).

**Atualizacao 103 (mesma versao):**
- **Leilao:** os anuncios dos bots que ja existiam agora tambem pegam o preco novo ao abrir o servidor: pecas/itens
  (preco da loja x1.5, nivel, +N, qualidade, Convert x2; cupom bem caro) e carros (preco da loja com o desconto de km e
  combustivel). Antes so os anuncios novos tinham o preco novo.
- **Teste do Assist Option (Option "Engine Speeder" = Maximum Speed +1,2 MPH):** `!!stat teste` coloca 1.0 a 19.0
  nos 19 campos depois dos totais do pacote de atributos; `!!stat testeint` coloca 1 a 19 (inteiros); `!!stat limpar`
  volta ao normal. Com o print do painel eu descubro qual campo e o "Maximum Speed Increase", "Booster Charging Speed",
  "Speed Decrease After Crash" e "Boost Time".

**Atualizacao 102 (mesma versao):**
- Corrigido erro de compilacao CS0136 no BotChatter.cs (variavel "line" repetida no presente do bot da crew).

**Atualizacao 101 (mesma versao) — Escada de crews vira mod:**
- **Padrao = modo classico:** cada crew com o seu requisito de nivel (Rookies, KoiFlow, NightRiders, MitoKings,
  CrasGhosts, OrosLegends, LendasDC), os bots recrutam no chat e voce entra se tiver o nivel. Criar crew volta a pedir
  nivel 20 (`CrewCreateLevel`). Na primeira vez que o servidor abre sem o mod, as crews dos bots voltam ao formato
  classico (bots que ja estao na sua crew continuam nela).
- **Mod Escada de crews:** tudo das Atualizacoes 95 a 100 ligado a escada (crews por cidade, mano a mano, chefes,
  Most Wanted, dupla, vagas, metade para o lider, amigo entra na crew, bot de nivel maior recusa) so funciona com o mod.
  Para ligar: `Menu.bat` > `8) Mods` (ver `Rice/res/mods/LEIA-ME_MODS.md`).
- **Continuam sempre:** moradores das cidades andando, grupos/HUV/Battle Zone dos bots, troca com option/dock, presente
  do bot da sua crew, crew na selecao de corredor, `!!upar <bot>`, bots da crew aceitam party, evolucao dos bots,
  precos da loja e do leilao.

**Atualizacao 100 (mesma versao):**
- **Troca com bots aceita option e dock:** um de cada tipo no carro do bot. Ele confere se tem nivel para usar e compara
  com o que ja tem (te devolve o antigo). Options e docks dos bots somam nos atributos deles.
- **Selecao de corredor:** a licenca mostra a crew atual de cada corredor.
- **Presente do bot da sua crew:** ele sai do lugar dele e vem dirigindo ate ficar 3 m na sua frente (`MeetDistance`),
  avisa no chat e manda o pedido de troca sozinho (nao precisa responder "sim"), so com o que achou para te dar:
  Mittron Enhancer, Seed e pecas ate o seu nivel. Quanto maior o nivel dele, mais e melhor. Ele fica parado ate a troca
  acabar. Se a janela de troca nao abrir sozinha, abra a troca com ele: os presentes ja estao la.
  (Todo bot que vem ate voce agora vem dirigindo, nao aparece na sua frente.)
- **Corrida em dupla:** a cada ~8 min (`CrewTeamCallMinutes`) um bot da sua crew chama no chat da crew para correrem
  juntos contra um membro da crew da vez (de preferencia da cidade em que voce esta). Responda "sim": sala com voce, ele e
  o rival. Se voce OU ele chegar na frente do rival, o rival entra na sua crew (conta na escada).
- **Vagas da crew:** 3 no comeco (`CrewSlotsStart`), +3 a cada chefe vencido (`CrewSlotsPerBoss`). So contam os bots que
  voce chama (`!!recrutar` ou amizade); os vencidos no mano a mano/dupla e seus outros corredores nao usam vaga.
- **Metade para o lider:** o aviso agora diz qual membro rendeu e quanto voce ganhou ("[Crew] EoPixNada? (corrida): +350 mito para voce").
- **Leilao de carros:** carros dos bots com o preco da loja (`AuctionCarShopPrice`, `AuctionCarPriceMultiplier` 1.0),
  +30% por V acima do V da loja, com o desconto de km rodados (ate 12%) e combustivel no tanque (ate 8%). Os anuncios
  que ja existiam sao recalculados ao abrir o servidor.

**Atualizacao 99 (mesma versao):**
- **Recrutar bot:** bot de nivel maior que o seu recusa (no `!!recrutar <bot>` e quando vira seu amigo). Bot de crew da
  escada (Rookies, MoonFlow...) nao vem pelo `!!recrutar`: so vencendo no mano a mano.
- **Progresso da escada por login (conta):** os slots da mesma conta dividem o progresso; cada conta tem o seu.
  O progresso antigo (por corredor) passa para a conta na primeira vez.
- **Metade para o lider (`CrewLeaderShare`):** quando um membro da crew ganha mito (bots: atividades, corridas de desafio,
  Rush, grupos de HUV/Battle Zone na cidade; seus outros corredores: premios), ele fica com o dele e o lider da crew
  (quem criou) ganha a metade disso. O lider recebe quando estiver jogando (aviso "[Crew] Lider: +X mito").
- **`!!upar <bot>`** (ou `!!<bot> upar`): usa um "V Car Upgrade" do seu inventario no carro de um bot da sua crew. Tem que
  ser o do proximo V: carro V1 so aceita o V2 Car Upgrade, V2 o V3, e assim por diante.

**Atualizacao 98 (mesma versao) — bots, etapa 4 (Most Wanted):**
- **Most Wanted (Blacklist do NFS Most Wanted):** 15 bots, todos com o carro do jogo mais parecido com o da Blacklist,
  no V9, nivel 85 (#15) a 99 (#1). So aparecem (rua, lobby, chat) depois que voce vence a LendasDC.
  - Moon Palace: #15 Sonny (Birdie RTI = Golf GTI), #14 Taz (Dreamer = Lexus IS), #13 Vic (Above = Supra), #12 Izzy (Nova = RX-8).
  - Koinonia: #11 Big Lou (Fondo = Lancer Evo VIII), #10 Baron (Lightning = Cayman), #9 Earl (Impact = Impreza), #8 Jewels (BULLITT = Mustang).
  - Cras: #7 Kaze (Arena = CLK), #6 Ming (Luna = Gallardo), #5 Webster (Indigo = Corvette), #4 JV (Viper).
  - Oros: #3 Ronnie (Cross = Aston Martin), #2 Bull (Aria = SL AMG), #1 Razor (Snake = Shelby GT500), o chefe.
- **Desafio:** igual a escada (perto 10 s, "sim", sala mano a mano). Cada membro vencido: o carro dele no V9, 200.000
  mito, 4 pecas H upadas (ou dock/option H) e ele entra na sua crew (doa em dobro). Sem membros: o Razor chama e a sala
  dele fica na Battle Zone. Razor vencido: o Snake V9, 10.000.000 mito, 8 pecas U upadas, 4 docks U e 4 options H (a
  melhor que existe) e, com ele na sua crew, **toda exp e mito de premio que voce ganhar vale em dobro** (vendas,
  leilao e reembolsos nao contam).
- `!!escada` mostra o progresso da Most Wanted.

**Atualizacao 97 (mesma versao):**
- **Amigo sem crew entra na sua crew:** adicionou de amigo um bot sem crew (e voce tem crew): ele entra na sua crew.
- **Bots da sua crew sempre aceitam party** (mesmo quando aparecem offline).
- **Fim do mano a mano:** 30 s depois do resultado (`DuelAutoLeaveSeconds`), quando voce volta para a sala, voce sai dela
  sozinho (volta para a lista de salas da Battle Zone) e o chat mostra o progresso da crew da vez ate o chefe.

**Atualizacao 96 (mesma versao):**
- **Escada de crews pede crew:** sem crew o membro da crew da vez nao corre; ele para e pede para voce criar uma crew.
  (A crew nao e mais criada sozinha.) Nivel para criar crew: 1 (`CrewCreateLevel`; era 20), custo continua 10.000 mito.
- **Criar crew:** os outros corredores da sua conta (slots) entram juntos como membros.

**Atualizacao 95 (mesma versao) — bots, etapa 3 (crews e mano a mano):**
- **Crews por cidade (feito uma vez ao abrir o servidor):** 10 membros cada, chefe = o de nivel mais alto.
  Moon Palace: Rookies (esquerda) e MoonFlow (direita; era a KoiFlow). Koinonia: MitoKings (sul) e KoiGhost (norte; era a
  CrasGhosts). Cras: CrasRacers (direita, nivel baixo de Cras) e CrasElite (esquerda, nivel alto). Oros: OrosLegends
  (em cima a direita) e LendasDC (Joaopvbr, Marsteh e Brunizin.X, pela cidade toda). A NightRiders acabou.
  Em cada cidade sao 20 de crew e 10 sem crew. Os membros saem do ponto da crew e andam no lado dela
  (`CrewRegions`: {x0, x1, y0, y1} em fracao do mapa; se algum lado sair trocado, troque ali).
- **Bots nao recrutam mais** (sem chamadas de "a crew X esta recrutando").
- **Escada (`CrewLadder`):** Rookies -> MoonFlow -> MitoKings -> KoiGhost -> CrasRacers -> CrasElite -> OrosLegends ->
  LendasDC. So a crew da vez aceita desafio. Fique perto (25 m) de um membro dela por 10 s: ele para e pergunta se
  quer correr mano a mano; responda "sim" em 1 minuto e ele abre a sala "<nome> - <crew>" na Battle Zone, com 2 lugares
  (fica 3 min). Ganhou: ele sai da crew dele e entra na sua (precisa ter crew; ver Atualizacao 96).
- **Chefe:** sem membros na crew, o chefe te manda mensagem e a sala dele "(CHEFE)" fica na Battle Zone ate voce vencer.
  Premio: o carro dele (no V dele), 50.000 mito (+50.000 a cada chefe vencido: 50k, 100k, 150k...), +1 andar na garagem
  (aparece quando entrar de novo) e ele entra na sua crew. Depois de LendasDC, a Most Wanted libera (etapa 4).
- **Doacoes:** os bots que voce venceu, na sua crew, doam mito ou uma peca a cada 12-18 min (mais nivel, mais premio).
- **`!!escada`** mostra a crew da vez e quantos faltam. Progresso salvo em `crew_ladder.json`.

**Atualizacao 94 (mesma versao):**
- **Moradores todos andando (`CityAllDriving`):** antes os bots de crew ficavam estacionados no ponto da crew (Koinonia,
  Cras e Oros tinham so ~10 de 30 andando). Agora todos andam; os de crew saem do ponto da crew.
- **Postos da Battle Zone:** o servidor gravava o lugar onde voce saiu da cidade anterior (errado). Agora grava a posicao
  que o jogo salva ao abrir a lista de salas da Battle Zone no posto (SaveCarPos + RegisterRoomObserver) e confere se o
  ponto fica numa rua. Os postos errados ficaram no `battle_posts.json` antigo; os novos vao para `battle_posts2.json`.
  Passe de novo pelo posto da Battle Zone de cada cidade (abrir a lista de salas ja basta).

**Atualizacao 93 (mesma versao):**
- **Chamada de HUV/Rush no lobby:** se ninguem entrou com voce em 1 minuto, quem chamou e quem respondeu dirigem ate a
  saida do lobby mais perto e somem (ficam fora 4-8 min). Vao para a cidade deles: no HUV, quando voce estiver nessa
  cidade eles aparecem andando juntos com a sirene; no Rush, ficam esperando o boss no lugar do Rush da cidade deles.
  Os bots que esperam o Rush numa cidade agora sao os moradores dela.
- **Bot vem ate voce:** so os da sua crew (a que voce criou ou a mesma em que voce esta). Sem crew, ninguem para em voce.

**Atualizacao 92 (mesma versao) — bots, etapa 2 (cidades):**
- **Moradores de cada cidade (`CityWholeCity`):** cada cidade tem 30 bots fixos (`CityBotsPerCity`), do nivel mais baixo
  para o mais alto: Moon Palace nivel 1-16 (V1-V2), Koinonia 15-30, Cras 30-50, Oros 50-99 (`CityLevelBands`). Feito
  uma vez ao abrir o servidor; quem estava fora da faixa da cidade dele passa para dentro dela e pega o carro do V do
  nivel novo (o console mostra quantos ficaram em cada cidade).
- **Pela cidade inteira:** os bots aparecem em ruas aleatorias da cidade, nao mais em volta de voce, e nao somem nem
  voltam para perto de voce quando voce se afasta: continuam andando pela cidade. Voce ve os que estao perto.
  Bot de crew sempre aparece no ponto da crew (`CrewSpots`, nome da crew -> {x, y}; sem = um ponto sorteado fixo).
- **Grupos:** a cada 1-2,5 min alguns bots da mesma cidade saem juntos (2 a 4; os de crew so com a propria crew):
  - **HUV:** andam mais rapido, com a sirene ligada (o servidor copia o estado do seu carro na primeira perseguicao de
    HUV que voce fizer; antes disso vao sem sirene).
  - **Passeio:** andam juntos pela cidade.
  - **Battle Zone:** vao dirigindo pelas ruas ate o posto da Battle Zone mais perto, param 3 s e somem; depois de 2-5 min
    saem pelo mesmo posto e voltam para o lugar de origem. Os postos sao aprendidos quando VOCE entra na Battle Zone
    pela rua de uma cidade (`battle_posts.json`); ate la os bots daquela cidade nao vao a Battle Zone.
    Da para colocar postos a mao em `CityBattlePosts`.

**Atualizacao 91 (mesma versao) — bots, etapa 1:**
- **Troca:** o bot fica parado de frente para voce ate a troca acabar (antes saia depois de 90 s). Feita ou cancelada,
  ele volta para o lugar de origem. Se veio de longe (nao estava na sua tela), some em vez de ficar parado na sua frente.
  Sair da area no meio da troca tambem fecha a troca.
- **Evolucao dos bots (`BotsFrozen`):** fora da sua party e da sua crew os bots nao evoluem mais (nao sobem de nivel, nao
  criam metas, nao compram peca/carro, nao pegam licenca). Os da sua crew sobem so de nivel, bem devagar
  (`BotCrewExpScale` 0.1). Os da sua party evoluem rapido: a cada 5 min (`PartyBotGrowMinutes`) ou quando voce ganha
  exp (no maximo 1x por minuto) sobem 1 nivel (ate o seu nivel +3), 50% de chance de peca melhor, trocam/compram o
  carro do V do nivel e avancam as licencas (x3). Eles contam no chat da party.
- **Bot vem ate voce (`BotApproach`):** de 3 a 7 min, um bot (mais vezes um da sua crew) dirige ate voce, para de frente
  e pede para trocar, correr mano a mano (abre sala na Battle Zone) ou ajudar (party de HUV/Rush/Undercity). Fala no
  chat local; o da sua crew tambem no chat da crew. Enquanto isso as outras chamadas param por 2 min. Voce tem
  1 minuto para responder (sim/bora); sem resposta ele volta para o lugar dele.
- **Chamadas no chat:** cada chamada vale 1 minuto (`BotOfferSeconds` 60) e a proxima vem 60-75 s depois.
- **Lobby (Driver Dome):** ate 50 bots (`DomeParkedBots`). Eles saem do lobby e ficam fora 1-5 min.
- **Battle Zone (`BattleAiOnlyBots`):** as vagas de IA sao completadas so com os nossos bots que ja tem linha na
  tabela de IA do jogo (83), com o carro e o visual deles. Os pilotos originais (Elio, Taxi Driver...) nao aparecem
  mais. Falta dar linha aos outros bots: a tabela fica num arquivo do jogo que precisa do PC ligado.
- **Se o seu server.json tiver** `BotOfferSeconds`, `BotOfferGapMax` ou `DomeParkedBots`, troque pelos valores novos.

**Atualizacao 90 (mesma versao):**
- **Categoria especial por tipo:** BT e CT sao os mais baratos (10.000.000-33.750.000), depois ST (33.750.000-57.500.000),
  MINI (57.500.000-81.250.000) e Xmas (81.250.000-105.000.000).
- **Carros de cupom** (Coupon Book: Panda, Tokio, Viper, Above, Fondo...): 30% mais baratos que a tabela.
  `Cliente/precos_carros.md` marca cada um com "(cupom, -30%)".
- **Cupons no leilao:** os bots as vezes anunciam um cupom de carro (`AuctionCouponChance` 4% de cada anuncio novo),
  bem caro: preco do carro na loja x3 (`AuctionCouponPriceFactor`). Cupom que voce anuncia vale o mesmo para os bots comprarem.
- **Copie `Cliente/Init.agt` para `Data` e `Cliente/Init_us.agt` para `Data_US`.**

**Atualizacao 89 (mesma versao):**
- **Precos dos carros da loja por V** (cliente e servidor): V1 1.000-6.000; V2 7.500-19.000; V3 25.500-38.000; V4 45.000-74.000;
  V5 99.000-180.000 (ACROBAT nao mexido); V6 250.000-340.000; V7 380.000-550.000; V8 700.000-1.200.000;
  V9 1.350.000-5.300.000; categoria especial (ST/BT/CT/MINI/Xmas) 10.000.000-105.000.000. Dentro de cada V o preco vai
  do mais simples ao mais raro/caro: pesa o carro real (Bugatti, Pagani, Ferrari... no topo; Kia, Smart, Damas embaixo)
  e os atributos. A venda do carro passa a ser metade do preco. Lista completa em `Cliente/precos_carros.md`
  (script: `Cliente/ferramentas/precos_loja.py`).
- **Ordem da loja:** os 11 carros que ficavam no fim da lista, depois das categorias especiais (PT Cruiser, Jupiter,
  Solo, CR2, Deluxe, Seven, Vega, Zorro, Leo Patrol TW/USA, BONITO), agora ficam no V deles. `res/ShopCars.json`
  refeito com o V que a loja mostra.
- **Corrigido (erro da Atualizacao 84):** a lista antiga da loja estava errada e a 84 subia para o V9 carros que a loja
  mostra em outro V (NSR05 V6, BULLITT V2...). A compra volta a dar o V que a loja mostra. Ao entrar no jogo, uma vez
  por corredor, os carros que a 84 subiu voltam ao V certo (console: "Garagem: NOME V9 -> V6"; marca em `garage_v_fix.txt`).
- **Leilao mais caro:** itens = preco da loja x1.5 (`AuctionItemPriceMultiplier`), +100% para peca de nivel 100
  (`AuctionLevelBonus`), +40% por +N (`AuctionUpgradeBonus`), +5% por ponto de qualidade (`AuctionQualityBonus`),
  Convert x2 (`AuctionConvertedMultiplier`); carros = preco novo da loja x1.2 (`AuctionCarPriceMultiplier`).
- **Venda no Part Shop:** o extra por qualidade (+50 por ponto) e por up (+100 por nivel) voltou; sem os +250.
- **Copie `Cliente/Init.agt` para `Data` e `Cliente/Init_us.agt` para `Data_US`.**

**Atualizacao 88 (mesma versao):**
- **Venda no Part Shop volta ao preco normal do jogo:** `SellBonus` 250 -> 0, `SellQualityMito` 50 -> 0, `SellUpgradeMito` 100 -> 0. Continuam os precos fixos que voce pediu (`SellPriceOverride`: Mittron Enhancer N/S/H 350/500/800, Seed N/S/H 200/500/750).

**Atualizacao 87 (mesma versao):**
- **Sons de motor trocados** (direto em `Data_US\audio\engine` do PC; backup em `_backup_sons_20261029_2245`): BULLITT -> som do HYKAN; Dominator (e ST/BT) -> Tyrant; HYKAN (e ST/BT) -> PLUCK; PLUCK (e ST/BT) -> 366C; Pandora (e ST/BT) -> RAGE; Mento (e ST/BT/MINI) -> Dragoon. `sons_motor_doadores.lst` atualizado.

**Atualizacao 86 (mesma versao):**
- **Gasolina:** `FuelEfficiencyMultiplier` 45 -> 15 (gasta cerca de 3x mais). Quanto menor o numero, mais gasta. Se ficar demais, suba (ex.: 25).

**Atualizacao 85 (mesma versao):**
- Corredor novo: o minimapa voltava a repetir "-7000" sem parar depois do tutorial. A licenca do seu carro (Rookie) so era enviada ao entrar no jogo, e o corredor novo entra direto no tutorial. Agora ela e enviada ao terminar o tutorial, e tambem ao entrar com qualquer corredor que ja saiu do carro de treino.

**Atualizacao 84 (mesma versao):**
- Carro comprado na loja agora vem no V que a loja mostra. Antes o servidor usava o V "de fabrica" do carro, e 56 carros da loja ficavam diferentes (os carros novos aparecem no V9 da loja, mas vinham V1 a V8; ex.: NSR05 vinha V6). Preco e nivel exigido tambem seguem o V da loja. O comando !carro segue a mesma regra.
- Ao entrar no jogo, carros da garagem que ainda estao no V "de fabrica" (nunca upados) sobem para o V da loja. O console mostra "Garagem: NOME Vx -> Vy".

**Atualizacao 83 (mesma versao):**
- **Crash comprando muitos carros seguidos na loja:** o jogo cria um modelo de carro a cada compra e so libera ao sair
  da loja. Depois de ~45 compras seguidas acabou o espaco ("don't have Model" no GameLog) e o jogo fechou.
  - Agora a loja aceita no maximo 15 compras por visita (`ShopMaxBuysPerVisit`); depois disso avisa para sair e voltar.

**Atualizacao 82 (mesma versao):**
- **Som do motor dos carros novos:** o jogo toca `Data_US\audio\engine\idle<id>.wav` e `mid<id>.wav` (id do carro) e
  139 carros (os MINI, ST/BT e os novos 314-442, ex.: Luna MINI, 387SR ST, Dragoon, Prometheus) nao tinham arquivo.
  Foram criados no seu cliente copiando o som de um carro parecido (mesma familia, ou mesmo fabricante e atributos
  parecidos). A lista "carro:doador" esta em `Cliente/sons_motor_doadores.lst`.

**Atualizacao 81 (mesma versao):**
- **Venda no Part Shop (preco final):** N Mittron Enhancer 350, S 500, H 800; Normal Seed Parts 200, Super 500, Hyper
  750 (`SellPriceOverride`; sem o +250). O preco que o jogo mostra tambem mudou: copie `Cliente/Patch/patch.009`.
- **Pecas de desempenho** (Speed/Accel/Crash/Boost) vendem pelo preco normal do jogo, sem o +250 e sem bonus de qualidade/up.

**Atualizacao 80 (mesma versao):**
- **GameLog "XiUIMiniMap::DrawVehicleNameNonTeam idx error -7000":** o minimapa desenha o icone da licenca de cada
  carro que voce ve e le a 1a licenca montada (Rookie = 7000). Nos bots ela chegava 0 (0 - 7000 = -7000).
  - Agora o servidor manda as licencas montadas (806) assim que o bot aparece, alem de quando o jogo pede, e o 1o
    espaco nunca vai vazio (sem licenca de corrida = Rookie).
  - O seu carro: o jogo le a sua 1a licenca de um espaco que so o pacote 816 preenche, e ele so ia quando voce abria a
    janela de licencas. Agora vai ao entrar no jogo (so o corredor novo no tutorial, nivel 1, nao recebe; antes o perfil upado de um banco
    novo tambem ficava sem).
  - Tambem para voce: sem nada montado, o servidor manda Rookie no 1o espaco e manda o 806 com o seu serial (os HUVs
    da sua perseguicao usam o seu serial no jogo).

**Atualizacao 79 (mesma versao):**
- **Pacote de nivel:** 50.000 mito x (nivel / 10): nivel 10 = 50.000, 20 = 100.000, 30 = 150.000... (`LevelRewardMitoStep`).
- **Convert no Dock:** o dock tambem aceita Convert no Part Shop (2 opcoes, igual a Option Part). As opcoes de
  Speed/Accel/Dura/Boost do dock somam no carro.
  - A opcao da fileira dos docks de nivel (Turbo Start / Drift Booster) passou para o terceiro espaco de opcao, para nao
    brigar com o Convert.
- **Leilao:** option e dock dos bots podem vir com Convert (30%, `AuctionConvertedChance`), mais caros. Ao comprar, os
  pontos sorteados da option/dock ficam (o anuncio so passa as opcoes).
- **Bots:** progressao mais lenta (`BotActivityScale` 0.6 -> 0.4) e nas corridas entre bots so os 3 primeiros ganham
  (3x, 2x, 1x); quem perde nao ganha nada. Mais sorte na corrida: o melhor carro tambem perde as vezes.

**Atualizacao 78 (mesma versao):**
- **% de XP:** a barra comecava em 80%+ depois de subir de nivel. O jogo calcula (xp atual - inicio do nivel) / (fim -
  inicio) e o servidor mandava o "inicio" errado. Agora comeca em 0% em cada nivel.
- **Dano dos bots (Rush e HUV):** dano normal do carro: durabilidade do carro + pecas + nivel x 61-100% (a mesma faixa
  do jogo), sem reducao. `BotRealDamage`, `BotHitScale` (multiplicador).
- **Batida no HUV da cidade:** 35% de chance (no maximo 1 a cada 3 s) de cair Booster Protector F ou D, Speed/Accel/Dura
  Enhancer ou uma peca do seu nivel (com qualidade). 30% desses viram Mittron Enhancer ou Seed Part: N sempre, S a
  partir do nivel da 1a peca S (31), H a partir da 1a H (60). No Rush tambem: 30% dos drops viram Mittron/Seed.
  (Nao existe "Boost Enhancer" no jogo.) `HuvHitDropChance`, `HuvHitDropSeconds`, `HuvDropMittronChance`.
- **Lista de entregas:** exp e mito na lista agora sao os que voce ganha de verdade (ouro / prata / bronze / sem
  medalha, com o bonus da cidade) e o nome mostra a dificuldade, ex.: "Pizza Delivery [Facil]".
  - **Arquivos do cliente:** `Cliente/Patch/patch.003` -> Patch\ e `Cliente/Data_US/World.agt` -> Data_US\ (guarde os
    originais). O valor da lista nao conta o bonus da party nem o `DeliveryRewardMultiplier`.
- **Itens de uso:** o servidor respondia com tempo 0, por isso o Booster Protector nao protegia. Agora manda o valor e
  o tempo do item. Funcionam: Booster Protector F/D, Speed/Accel/Dura Enhancer (somam no carro pelo tempo), Mittron Fuel
  (enche o tanque), Fuel Additive, Damage Power Up, kits de reparo/restauracao, Traffic/HUV Kit, Bacchus, Pulse Shield e
  outros. Se algum nao fizer efeito, mande o log ("ItemUse: ...").
- **Venda no Part Shop:** +250 mito por item; +50 por ponto de qualidade e +100 por nivel de up (`SellBonus`,
  `SellQualityMito`, `SellUpgradeMito`). O jogo pode mostrar o preco antigo, mas voce recebe o novo.
- **Pacote de nivel (10, 20...):** 50.000 mito (N) e 10 Mittron Enhancers do grau do pacote (N; S; o U da H).
- **Gasolina:** `FuelEfficiencyMultiplier` 68 -> 45 (gasta mais). O tanque e a capacidade do carro.
- **Prometheus** agora e V3 na loja (atributos e preco do V3, nivel 20). Arquivos do cliente: `Cliente/Init_us.agt`
  -> Data_US\ e `Cliente/Init.agt` -> Data\.
- **Aerofolio nos bots:** os bots usavam numeros de aerofolio de outros modelos (nao aparecia). Agora usam os que
  servem em qualquer carro (TB, GT, Mitron... SPORTS) ou o do proprio modelo. `BotSpoilerChance` 0.7.
- **Option Parts:**
  - Convert no Part Shop (coloque a option no lugar da peca): sorteia 2 opcoes entre Speed/Accel/Dura/Boost +N,
    velocidade maxima (mph) e tempo de booster, mais fortes no grau S/H e com a Formula.
  - Nao sobem de nivel no jogo: o leilao nao anuncia mais option/dock "+N".
  - Pontos variam de 0 a +6 sobre o padrao ao ganhar/comprar (`OptionQualityRange`).

**Atualizacao 77 (mesma versao):**
- **Eventos do jogo:** liga/desliga pelo server.json, em `"Events"` (nome: 1 liga, 0 desliga). Exemplo de Natal:
  `"Events": {"Christmas": 1, "SnowEffectOn": 1, "ChristmasTree": 1, "ChristmasChase": 1}`
  - Nomes: Christmas (roupa de Papai Noel), ChristmasTree, ChristmasChase, SnowEffectOn (neve), Halloween, Valentine
    (ja vem ligado), ValentainChocolate, GoodHarvest, WaterMelon, IndependenceDay, Event3Year, GWFestival, LuckyBag,
    Hongbao, HongbaoRush, FreeOBGacha, PartyPatrol, 2xPartyBonus, NewChar2Exp, Quest, TeamPoint, ModifyBetter (0-7).
  - E so o aviso para o jogo: decoracao, roupa, neve e textos sao do proprio cliente. Missoes, HUVs ou premios de
    evento que dependem do servidor podem nao funcionar ainda.
  - Precisa reentrar no jogo depois de mudar.

**Atualizacao 76 (mesma versao):**
- **Tela cheia:** os .bat (Start_AutoLogin / Start_Admin) mandavam `/ForceWindow` para o jogo, que forca a janela,
  sempre que nao achavam "tela cheia" salva. Agora tem `set TELACHEIA=1` no comeco do .bat: abre sem `/ForceWindow`.
  - Use junto com o Option.agt com `FullScreen = 1`. Para voltar a abrir em janela: `set TELACHEIA=0`.
  - Lembre de copiar os .bat novos para a pasta do DriftCity.exe.

**Atualizacao 75 (mesma versao):**
- **Docks: cada letra +6 pontos** sobre a anterior (`DockPointStep` = 6), com a mesma variacao de antes por grau.
  - N: A 1-3, B 7-9, C 13-15 ... H 43-45
  - S: A 3-7, B 9-13 ... J 57-61
  - H: A 6-12, B 12-18 ... O 90-96
  - U: A 10-18, B 16-24 ... S 118-126
  - Os docks que voce ja tem sao sorteados de novo ao entrar (ficaram fora da faixa nova).
  - `DockPointStep: 0` volta para as faixas da Atualizacao 72.

**Atualizacao 74 (mesma versao):**
- **HUV "Chase Successful" sem recompensa:** o servidor lia o resultado do HUV no lugar errado do pacote (vinha
  65879 em vez de 1 = pego), entao todo HUV contava como fugido. Agora le no lugar certo (@0xe).
  - Tambem: se o jogo diz que pegou e o servidor nao reconhecia o numero do HUV (ou a perseguicao nao estava
    registrada), conta o HUV como pego e paga.
  - O log mostra cada "ChaseEnd: ..." com os dados que o jogo mandou. Se acontecer de novo, mande o log.

**Atualizacao 73 (mesma versao):**
- **Coupon Book (carros de cupom):** agora conta e guarda.
  - O jogo conta sozinho: Battle Zone, entregas, perseguicoes, combos, tempo dirigindo, drifts, Skill Pass, pulos,
    contramao (Reckless) e Undercity. O servidor guarda os numeros em `coupons.json` e devolve ao entrar no jogo
    (antes zerava a cada entrada).
  - FondoX (GameMode 10): conta cada Rush Time que voce faz.
  - Meta cumprida: marque, abra e pegue o cupom no livro. O cupom (ex.: PANDA Coupon) vai para o inventario.
  - Na loja de carros, o carro de cupom e comprado gastando o cupom (e o preco normal em mito).
  - Duel (Real Match) e Liner (Line Ball) ainda nao contam: esses modos nao existem no offline.
  - Desligar: `CouponsEnabled: false`.
- **Rush:** os bots batem 40% mais fraco (`RushBotBossHit` 0.0015 no boss, `RushBotHuvHit` 0.018 nos HUVs que ele solta).

**Atualizacao 72 (mesma versao):**
- **Docks (Docking Parts):** antes o dock guardava um bonus em % (por isso aparecia um numero alto) e so somava se
  tivesse peca do mesmo tipo no quadrado. Com pecas pequenas o % dava 0.
  - Agora cada dock tem **pontos proprios**, sorteados pela letra, que somam direto no tipo dele (Speed, Accel, Crash
    ou Boost), igual as pecas. Mais facil sair perto do minimo.
  - Faixas (padrao), da letra A ate a ultima:
    - N (A-H): A 1-3 ... H 6-8
    - S (A-J): A 3-7 ... J 14-18
    - H (A-O): A 6-12 ... O 24-30
    - U (A-S): A 10-18 ... S 36-44
  - Os docks que voce ja tem sao sorteados de novo ao entrar no jogo (o log mostra "Dock: ... ajustado").
  - Ajuste no server.json: `DockPointMin`, `DockPointMax`, `DockPointSpread` (N, S, H, U). `DockPointRoll: false`
    volta ao sistema antigo de %.
  - Todo dock novo (Combine, nivel, leilao...) ja vem com os pontos sorteados.
- **Pacote de nivel (10, 20, 30...):** as 4 pecas agora sorteiam a qualidade (igual a loja) e os 4 docks sorteiam os
  pontos. O chat mostra os pontos de cada um.
- **Fileira de docks de nivel:** os 4 docks do pacote (Speed, Accel, Crash, Booster) na mesma fileira do carro
  (de cima ou de baixo) dao **+50 em cada atributo** e a opcao sorteada no pacote: **Turbo Start** ou **Drift Booster**.
  - Duas fileiras completas = +100 em cada.
  - A opcao aparece no dock de velocidade da fileira (teste se o Turbo Start / Drift Booster funciona na corrida).
  - Os docks letra A que voce ja tem viram docks de nivel na primeira entrada (o log mostra "Docks de nivel: ...").
  - Fica salvo em `level_docks.json` (ao lado do Rice.exe). Config: `DockRowEnabled`, `DockRowBonus`.

**Atualizacao 71 (mesma versao):**
- **Vehicle Performance Info (inventario):** o servidor mandava o id do servidor = 0 ao entrar no jogo. Com 0 o cliente
  nem calcula Maximum Speed, Time to reach, Crash Damage e Boost Time (por isso ficavam 0).
  - Agora manda `LoadCharServerId` (padrao 2016) e a hora de inicio do servidor.
  - Assist Option: so mostra valor se o carro/pecas tiverem assist (carro V1 comum normalmente nao tem, entao 0 e normal).

**Atualizacao 70 (mesma versao):**
- **Licencas:** a janela mostra em "Amount of LicenseReward" a soma das licencas que voce tem (Rookie = 3.000).
- **Qualidade das pecas de desempenho:** ao comprar na Part Shop ou ganhar (Rush, entregas, RENT), cada peca sorteia de 0
  ate N pontos a mais sobre o ponto padrao (`PartQualityRange`): N ate +4, S ate +10, H ate +16, U ate +28.
  - Exemplo: Small (speed 1) vem com 1 a 5 pontos. Mais facil vir perto do padrao.
  - O chat mostra a qualidade (fraca, media, boa, excelente) e os pontos.
  - Os pontos somam no carro como os do Modify. Upada no Rush: os pontos do up somam em cima.
- **Teste da performance do carro no inventario:** `!!stat <0-18> <valor>` muda um dos 19 campos depois dos totais do
  pacote de atributos e manda de novo. `!!stat limpar` zera.
  - Use inteiro ou numero com `f`, ex.: `!!stat 0 150f`.
  - Serve para descobrir qual campo faz aparecer mph, tempo, crash, boost e as Assist Options.

**Atualizacao 69 (mesma versao):**
- **Patrulha em party:** o jogo nao mostrava os pontos dela (falta um pacote que ainda nao foi descoberto).
  - Por enquanto o posto de patrulha oferece a patrulha normal (`PartyPatrolOffer` = false). Os pontos aparecem e os bots
    da party ajudam igual.
  - Para voltar a oferecer a em party: `PartyPatrolOffer: true`.
- **Rush:** o log mostra quando voce sai da zona do Rush ("Rush: Joao saiu da zona do Rush (antes de comecar)").

**Atualizacao 68 (mesma versao):**
- **Garagem dos bots:** cada bot guarda os carros que tem.
  - Comprou um carro: ele vai para a garagem e o bot passa a usar.
  - Trocou de carro no lobby: so entre os que ele tem.
  - Todos comecam com o carro atual.
- **Pedir para o bot da party trocar de carro** (so funciona com bot que esta na sua party):
  - `!!<nome do bot>`: ele fala no chat da party o que tem na garagem, por exemplo
    "Minha garagem: 1) Metro V1 (usando), 2) Ironia V1".
  - `!!<nome do bot> <numero>` (ou parte do nome do carro): ele troca para esse carro. Ele some por um instante e aparece
    com o carro novo.

**Atualizacao 67 (mesma versao):**
- **Nomes dos bots novos com no maximo 12 letras:** o jogo corta o nome em 12 letras e ai nao achava o bot para adicionar de
  amigo. Os compridos foram encurtados, e o `bots.json` e corrigido sozinho na proxima vez que o servidor abrir:
  - CtrlAltPerde, PãoDeQueijo, Avestruzinho, MonaSemSobra, CapiRaivosa, JulioDoGrau, PadeiroTempl, SaboFrancis.
  - BetoFurada, CaniveteGalo, MestreCuscuz, GeladeiraInv, LiquidLigado, PinguimFrio, ChineloGanso, LadeiraBaixo.
  - ErreiMoleque, ProcrastiPro, ReiDoRespawn, SacoPancada, VoltaPLobby.
- **Adicionar amigo / party pelo nome:** se o nome vier cortado, o servidor acha o bot pelo comeco do nome (quando so um bate).

**Atualizacao 66 (mesma versao):**
- **Party no lobby:** cada bot da party faz as coisas dele no proprio tempo. Varios podem ir a lojas ao mesmo tempo, sem um
  esperar o outro voltar.

**Atualizacao 65 (mesma versao):**
- **42 bots novos** (`res/bots.default.json`), metade no nivel 1 e metade acima do 50. Eles entram sozinhos no `bots.json`
  na proxima vez que o servidor abrir (os antigos continuam).
  - Nivel 1: CtrlAltDerrota, EoPixNada?, AvestruzQueTeSeduz, CapivaraRaivosa, JulioCesarDoGrau, Padeiro Templário, Sabonete Francis, Betoneira Furada, MestreDoCuscuz, Cavalo Frito, GeladeiraInversa, ChineloDeGanso, AimBotless, ZeroMiss, Ladeira Abaixo, ErreiFuiMoleque, VoltaProLobby, Previsivel, GasteiOFlash, FoiSemQuerer, InstaKillMe.
  - Nivel 51+: CabeloDeWiFi (92), PãoDeQueijoOP (84), MorreuSemLer (59), InimigoDoFim (78), MonALisaSemSobrancelha (84), DeuBora (57), Canivete Comgalo (77), Discosauro (60), CebolaToxica (86), LiquidificadorLigado (99), PinguimDeGeladeira (84), Piada Infame (86), OCaraDoLag (87), FreeKill (94), PesoMorto (51), ProcrastinadorProfissional (70), MestreDoRespawn (89), SacoDePancada (94), BeijaOChao (69), NemViQuemFoi (63), ChoraNoChat (85).
  - Carro, cor, personalidade e foco (corrida ou bater) sao sorteados, e o carro e escolhido da loja pelo V de cada um.
  - Nomes com mais de 18 letras podem aparecer cortados em algumas telas do jogo.

**Atualizacao 64 (mesma versao):**
- **Bots so evoluem participando:** nivel, mito e licenca sobem so quando o bot participa de algo (missao, entrega, HUV,
  Rush, corrida...). Nada passivo por minuto (`BotLicensePassive` = false).
  - Bot sem meta participa de algo com chance de 20% por minuto (`BotActivityChance`).
  - Bot com meta, 50% (`BotGoalActivityChance`).
  - Tudo um pouco mais devagar que antes (`BotActivityScale` = 0,6).
- **Rush:**
  - Na primeira vez que voce entra num Rush, o servidor grava o lugar daquela cidade (`rush_spots.json`).
  - Daí em diante, os corredores daquele Rush (uma turma que troca a cada 10 min) ficam estacionados em roda la.
  - Quando voce chega e entra, eles esperam a contagem parados. Quando o boss aparece, saem dirigindo de la, te seguem e
    vao no boss.
  - No fim, quem participou e nao e da sua party tambem ganha exp, mito e licenca.
- **Corrida (desafio no chat):** os bots vao ate a porta da Battle Zone e esperam 1 minuto (`BotRaceOpenSeconds`) voce
  entrar. Se voce nao entrar, correm sozinhos:
  - Somem da porta por 2 a 5 min (`BotRaceMinSeconds` / `BotRaceMaxSeconds`).
  - Depois voltam, cada um ganha pela colocacao (habilidade x velocidade + aceleracao do carro, com sorte), e o vencedor
    comemora.
- **Carro pelo gosto do bot:** cada bot tem um foco, "corrida" ou "bater" (competitivo = corrida, calado = bater, os outros
  meio a meio).
  - Para cada carro da loja: durabilidade maior que velocidade e aceleracao = bom para bater (HUV/Rush). Velocidade ou
    aceleracao maior = bom para corrida. Empate = serve para os dois.
  - O bot escolhe, no V dele, um dos 3 melhores para o foco dele.
  - Exemplo no V1: corrida = Metro, Birdie RTI, April, Drifter...; bater = Ironia, Caliber, Goat...
  - Vale para todos os V. Quem esta com carro que nao combina troca quando o servidor abrir.

**Atualizacao 63 (mesma versao):**
- **Bot que cumpriu a meta, no lobby:** ele vai dirigindo ate a loja (concessionaria para carro, Part Shop para peca),
  para 3 s na porta (`DomeShopWaitSeconds`), compra e fica mais 1,5 s (`BotGoalShopStay`).
  - Depois volta dirigindo: se for da sua party, ate o lugar dele na formacao atras de voce; se estava estacionado, para a
    vaga dele.
  - Ele confirma que conseguiu ("Pronto, ja tenho o ...!"): na party/crew pelo chat, e no balaozinho para quem esta perto
    (nao vai para o chat geral).
  - Se ele nao estiver no lobby, compra na hora, como antes.

**Atualizacao 62 (mesma versao):**
- **Bots da party so evoluem com voce:** enquanto estao na sua party, nao ganham exp, mito nem licenca sozinhos.
  - So ganham quando voce faz algo: missao, entrega, HUV, Rush, Undercity...
  - Levam `PartyBotExpShare` (50%) da sua exp e do seu mito e +`BotLicenseActivity` (2) pontos em cada licenca montada.
- **Tabela no chat da party** a cada vez que ganham (`PartyBotExpTable`), por exemplo:
  `+60 exp | nivel 12: 310/480 exp p/ o 13 | Licencas: Class E 4/6 p/ Class D`
- **Exp para subir:** do nivel N para o N+1 = max(100, N x 40) (`BotExpPerLevel`). Agora a exp que sobra passa para o
  proximo nivel.

**Atualizacao 61 (mesma versao):**
- **Sem convites enquanto voce esta ocupado:** em missao, entrega, quest, HUV, Rush ou Undercity, os bots nao chamam para
  party, corrida, leilao ou crew (`BotOffersWhenBusy` = false).
  - Os bots da party tambem nao sugerem nada. Depois que voce termina, esperam o tempo normal para sugerir.
- **Party no lobby:**
  - Um bot so sai para uma loja quando voce esta parado ha 15 s (`PartyDomeErrandIdle`).
  - Na volta ele vem dirigindo ate o lugar dele na formacao atras de voce (1o e 2o atras e de lado, 3o e 4o atras deles),
    sem pular e sem ficar em cima de outro.
  - Quando voce sai andando, ele volta a te seguir normal.
- **Transito e bots nas outras cidades:** o servidor nem sempre sabia que voce tinha trocado de cidade. Continuava usando as
  ruas de Moon Palace, entao a cidade ficava vazia e os bots apareciam longe no mapa.
  - Agora ele descobre a cidade pela sua posicao (a cidade com uma rua perto de voce) e monta o transito e os bots de novo.
  - No log aparece "Trafego: <nome> esta na cidade N (pela posicao)".

**Atualizacao 60 (mesma versao):**
- **Pecas do Rush (e das entregas):** agora so saem as pecas normais da loja (as `i_..._000001`), do seu nivel para baixo.
  - Antes saiam as de aluguel (`r_...`), que nao tem nome no jogo e tinham nivel 1 (dava para equipar qualquer uma).
  - Com 10% de chance (`RushRentChance`) vem uma peca RENT (as com o selo branco).
- **Rush, bots:**
  - Dividem o monte: a cada 3 s, quem esta no HUV mais cheio vai para o mais vazio.
  - Poder do boss: so os bots a ate 25 m de voce (`RushPowerRadius`) pulam junto quando voce e jogado. Os outros continuam.
  - Os bots tambem levam o poder sozinhos quando estao perto do boss (em media a cada 25 s, `RushBotPowerEvery`).
  - Nao ficam mais parados esperando voce andar.
- **Rush, dano:** o dano de cada golpe dos bots aparece como no HUV (`RushShowBotDamage`).
- **Transito:** ja esta ligado para Moon Palace, Koinonia, Cras e Oros. Ele comeca quando voce entra na cidade
  (log "Trafego koinonia: ... carros").

**Atualizacao 59 (mesma versao):**
- **Missao travada:** se o jogo trava no meio de uma missao e voce volta, ela ficava presa e o servidor derrubava o jogo
  ("quest abandon mismatch") quando voce tentava abandonar.
  - Agora o servidor acha a missao no banco, marca como abandonada ou falha e libera.
  - Concluir, receber a recompensa e passar nos pontos tambem voltam a funcionar (sem recompensa em dobro).
- **Licencas dos bots:** todos os bots voltam uma vez para o Rookie e comecam do zero.
  - Sem a aceleracao de 3x (`BotLicenseCatchUp` = 1).
- **Neon:** todo bot acima do nivel 10 (`BotNeonLevel`) tem neon. Do nivel 10 para baixo, nao.

**Atualizacao 58 (mesma versao):**
- **Licencas dos bots evoluem** (`BotLicenses`):
  - Todo bot comeca so com o Rookie. A cada minuto ele ganha progresso no que esta montado. Ao bater a meta, passa
    para a proxima: Rookie -> Class E -> D -> C -> B -> A -> AA -> AAA -> No.9.
    - O nivel do bot limita ate onde ele vai.
    - A meta e `BotLicenseNeed` x grade da proxima / 1000: Class E 6 pontos, Class C 20, No.9 140.
  - **Nivel 16 a 30:** tira o nivel 1 (a mais baixa) de uma licenca que combina com ele e vai upando quando bate a meta.
    - Competitivo: Team/Ind. Battle ou HUV.
    - Comerciante: Auction, Tuning ou Delivery.
    - Calado: Undercity, Patrol ou HUV.
    - Os outros: HUV, Delivery, Moon Palace, Undercity ou Patrol.
    - Exemplos: HUV Hunter Grade C -> B -> A -> Master; Moon Palace Grade C -> B...
  - **Nivel 31+:** ganha um terceiro espaco e escolhe qualquer licenca. De vez em quando troca por outra (`BotLicenseSwapChance`).
  - **Velocidade:** bot muito abaixo do que o nivel permite aprende 3x mais rapido. Online, 1,5x. Bot com mais habilidade
    tambem vai mais rapido. Ajuste geral: `BotLicenseSpeed`.
  - O que ele tem montado muda o que ele faz bem, como antes: corrida (velocidade), HUV (dano), Undercity, bonus de entrega.
  - Na sua party ou crew, ele avisa no chat quando tira ou sobe uma licenca.
  - Marsteh, Brunizin.X e Joaopvbr continuam com as licencas deles.

**Atualizacao 57 (mesma versao):**
- **Configuracao de video salva:** o `Start_AutoLogin.bat` e o `Start_Admin.bat` guardam o `Data\Init\goption.ini`
  em `goption_salvo.ini` quando o jogo fecha, e colocam ele de volta antes de abrir.
  - Se voce salvou tela cheia, o jogo abre sem o `/ForceWindow`.
  - Antes, o .bat sempre abria em janela 1024x768 e matava o jogo aberto (taskkill), entao o jogo nao gravava a config.
    Agora, se o jogo ainda estiver aberto, o .bat pede para fechar pelo menu.
  - Copie os dois .bat de novo para a pasta do `DriftCity.exe`.

**Atualizacao 56 (mesma versao):**
- **Joaopvbr:** igual ao Brunizin.X e ao Marsteh, so aparece na rua de Oros junto com eles, e so as vezes.
- **Login:**
  - A conta **admin** (com o perfil upado RiceAdmin) sempre existe. Entre com o usuario `admin` (`Start_Admin.bat`).
  - **Outro nome:** o servidor cria a conta na hora, vazia, como um jogador que acabou de se cadastrar
    (`AutoCreateAccounts`). Os corredores que voce criar ficam salvos nela.
  - Os outros corredores da mesma conta viram bots (amigos que entram na party), como antes.
  - Nome em branco = conta `jogador` (`NormalAccount`). O `Start_AutoLogin.bat` agora digita "jogador".
    Troque no .bat se quiser outra conta.

**Atualizacao 55 (mesma versao):**
- **Part Shop:** N Mittron Enhancer 1.000 mito e S 2.000 mito. Copie `Cliente/Patch/patch.009` para a pasta `Patch` do jogo.
- **Combo da party:** nao da mais recompensa, so conta (`PartyComboRewards` = false).
- **Party no lobby:** a cada ~3 minutos (`PartyDomeErrandMinutes`) um bot da party (um por vez) vai a uma loja do
  Driver Dome, faz as coisas dele (compra peca, troca carro, olha o leilao...), fica um pouco na porta e volta a te
  seguir. Ele avisa no chat da party quando sai e quando volta.
- **Carros dos bots:** usam so os carros que a loja mostra em cada V (`res/ShopCars.json`, da ordem da loja). No V1:
  Laputa, Metro, Birdie RTI, Drifter, Kicker, Condol, Advance, Ironia, Caliber, April e Goat.
  - Antes eles pegavam Allied, AXIS, ECO, CANTATA e START, que a loja mostra como V9.
  - Quem estava com um desses abaixo do V9 troca na proxima vez que o servidor abrir.
- **Missoes com pontos (instantaneas, patrulha):** os bots vao dirigindo pelas ruas ate cada ponto (caminho pelas faixas do
  transito), com a velocidade e a aceleracao do carro deles. Nao somem nem pulam mais (`MissionBotTeleportSeconds` = 0,
  `MissionBotRoads`). Se um ponto nao tiver rua por perto, ele ainda aparece la depois de um instante.
- **Crew:** os bots da sua crew contam no chat da crew o que estao fazendo (`CrewBotsTell`).
- **Cidade:**
  - Sem bots estacionados na rua, so andando (`CityParkBots` = false).
  - Brunizin.X e Marsteh so aparecem na rua de Oros, e so as vezes (35%, `CitySellersChance`). No lobby nao aparecem mais.

**Atualizacao 54 (mesma versao):**
- **Entregas:** a dificuldade agora e dividida dentro de cada posto. Cada posto tem 6 entregas:
  - as 3 comuns, na ordem da tabela: facil, normal e dificil;
  - a EX: especial;
  - Pascoa (coelho) e colheita: evento.
- **Cras:** so o 1o posto (00043) tem facil. Nos postos 00044 a 00046 as 3 comuns sao normal, normal e dificil.
- **Oros:** so tem dificil e especial (mais os eventos).
  - Time Attack da Olivia (postos 00007 e 00009): especial.
  - As 5 do posto 00008: dificil.

**Atualizacao 53 (mesma versao):**
- **Recompensas das entregas por dificuldade** (`DeliveryTierRewards`). Com ouro:

  | Dificuldade | Exp | Mito | Peca |
  |---|---|---|---|
  | Facil | 50 | 1.000 | N fraca |
  | Normal | 75 | 2.500 | N ou S do seu nivel |
  | Dificil | 85 | 3.000 | N, S ou H do seu nivel |
  | Especial | 100 | 5.000 | N, S ou H; 50% de vir upada (+1 a +9, com Convert) |
  | Evento (coelho da Pascoa, colheita) | 80 | 5.500 | caixa misteriosa: peca N/S/H que pode vir upada |

- **Medalha:**
  - Ouro: 100%.
  - Prata: 2/3, e a peca vem um grau abaixo. Na especial/evento, 20% de vir upada.
  - Bronze: 1/3, e a peca e a do comeco da linha (o menor nivel daquele grau).
  - Sem medalha: 1/3, sem peca.
- **Cidade:** cada cidade seguinte da +2% (`DeliveryCityBonus`): Moon Palace 0%, Koinonia +2%, Cras +4%, Oros +6%.
- **Qual entrega e de qual dificuldade:**
  - Em cada cidade: 1o posto = facil, 2o = normal, 3o e 4o = dificil.
  - As "EX" e as Time Attack da Olivia = especial.
  - Pascoa e colheita = evento.
  - Em Oros, as normais = dificil.
  - Para trocar uma entrega: `DeliveryTierOverride`, por exemplo `{"a_00001": "normal"}`.

**Atualizacao 52 (mesma versao):**
- **Entregas:** os bots da party so te seguem; voce pega os pontos (`PartyBotsHelpDeliveries` = false).
  Nas patrulhas eles continuam ajudando.
- **Upgrade de V do carro:** os itens "V2 Car Upgrade" ate "V9 Car Upgrade" estao na Part Shop, por um pouco mais
  (x1,2) que o preco medio dos carros daquele V: 36.000, 84.000, 168.000, 288.000, 456.000, 660.000, 900.000 e
  1.200.000 mito.
  - Duplo clique no item: o carro que voce esta usando sobe de V(N-1) para VN, se o modelo tiver esse V e voce tiver o
    nivel (`CarTierLevels`). O item so e gasto quando da certo.
  - Tabela de itens do cliente: copie `Cliente/Patch/patch.009` de novo para a pasta `Patch` do jogo.

**Atualizacao 51 (mesma versao):**
- **Loja de carros:** Laputa, Metro, Birdie RTI e Drifter voltaram e agora sao os 4 primeiros da lista.
  Copie o `Cliente/Init_us.agt` (e o `Init.agt`) para o jogo.
- **Part Shop:** N, S e H Mittron Enhancer a venda (100 / 300 / 3.000 mito).
  - Isso fica na tabela de itens do `Patch/patch.009`. Copie `Cliente/Patch/patch.009` para a pasta `Patch` do jogo.
- **Combo:** 2 de exp por marco (20, 40, 60...), `ComboRewardExp` = 2. O `ComboExpPerCombo` antigo nao vale mais.
- **Ofertas dos bots no chat** (party, corrida, leilao, crew, ajuda): uma de cada vez, com 1 a 3 minutos entre elas
  (`BotOfferGapMin` / `BotOfferGapMax`). Cada uma vale 3 minutos para responder (`BotOfferSeconds`).
- **O que o bot faz sozinho** (comprou peca, trocou de carro, subiu de nivel, comprou no leilao...) nao aparece mais no chat.
  So aparece se ele estiver na sua party, no chat da party.
- **Rush Time:**
  - O seu golpe agora acha o HUV certo e sempre conta. Se o jogo mandar dano 0, vale `RushPlayerMinDamage`.
    O log mostra os 30 primeiros golpes ("Rush: seu golpe ...").
  - Cada golpe seu pode dar uma peca do seu nivel para baixo (no maximo uma a cada 1,5 s, `RushHitDropSeconds`).
    Com 30% de chance (`RushHitUppedChance`) ela vem upada (+1 a +9) e com Convert.
  - Golpe de bot = fracao da vida do HUV vezes a forca do carro dele (atributos e habilidade): boss 0,25%
    (`RushBotBossHit`) e HUVs soltos 3% (`RushBotHuvHit`). Eles nao derretem mais o boss nem explodem os HUVs na hora.
  - Quando um HUV cai, os bots largam dele e voltam a te seguir ate voce chegar nos que faltam. O bot que ja estiver a
    40 m de um (`RushBotNearHuv`) vai direto. Cada bot pega um HUV diferente.
  - Poder do boss: os bots copiam o seu pulo (altura e estado do carro). Depois ficam parados junto com voce
    enquanto voce estiver parado.
- **Battle Zone:** o bot que entra numa sala (sua ou desafio no chat) dirige ate a porta da Battle Zone no Driver Dome e
  espera la. Na largada ele some (esta correndo) e no resultado aparece de novo na porta.
- **Login:**
  - **Conta normal:** o AutoLogin entra na conta normal (`NormalAccount`, "jogador"), que nao tem o perfil upado.
    Na primeira vez, os seus corredores da conta admin passam para ela; o upado fica na admin.
  - **Conta admin:** digite `admin` como usuario (o `Start_Admin.bat` faz isso) ou deixe `AdminLogin` = true.

**Atualizacao 50 (mesma versao):**
- **Combo:** a exp caiu para 1 por combo (`ComboExpPerCombo` = 1): 20 exp a cada marco de 20 combos, em vez de 100.
  O mito e os itens (protetor, Mittron Enhancer) continuam iguais.
  - No `server.json` que ja existe, troque `"ComboExpPerCombo": 5` para `1`, porque o valor do arquivo vale mais que o padrao.

**Atualizacao 49 (mesma versao):**
- **Battle Zone - carros dos bots:** os bots agora correm com a cor, o tint e os visuais (neon, decal, aerofolio, placa...) do
  proprio perfil. Nao vem mais branco e pelado.
- **Modo time:** durante a corrida, o time vermelho fica com pintura vermelha (`BattleTeamRedColor`) e o azul com azul
  (`BattleTeamBlueColor`). Ao sair da corrida (resultado), cada carro volta para a cor original.
  - Os assentos pares sao do vermelho e os impares do azul. Se aparecer invertido, me avise.
- **Dificuldade da IA:** `BattleAiDifficulty` passou para 0.6 e `BattleAiMaxLevel` para 65.
- **Bot ajudando bot:** quando um bot pede ajuda, as vezes outro bot topa ("Eu te ajudo!"), com chance `BotHelpByBotChance`.
  - Os dois andam juntos pela cidade e a meta anda uns 70% mais rapido.
  - No fim, quem tinha a meta agradece o ajudante, e o ajudante ganha um pouco de mito.
- **Voce tem o que o bot quer:** se o bot com meta esta na sua party e voce tem a peca que ele procura, ele avisa no chat da
  party e pede para trocar.
  - Ao confirmar a troca com o que ele precisava (a peca, um carro, ou o mito que faltava), ele agradece muito.
  - Ele te da uma recompensa maior: `BotTradeRewardShare` = 25% do valor, ate `BotTradeRewardMax` = 500.000 mito, mais exp.
  - Depois sai da party.
  - O mito que voce da numa troca agora vai de verdade para o bot.

**Atualizacao 48 (mesma versao):**
- **Bot pedindo ajuda:** quando um bot ganha uma meta (falta mito, nivel ou peca), ele chama alguem para a party
  (`BotHelpAskChance`). Responda "sim" e chega o convite.
  - Na party, cada missao, delivery, patrulha, HUV, Rush, Undercity ou corrida que voce concluir faz a meta dele andar.
    Ele conta o progresso no chat da party.
  - Quando consegue, ele agradece, te recompensa e sai da party. A recompensa e 15% do valor da meta em mito
    (`BotHelpRewardShare`, maximo `BotHelpRewardMax`), mais exp. Depois ele vai comprar.
- **Missao do HUV:**
  - Nenhum HUV pego: missao falhada, sem recompensa.
  - Parte dos HUVs (k de n): meio sucesso, com k/n da recompensa total.
  - Todos os HUVs (inclusive quando era um so): missao concluida, com 100%.
  - Bonus da party: +5% por membro (`HuvPartyBonusEach`), no maximo +20% (`HuvPartyBonusMax`, 4 membros).
    As licencas dos bots continuam somando.
  - A recompensa sai no fim da perseguicao.

**Atualizacao 47 (mesma versao):**
- **Poder do boss no Rush:** quando o poder do boss joga o seu carro para cima, o jogo manda 549 JumpVehicle.
  Agora os bots tambem pulam: ficam 1,6 s no ar, subindo ate 4 m (`RushBotJumpAir`/`Height`), caem, ficam
  parados 3 s (`RushBotJumpRest`) e voltam. O poder que joga obstaculos na rua nao avisa o servidor, entao esse
  continua sem afetar os bots.
- **HUVs que o boss solta:** a party se divide entre eles (cada bot fica com o menos perseguido). O dano agora vai no
  HUV em que o bot bateu (antes ia sempre no primeiro vivo). Derrubou um, os bots vao atras dos outros.
- **Batida no boss:** o bot fica devagar 2 s (`RushBotHitSlowSeconds`) e depois volta com tudo para alcancar.

**Atualizacao 46 (mesma versao):**
- **Lojas do lobby:** os bots nao somem mais nelas. Param na porta, fazem a compra e ficam parados 5 a 15 s
  (`DomeShopStayMin`/`Max`); depois arrancam e saem fazendo a curva.
- **Saidas para a cidade:** so ai o bot some. Depois do tempo fora (`DomeExitStayMin`/`Max`), entra pela saida outro
  bot que estava fora do lobby, e eles vao se alternando.
- **Economia dos bots:** cada bot tem mito (o inicial depende do nivel) e exp proprios.
  - Para comprar precisa de nivel e mito. A peca do Part Shop sai pelo preco da tabela, o upgrade custa
    20.000 + 1.500 x nivel, o carro da concessionaria sai pelo preco do V e o leilao pelo preco do anuncio.
  - Se faltar mito, nivel ou peca, o bot ganha uma meta. A cada minuto ele faz uma missao, delivery, HUV, Rush,
    corrida ou patrulha: ganha mito e exp e sobe de nivel. O progresso nao aparece no chat.
  - Quando chega na meta, vai a loja (ou ao leilao) e compra. Meta de "peca": depois de alguns minutos de missoes,
    ele acha uma peca melhor.
  - O bot que vende no leilao recebe o mito da venda.
- **Chat local:** vai ate 150 m (`BotLocalChatRange`).

**Atualizacao 45 (mesma versao):**
- **V9 no nivel 80**, e nao mais 85: `CarTierLevels`, o Init_us.agt/Init.agt e as tabelas do servidor. Copie o
  Cliente/Init_us.agt.
- **Lojas do lobby:**
  - O bot freia e para na porta. Espera 3 s (`DomeShopWaitSeconds`), faz o que foi fazer e entra (some).
  - Ao sair, aparece na porta de frente para o meio do lobby (antes voltava 1 m de re e virado).
  - Bot estacionado que vai a uma loja volta para a mesma vaga depois.
- **O que eles fazem em cada loja:**
  - **Part Shop:** compra uma peca melhor (com nivel para ela) ou upa a peca dele.
  - **Garagem:** troca de carro (outro do V dele).
  - **Concessionaria:** compra um carro do V dele.
  - **Battle Zone:** abre uma sala de desafio de corrida.
  - **Leilao:** da uma olhada.
- **Compra no leilao:** quando o bot diz que vai comprar, ele vai ate o leilao do lobby, espera 3 s, compra e volta
  para o lugar dele. Fora do lobby, compra na hora.
- **Saidas do lobby:** as 4 saidas que voce passou (`DomeExits`). Os bots as vezes saem por elas (`DomeExitChance`),
  ficam 30 a 120 s fora e voltam por uma delas.
- **Chat dos bots:**
  - Ate 60 m de voce (`BotLocalChatRange`): chat local, com balaozinho.
  - Mais longe ou em outro mapa: chat geral.
- **Correcao:** os bots andando no lobby se acumulavam cada vez que voce entrava (16, 22, 33, 45...). Agora o lobby
  comeca do zero a cada entrada.

**Atualizacao 44 (mesma versao):**
- **Lojas no Driver Dome:** o mapa do jogo (parkinglot_a.nif) marca a porta de cada loja: leilao (0, -129),
  concessionaria (-13, 117), Battle Zone (108, 60), garagem (-90, -45), Part Shop (-89, 48) e Crew Center (95, -55).
  Os bots que andam no lobby as vezes decidem ir a uma delas (`DomeShopChance`) e seguem o seu caminho ate a porta.
  La eles entram e somem por 20 a 90 s (`DomeShopStayMin`/`Max`), depois saem e voltam a andar. As posicoes ficam em
  `DomeShops` no server.json, caso precisem de ajuste.

**Atualizacao 43 (mesma versao):**
- **Cores dos bots:** o jogo guarda as cores em azul-verde-vermelho (BGR). As cores de neon, adesivo, vidro e pintura
  dos bots agora sao convertidas; antes o amarelo do Joaopvbr virava azul claro e o vermelho do Brunizin.X virava
  azul. O `!!visualbot` tambem converte o vidro e a pintura.
- **V do carro dos bots:** o carro de um bot nunca aparece abaixo do V dele (um carro V7 aparece no minimo V7). Bot
  sem nivel para o V do carro que ganhou volta a um carro do V do nivel dele.
- **Driver Dome:** os bots andam por mais lugares. Eles mudam de rota as vezes, nao repetem os ultimos 60 pontos e
  pulam ate 40 m entre pontos. O servidor agora aprende ate 4000 pontos por onde voce anda (`DomeLearnLimit`), entao
  quanto mais voce andar, maior o caminho deles.
- **Formacao da party:** parado, cada membro fica no lugar dele na fila, sem ir para o lado nem subir em outro, e so
  volta a andar quando voce se afasta 8 m dele (`PartyHoldRadius`).

**Atualizacao 42 (mesma versao):**
- **Troca:** quando a janela de troca fecha (concluida ou cancelada), o bot sai da sua frente e volta para onde
  estava, parado ou andando. Nas trocas entre bots, quem foi ate o outro tambem volta ao lugar de antes.

**Atualizacao 41 (mesma versao):**
- **Chat reiniciado a cada 15 minutos** (`ChatResetMinutes`; 0 desliga): o jogo nao tem comando para o servidor apagar o
  chat. Entao o servidor manda 60 linhas vazias (`ChatResetLines`), que empurram as falas antigas para fora da janela,
  e depois a dica "Digite !!help...".

**Atualizacao 40 (mesma versao):**
- **Rush:** o bot pula para 5 m atras do boss (`RushBotBehind`). Para bater, ele anda ate 0,5 m da beirada
  (`RushBotEdgeGap`) e bate nas bordas, sem entrar no boss. A beirada fica a 4,5 m do centro (`RushBossRadius`).

**Atualizacao 39 (mesma versao):**
- **Discussao na party:** voce parado 3 minutos (`PartyArgueSeconds`) com 2 ou 3 de Joaopvbr, Marsteh e Brunizin.X
  na party, e eles discutem. Sao 10 falas no chat local (balaozinho em cima do carro), com 2 a 8 s entre elas.
  - Quem comeca e sorteado. Marsteh manda "vai chupar um prego" para um ou para os dois. Joaopvbr manda "vai cagar"
    so para o Marsteh. Brunizin.X manda "vai cagar" para os dois ou para o Marsteh.
  - Sao 10 conversas diferentes. Marsteh se acha o melhor, Brunizin.X o esperto e Joaopvbr e o estressado.
  - So discutem uma vez enquanto voce esta parado; voce anda e para de novo e eles podem discutir outra vez.
- **Sugestoes da party:** quando voce nao esta fazendo nada (nem HUV, Rush, Undercity ou missao), um membro sugere
  algo no chat da party: corrida, HUV, Rush, Undercity, entrega, patrulha, leilao, a peca que colocou ou uma peca
  que queria. A espera entre uma sugestao e outra e de 5 minutos (`PartySuggestSeconds`).
- **Elogios:** a party elogia quando voce conclui missao, entrega, patrulha, HUV, Rush, Undercity ou corrida (ate o 3o
  lugar). Marsteh, Brunizin.X e Joaopvbr respondem cada um do seu jeito.
- `"PartyTalk": false` desliga tudo isso.

**Atualizacao 38 (mesma versao):**
- **Visual fixo dos bots** (`Look` no bots.json):
  - Joaopvbr: pintura (30,30,30). Vidro S15, neon S15 e Dragoon Decal em (251,216,0). Placa amarela "GOLD".
  - Brunizin.X: aerofolio GT wing, aero do Mento MINI (Type C), chama vermelha e rodas HYKAN. Vidro (255,1,1),
    carro preto, neon vermelho.
  - Marsteh: rodas Dragoon e aerofolio PLUCK. Vidro (1,1,1), neon branco.
  - O `!!visualbot` agora usa o numero certo de cada item (o da tabela do jogo, e nao o fim do nome). Ele tambem
    leva as cores do neon e do adesivo, o texto da placa e o vidro.
- **Rush:** os bots andam 5 m atras do boss (`RushBotBehind`), nao no meio dele. Cada bot so pula depois de 60 s sem
  bater ou de 60 s a mais de 100 m do boss (`RushBotJumpSeconds` / `RushBotJumpDistance`).
- **Party:** os membros nunca ficam a menos de 3 m de voce (`PartyKeepAway`). Parados, nao te empurram.
- **Loja:** Laputa, Metro, Birdie RTI (80) e Drifter, os carros iniciais do assistente, sairam da loja (tabela
  VehicleColorGroups). Copie o Cliente/Init_us.agt.
- **Combo:**
  - A cada 20 combos: o protetor de nitro continua, mais 50 mito (sem multiplicar) e 5 exp por combo (100 exp).
  - Mittron Enhancer: N a partir do combo 100, S a partir do 200 e H a partir do 300.
  - A partir do 400 vai o H DELUXE (`ComboEnhancerUItem`), porque o jogo nao tem enhancer U.
- **Driver Dome (lobby):** 40% dos bots andam pelo caminho que voce fez (`DomeDrivingShare`), ate 80 mph; os outros
  ficam parados.
- **Trocas:** quando a troca com um bot abre, ele aparece parado de frente para voce. Bot com bot ja fazia isso,
  agora tambem no lobby.

**Atualizacao 37 (mesma versao):**
- **Visual personalizado nos bots:** `!!visualbot <nome>` passa para o bot o seu carro atual: modelo, grau, pintura e
  as pecas visuais equipadas do drift shop (neon, placa, adesivo, para-choque, entrada de ar, aero, chama, rodas e
  aerofolio). Fica salvo no bots.json e nao e trocado quando o servidor reinicia, nem nas trocas e no leilao. Vale
  tambem para Joaopvbr, Marsteh e Brunizin.X. `!!visualbot <nome> limpar` desfaz.
  Exemplo: monte o visual numa Dragoon sua e digite `!!visualbot Joaopvbr`.

**Atualizacao 36 (mesma versao):**
- **Joaopvbr:** bot fixo novo, nivel 99, com a Dragoon V9 e as pecas no maximo (Ouranos/Tartaros/Kronos/Chaos Sorched,
  2 de cada). Fica sempre online e anda pela cidade. Bate mais forte no HUV e no Rush (`Skill` 1.6). Na corrida usa a
  linha 218 da tabela de IA do jogo, que antes era a Erina, agora com o nome dele e mais rapida. Copie o
  Cliente/Init_us.agt para Data_US.
- **Crew LendasDC:** requisito nivel 90. Membros: Joaopvbr (lider), Marsteh e Brunizin.X. Marsteh e Brunizin.X agora
  tem as pecas no maximo equipadas.
- **Party no HUV e no Rush:** a party te segue ate voce chegar a 120 m do HUV ou do boss (`PartyChaseJoinDistance`),
  sem pular nele logo no comeco. Na perseguicao ela esta mais rapida (`PartyChaseSpeed` 1.25, `PartyChaseCatchUp` 1.8).
  O Rush leva 12 corredores (`RushBots`) e os bots tiram menos vida do boss (`RushBotDamage` 0.4).
- **Tutorial:** um corredor que ainda esta com o carro de treino nao entra em party nem faz troca, seja voce ou o
  outro corredor.
- **Interacoes dos bots no chat:**
  - **Party:** um bot chama para HUV, Rush ou Undercity, outros bots aceitam e andam juntos. Responda bora, sim, s,
    yes, y... para receber o convite de party.
  - **Leilao:** o desconto e de verdade. Os bots se interessam entre 5 s e 40 s e compram a partir de 55 s, mas so se a
    peca ou o carro for melhor e eles tiverem nivel. Escreva quero, eu compro... e o bot te espera 3 minutos.
  - **Corrida:** a sala "Desafio de <bot>" aparece na Battle Zone com um lugar livre. Voce entra e a corrida larga em
    5 s. Aceitar vale +100 exp e +1000 mito.
  - **Trocas entre bots:** quem propos vai ate o outro e para de frente. So aceitam peca, carro ou visual melhor, com nivel.
  - **Crew:** os membros anunciam vagas com o requisito da crew. Bots que cumprem o requisito entram; voce entra
    respondendo sim.
- **Modo economico:** desligado por padrao. Para ligar, use `"EconomiaEnergia": true`.
- **Som dos carros novos:** o jogo nao tem som de motor para os carros depois do 313, como a Dragoon, nem para varios
  MINI. O Cliente/ferramentas/som_motor_carros_novos.bat copia o som de um carro parecido para cada um deles (a
  Dragoon usa o do Koenigsegg CCR, o Leopard). Coloque o .bat em Data_US\audio\engine e de dois cliques. Ele so
  cria arquivos novos.

**Atualizacao 35 (mesma versao):**
- **Tutorial:** o tempo voltou ao original do jogo (2:10, 2:15 e 2:20). Use o patch.000 original; o zip nao traz mais
  esse arquivo. O carro de treino (Birdie RTI 95) agora e V9, com os status do V9, no Init e no servidor. Vale para
  corredores criados depois desta versao.
- **WILD F:** o V7 custava 10.000.000 mito e agora custa 540.000.
- **Loja:** o 366C passou de V6 para V9 e fica no fim dos V9 normais. O Fervor passou de V2 para V5. O TyrantX CT fica
  em primeiro nas categorias especiais, antes dos BT. Copie o Cliente/Init_us.agt para Data_US.

**Atualizacao 34 (mesma versao):**
- **V dos carros na loja:** 55 carros que eram V9 (V-STAR era V8) mudaram de V, com o preco, os status e o nivel do V
  novo. Exemplos: BULLITT, Seed, Prometheus, Square, Hybrid, Monza e RAPIDO V2; START, CANTATA, ECO, AXIS e Allied V1;
  GRAVITY, PRANA e Cosmic V6; SILHOUETTE, F-WING, N1, Grace e 387SR V8. Onde o jogo nao tinha status para o V novo
  (V-STAR, por exemplo), os status saem dos do V9 na proporcao normal dos V. A mudanca esta no Init_us.agt, no
  Init.agt e no VehicleList.json/VehicleUpgrade.json do servidor (os bots seguem o V novo).
- **Ordem da loja:** os carros normais vem do V1 ao V9. Depois vem BT, ST, MINI e Xmas. O Sora GT e carro normal: fica no V7, nivel 60, e nao conta mais como carro do nivel 90.
  Copie o Cliente/Init_us.agt para Data_US.

**Atualizacao 33 (mesma versao):**
- **Loja de carros:** a loja so lista os carros que estao na tabela VehicleColorGroups (Index, Car Name, UI Name,
  Unique Id, New, Hot, GM, Category). Venom, ASTRO, SILHOUETTE, Dragoon, F-WING, RAGE, Eight, Shadow e os outros nao
  estavam nela. Foram colocados 160 carros no Init_us.agt (177 no Init.agt), todos com Category 7. O Monster CT (352)
  e o carro de treino (95) ficaram de fora. Copie o Cliente/Init_us.agt para Data_US.
- **Garagem do perfil upado:** `UppedGarageExpansion` passou de 0 para 8, o que abre os andares 2F a 9F.
  Se o 9F continuar fechado, coloque 9 no server.json.
- **Bot de tanque:** o Juno estava com o CR2 (67). No VehicleDisplay do jogo o CR2 tem "Support" 0, e o jogo desenha
  todo carro assim como tanque. Os carros com Support 0 sao Solo 43, Seven 49, PT Cruiser 64, CR2 67, Deluxe 68,
  Zorro 74 e Leo Patrol_TW 85. Eles entram em `UnsupportedCars`, junto com o Monster CT 352 (que fechou o jogo).
  Bots que estavam com eles trocam de carro ao ligar o servidor.

**Atualizacao 32 (mesma versao):**
- Carro de treino (Test Driver, Birdie RTI 95) fora da loja de verdade: "Sellable" 0 no Init_us.agt (o Display -1 so
  mandava ele para o fim da lista). Copie o Cliente/Init_us.agt para Data_US.
- **Carros que a loja do jogo nao lista** (Venom, ASTRO, HYKAN, RAGE, Dragoon, F-WING, Eight, Square, SILHOUETTE e as
  versoes ST/BT) funcionam: compre pelo chat com `!!carro <nome ou numero>` (ex.: `!!carro 353` = Venom). Bots tambem
  podem usar. So o **Monster CT** (352) fica fora (bot com ele virou um tanque e o jogo fechou): `UnsupportedCars`.

**Atualizacao 31 (mesma versao):**
- **Option Parts liberadas**: os quadrados de opcao das pecas no carro (o X) abrem com o flag Main.OptionItem
  (`OptionParts`, ligado; `DockingParts` tambem). A Option Part vai no quadrado da peca do mesmo tipo (op_S speed, op_A
  accel, op_C durabilidade, op_B boost; op_F em qualquer uma), os pontos dela somam no carro, colocar outra no mesmo
  quadrado troca a antiga, e tirar a peca do carro tira a opcao junto.
- **Monster CT**: a linha nova agora vai no FIM da lista de carros do Init_us (antes foi no meio, depois do Cobalt, e
  isso desalinhou a loja: o carro de treino apareceu no fim). Na loja continua no fim dos V6, 1.000.000 mito.

**Atualizacao 30 (mesma versao):**
- **Tabelas do jogo atualizadas pelos patches** (pasta Patch do cliente: o jogo usa as tabelas de la, nao as do
  World.agt). Missoes instantaneas (207 linhas: agora o numero que o cliente manda bate - era a causa da patrulha
  "de Oros" e dos bots fora do mapa), icones/postos (as 5 cidades, indices certos) e entregas (tempos de medalha certos).
- **Monster CT** (Dakoda, monster truck, V6, nivel 50, 1.000.000 mito) na loja de carros, no fim dos V6 (posicao 73): linha nova no VehicleDisplay e
  9 graus no VehicleList do Init_us.agt, coluna nova na tabela de pecas visuais por carro, chave "Monster CT Key" na
  tabela de itens do patch.009 e no servidor (VehicleList/VehicleUpgrade/UseItemClient.json). O modelo ja estava no patch.
- **Drift shop: 39 pecas que estavam no jogo mas fora da loja**: aero sets A/B/C/D dos MINI (Eric, Impact, Cielo, Fondo,
  Aria), RunnerR basic/aerokit, Liner Aero Set MK.II, e as asas de Venom, F-WING, Dragoon, HYKAN, PLUCK, Luna, Elisha,
  Cobalt, ALBIDA, 366C, Evolve, Spear, 387SR, Horizon, Midnight, Advance, natura, Atoka (permanentes, mito).
- **Bots**: MINI tambem entram no V9 (nivel 85+).
- Os arquivos do cliente (Data_US/Init_us.agt e Patch/patch.009) ja foram trocados no seu jogo; os originais ficaram em
  Data_US/backup e Patch/backup.

**Atualizacao 29 (mesma versao):**
- **Carro do bot pelo V da loja**: cada bot usa um carro do V que o nivel dele libera na loja (V1 ate o 9, V2 a partir do
  10, V3 20, V4 30, V5 40, V6 50, V7 60, V8 75, V9 85; `CarTierLevels`). Nivel 90+ continua com Xmas/ST/BT/GT. Nao mexe nos
  bots fixos (Marsteh, Brunizin.X), nos seus outros corredores, nem em quem ganhou carro na troca. Os amigos iniciais
  trocam de carro quando sobem de V.
- Conferidos os 11 patches + vehicles.agt: todo carro com modelo e tabela ja esta na loja (363 carros). Fora da loja so
  o Birdie RTI de treino; o "Monster CT" (pc_0352s) so existe na tabela coreana (Init.agt), nao na americana.

**Atualizacao 28 (mesma versao):**
- **Part shop: Convert dava crash** - a resposta era um eco de 12 bytes e o jogo le 52 (peca, as 10 opcoes, modo):
  lia alem do pacote e fechava. Agora vai a resposta certa, antes da lista de itens.
- Conferido o resto do part shop: Combine agora manda a confirmacao (429, com a peca nova), Upgrade Double com o eco do
  tamanho certo, compra com o preco em 4 bytes, uso do Booster Protector e da caixa de peca visual com a resposta certa.

**Atualizacao 27 (mesma versao):**
- **Economia de energia** (`EconomiaEnergia`, ligada): menos carros no trafego (220 em vez de 450, ate 240 m), bots
  visiveis ate 300 m, 15 bots no Driver Dome e 8 estacionados na cidade. O jogo desenha e simula menos carros, gasta
  menos CPU e esquenta menos. `false` volta ao movimento cheio.
- `Cliente/goption_economia.ini`: as suas opcoes graficas com sombra, alcance de visao, efeitos e luz dinamica mais
  baixos (para Data/Init/goption.ini; faca copia do seu antes).

**Atualizacao 26 (mesma versao):**
- **Patrulha: bots saindo do mapa corrigido.** O cliente manda um numero de patrulha que no servidor era uma patrulha de
  Oros (pontos em outra cidade): os bots iam para la. Agora, se os pontos da patrulha nao forem da sua cidade, o
  servidor usa a patrulha do mesmo tipo (party/solo) da sua cidade e faixa de nivel (aparece no log).
- **HUV**: o pulo agora so acontece com 10 s sem bater (`PartyChaseTeleportSeconds`) ou a mais de 400 m do HUV
  (`PartyChaseTeleportDistance`); atras do HUV eles aceleram mais (`PartyChaseCatchUp` 1,35) para alcancar e bater.
- **Undercity travada na cena**: se o cliente nao mandar a etapa 1 em 12 s, o servidor manda sozinho
  (`UnderCityStageFallbackSeconds`).
- **Party (1o da fila)**: o replay nunca volta para tras no seu rastro (o P2 ia e voltava).
- **Troca de pecas com 2 slots por tipo**: vaga livre e equipada na hora; com as duas cheias, a 1a peca da troca troca a
  1a dele e a 2a peca troca a 2a. O bot diz no chat qual slot a peca melhora. Ate 2 pecas de cada tipo por troca.
- **!!visual** lista so as pecas do inventario do drift shop (as que estao no carro ficam). `!!visual1` e `!!caixa3`
  (sem espaco) tambem funcionam.
- **Carros Xmas / ST / BT / GT liberados no nivel 90** (Req Level 90 no Init.agt e Init_us.agt: 138 carros, lista em
  Cliente/carros_nivel90.txt). Bots nivel 90+ passam a usar um desses carros (menos os fixos, seus corredores e quem
  ganhou carro na troca).
- **Bots conversam no chat** (`BotChatter`, a cada ~90 s): chamam para correr, anunciam promocao no leilao ou um item,
  chamam para HUV, Rush e Undercity, e outros respondem. **Trocas entre bots**: quem recebe so aceita peca melhor que a
  pior das duas dele (ou carro de grau maior); senao se estressa e cancela. Seus outros corredores nunca entram.
- Copie o Init.agt (para Data) e o Init_us.agt (para Data_US) da pasta Cliente de novo.

**Atualizacao 25 (mesma versao):**
- **Patrulha em party**: tempo limite de 5 min (`PartyPatrolSeconds`), o relogio corre (a cada 5 pontos +4 s), a
  contagem de pontos de cada membro (bots tambem) aparece na janela da party, pontos de emergencia dao tempo extra,
  janela de resultado no fim; o tempo acabou -> a missao falha.
- **Bots nas missoes nao saem mais do mapa**: ponto perto (ate `MissionBotDriveRange` 60 m) eles vao dirigindo; mais
  longe eles aparecem no ponto (na estrada) depois de um instante, em vez de andar reto pelo meio dos predios.
- **HUV voltou ao jeito anterior** (eles dirigem atras do HUV e batem), mas agora, quando o HUV faz curva, eles pegam o
  trecho do caminho do HUV para fazer a mesma curva e depois voltam a ir em cima dele. O modo "repetir o caminho do
  HUV" continua em `PartyChaseReplay: true`.
- **Rush Time**: entrar na zona responde; contagem de 10 s (`RushCountdown`), a party e corredores da cidade (ate
  `RushBots` 8) aparecem perto de voce e entram; no inicio o boss aparece e todos vao nele (batem no boss tambem).
  Boss derrotado: mito da cidade (30k/50k/80k/100k) e o resultado. 10 min no maximo.
- **Undercity**: entrar nela tira voce da zona da Rush (o travamento na cena aconteceu logo depois de entrar na zona
  da Rush com um HUV chamado). Se travar de novo, me mande o log.

**Atualizacao 24 (mesma versao):**
- **Party segue com atraso (replay)**: cada membro repete os seus movimentos (caminho, drift, fumaca) com atraso
  (`PartyFollowDelay` 0,6 s por posicao), sem chegar a menos de (distancia x posicao) de voce. Voce parado: param em
  fila atras e abrem um pouco para o lado. `PartyFollowReplay: false` volta ao modo antigo (dirigindo sozinho).
- **HUV com a party**: no inicio eles continuam te seguindo; quando passam perto do caminho do HUV, entram nele e
  repetem o caminho do HUV (sem 360, sem se perder), um atras do outro; no ataque encostam e batem. Sem chegar perto
  em `PartyChaseTeleportSeconds`, pulam no caminho do HUV.
- **Fumaca nos bots parados**: o estado "parado" so e copiado depois de 2 s com voce parado (antes pegava a fumaca do
  seu drift/arrancada).
- **Missoes instantaneas**: no posto de patrulha a oferta e a patrulha daquele posto (na sua faixa de nivel); as
  missoes de Battle Zone (Mecha Watch etc.) nao sao mais oferecidas na cidade. O log agora mostra o pedido, a oferta,
  cada ponto e se o cliente falhou a missao.
- **Chave vira caixa de carro com duplo clique** (se o jogo mandar o uso da chave); `!!caixa` continua.
- **Peca visual na troca**: `!!visual` lista as suas pecas do drift shop; `!!visual N` guarda a peca numa caixa no
  inventario. Coloque a caixa na troca: o bot equipa (neon, adesivo e rodas aparecem no carro dele) e te devolve a
  visual antiga dele, se tiver. Usar a caixa no inventario devolve a peca.

**Atualizacao 23 (mesma versao):**
- **Delivery Service (entregas) funcionando**: a lista do posto, comecar, os pontos no mapa, o tempo e a medalha
  (ouro/prata/bronze da tabela do cliente), exp e mito da medalha (`DeliveryRewardMultiplier`), desistir (volta ao posto)
  e o seu melhor tempo no ranking (arbeit.json).
- **Missoes instantaneas funcionando**: o pedido no posto (sorteio pela tabela do cliente, pela sua faixa de nivel),
  comecar, cada ponto da patrulha (exp por ponto), fim da patrulha no ultimo ponto (exp final), concluir, desistir.
- **Bots da party nas patrulhas e entregas**: param de te seguir e cada um vai ao ponto mais perto que falta (sem
  repetir o de outro bot; o mais perto de voce fica para voce) e pega ele (aparece no chat "Fulano pegou um ponto").
  Sem chegar em `MissionBotTeleportSeconds` (25 s), pula para o ponto. Desligar: `PartyBotsHelpMissions: false`.
- **Troca com os bots** (botao Trade no bot): as vezes ele recusa (o calado e o competitivo recusam mais; party, amigos
  e crew sempre aceitam; `TradeBotsRefuse: false` desliga). Coloque uma peca (velocidade/aceleracao/durabilidade/boost):
  o bot poe no lado dele a peca antiga que volta para voce e o chat mostra a comparacao (+pontos antes -> depois).
  Chave de carro ou caixa de carro: o bot passa a usar esse carro e te da o carro antigo dele (o chat compara
  velocidade/aceleracao/durabilidade/boost). Tranque e confirme: o bot tranca e confirma junto. As pecas que o bot
  ganha somam nos atributos dele (correr, HUV). Pecas do drift shop (visuais) nao entram na troca.
- **Conta upada**: mesmo ja salva antes, entra com a Mittron Island Guardian montada; a recompensa diaria soma o mito
  de todas as licencas.
- **Gacha (Gotcha)**: maquina de carro da a caixa de carro (BT/ST V1) ou pontos e mostra o premio; usar a caixa no
  inventario da o carro.
- **Trocar carro com bot pela caixa**: o jogo nao deixa por a chave na troca. Digite `!!caixa` para ver os seus carros e
  `!!caixa N` para guardar o carro N numa caixa de carro no inventario (lembra o carro e o grau, em carboxes.json).
  Coloque a caixa na troca: o bot passa a usar o carro e te da o carro antigo dele. A caixa tambem pode ser usada no
  inventario para tirar o mesmo carro de volta.
- **Bots online sorteados a cada entrada**: toda vez que voce entra num corredor, sorteia quem esta online (lobby, amigos,
  rua); os da party e os especiais ficam sempre. Corrigido erro de compilacao CS8957 no Gotcha.cs.
- Me mande o log se algo nao responder (entregas 321-340, missoes 990-1003, troca 500-515, gotcha 3500).

**Atualizacao 22 (mesma versao):**
- **Drift shop funcionando**, e TODAS as pecas sao permanentes. Preco: o "para sempre" da tabela; sem ele, o maior
  periodo x 2 (`VShopPermanentMultiplier`). Itens de CC pagam em CC; sem CC suficiente, em mito (1 CC = 1000 mito,
  `VShopCcToMito`). A peca vai direto para o carro; pintura/neon/vidro novo troca o antigo. Bonus de atributos das
  pecas somam no carro. Equipar, tirar e jogar fora tambem. Fica em `vsitems.json`.
- Licencas: a recompensa agora e diaria, todo dia a partir das 10h do relogio do PC (`LicenseRewardHour`).
- Licencas dos bots: cada bot equipa 3 (a de corrida pelo nivel: Rookie ... No.9, e duas pelo estilo dele). Nivel
  baixo = as faceis, nivel alto = as dificeis. Efeito: corrida = mais velocidade seguindo/perseguindo; HUV Hunter =
  mais dano no HUV; Undercity Guardian = mais dano e HUBs na Undercity; entrega/patrulha/cidades = mais bonus de
  recompensa para a party. Aparecem no `!!perfil <bot>` e na janela de licencas dele.
- Party seguindo refeita em formacao: 1o e 2o atras e de lado, 3o e 4o mais atras. Vao no seu ritmo (aceleram junto,
  freiam junto), sem dar 360 (se o lugar ficou para tras, so diminuem). Voce parado: param no lugar deles. Longe
  demais por 8 s ou sem se arrumar: pulam para o lugar (`CityBotFarSnapSeconds`, `CityBotSnapSeconds`).
- HUV: corrigido a party parada no comeco (o jogo marca o SEU carro com numero de objeto na perseguicao; agora o
  servidor reconhece o seu carro pelo tipo). Cada membro pega um HUV; diminui nas curvas; se nao encostar no HUV
  em 30 s pula para tras dele e o contador reinicia (`PartyChaseTeleportSeconds`). Dano dos bots ajustado ao seu
  (`CityHuvHitDamage` 0.05) e com o nome deles no golpe.

**Atualizacao 21 (mesma versao):**
- Corrigido: o Director Green ("It's time to get started") aparecia sempre ao entrar na cidade. Agora so para
  corredor novo de verdade (nivel inicial, sem missao feita, sem posicao salva); o perfil upado vai direto.
- Party seguindo: quando passa de voce ou e empurrada para a frente, nao da mais a volta (seguia pela parede):
  continua na sua direcao, devagar e de lado, ate voce passar. Fora do rastro, volta para ele por um ponto perto.
  Encostando em voce andando, so abre para o lado (nao para mais). Drift: o bot derrapa e solta fumaca onde voce
  derrapou (`PartyBotDrift`).
- HUV: a party vai pela ESTRADA (pelo seu rastro ate chegar no trajeto do HUV, e dai pelo trajeto dele), sem
  atravessar parede. Encostou no HUV (`PartyHuvContact`, 4 m de centro a centro = praticamente tocando): o golpe
  conta (pacote 187, como um jogador da party). `CityHuvHits`, `CityHuvHitSeconds`, `CityHuvHitDamage`.
  Na Undercity a mesma distancia (`UnderCityBotHitRange` = 4).
- Corrigido: na perseguicao, os bots de fora da party (e o trafego) apareciam com sirene. So a party tem.
- Perfil upado: todas as missoes principais feitas (a final OMD) e todas as 66 licencas (`UppedAllQuests`,
  `UppedAllLicenses`). Vale a partir do proximo inicio do servidor.
- Licencas funcionando (lista, condicoes e as 3 montadas). Recompensa das licencas (a soma do "Amount Paid" de cada
  uma) a cada 10 horas pelo relogio do PC (`LicenseRewardHours`), avisada no chat. Fica em `licenses.json`.

**Atualizacao 20 (mesma versao) — tutorial do jogo:**
- Ligado o tutorial proprio do jogo (Apprentice): o corredor novo nasce com o Birdie RTI de treino ("Test Driver"),
  vai ao Driver Dome / Battle Zone fazer a corrida de treino guiada (o jogo controla) e no fim escolhe o carro
  inicial; o carro de treino sai da garagem. `ApprenticeTutorial` (false = volta ao jeito antigo, com o Kicker).
- Corrigido: o carro escolhido na criacao (Laputa, Drifter, Metro, Birdie RTI) era trocado pelo Kicker.
- So vale para corredores criados a partir de agora.

**Atualizacao 19 (mesma versao) — loja de carros organizada:**
- Descoberta: o jogo usa a tabela do `Init_us.agt` (pasta Data_US) para a loja; o grau "V" de 43 carros la era
  diferente do `Init.agt` (onde estavam os graus da sua tabela). Agora os dois arquivos tem o mesmo grau, e os
  atributos mostrados sao os do grau de venda.
- Goat ST, Goat BT, Cielo Xmas, Fervor ST e Fervor BT agora sao V9 (atributos e preco do V9).
- Ordem: os 10 carros iniciais no topo; depois V1 -> V9; em cada grau o carro base seguido do ST/BT (e CT);
  MINI e Xmas no fim de cada grau. Lista completa em `Cliente/ordem_loja.txt`.
- **Copie `Cliente/Init.agt` para `Data` e `Cliente/Init_us.agt` para `Data_US`.**
- Metro e Birdie RTI (iniciais) agora V1. O Birdie RTI de treino ("Test Driver") saiu da loja.
- Nivel por grau tambem no cliente: a loja mostra o nivel exigido de cada carro (V1 1, V2 10, V3 20, V4 30, V5 40,
  V6 50, V7 60, V8 75, V9 85 — `CarTierLevels`). O servidor ja recusava a compra abaixo do nivel (loja, `!!carro` e
  leilao); ST/BT/Xmas/MINI pedem `SpecialCarsMinLevel` (100).

**Atualizacao 18 (mesma versao):**
- Corrigido (HUV): o jogo manda os movimentos do HUV com o SEU numero, e o servidor achava que eram voce. Por isso a
  party ficava pulando entre voce e o HUV, e o trafego sumia e aparecia (o servidor mandava os carros de perto do
  HUV, nao de perto de voce). Agora o HUV e separado pelo numero de objeto dele.
- A party nao alterna mais entre seguir voce e o HUV se o HUV demorar a mandar movimento (`PartyChaseKeepSeconds`).
- Carro do trafego batido (por voce ou por um HUV): o servidor deixa o jogo cuidar dele por 20 s e o recoloca longe
  de voce, em vez de puxa-lo de volta para a faixa. `CityTrafficHitSeconds`.
- Party com voce parado: o bot vem ate voce, vira de 20 a 70 graus para o lado dele (um para a esquerda, outro para
  a direita) e para na distancia certa (1o a 5 m, 2o a 10 m...). Parado, fica parado esperando voce andar (nao gira).
  Se em 4 s ele nao se arrumar, ou ficar colado em voce, ele pula para o lugar dele. `CityBotSnapSeconds`.

**Atualizacao 17 (mesma versao) — Undercity:**
- Party seguindo voce refeita: cada bot DIRIGE (posicao, direcao e velocidade proprias, sem teleporte), vai pelo seu
  rastro e controla a distancia; quando voce freia, ele freia antes (sem bater). Com voce parado, ele faz uma curva
  para um dos lados e para atras/ao lado (bots alternam esquerda e direita). Se voce for para cima dele, ele sai da
  frente devagar. `CityBotFollowGap` (distancia), `CityBotSideGap` (lado), `CityBotMinDistance` (folga minima).
- **Undercity ligada**: fale com a Tatiana (ou digite `/ucm`, `/uck`, `/ucc`, `/uco` no chat) com sua party de bots.
  Taxa: Moon Palace 600, Koinonia 1200, Cras 2100, Oros 4200 mito. As 4 missoes funcionam: 100 HUBs, HUBs barreira +
  HUB principal (3 tentativas), ondas de HUV e o BossHUV, com nota final (D a S) e premios (exp, mito, S Mittron
  Enhancer, peca do set Alpha/Beta da cidade e Mittron Enhancers pela classe).
- Seus bots da party vao junto: destroem HUBs na Missao 1 (aparecem na contagem da party; sempre deixam pelo menos
  10 para voce), seguem voce e perseguem os HUVs nas Missoes 3 e 4, e batem neles quando chegam perto (os HUVs que
  eles pegam contam para eles, e voce ganha metade da exp/mito). Seus outros corredores na party ganham junto.
- Na Undercity nao ha trafego nem bots estacionados.
- Teste: `!!uc fase N` faz a proxima Undercity comecar na missao N (2 pula os HUBs, 3 tambem as barreiras, 4 vai
  direto ao BossHUV; missoes puladas nao dao premio). `!!uc fase 1` volta ao normal. `!!uc` mostra a ajuda.
- Opcoes: `UnderCityEnabled`, `UnderCityFee`, `UnderCityFees`, `UnderCityRewardMultiplier`, `UnderCityStartPhase`,
  `UnderCityBarrierPlayers`, `UnderCityMission2ExtraSeconds`, `UnderCityBotHubSeconds`, `UnderCityHubsLeftForYou`,
  `UnderCityBotHitSeconds`, `UnderCityBotHitRange`, `UnderCityBotHitAnywhere`, `UnderCityBotDamage`,
  `UnderCityBotBossDamage`, `UnderCityBotKillShare`, `UnderCityBetaClass`, `UnderCityEnhancer`, `UnderCityEnhancers`.
- Se os bots nao baterem nos HUVs, troque `UnderCityBotHitAnywhere` para true e me mande o log (linhas "Undercity:").

**Atualizacao 16 (mesma versao):**
- HUV com party refeito: cada membro fica com um HUV (so troca se o dele acabar) e CORRE atras dele como carro de
  verdade (posicao e velocidade proprias, sem teleporte), pelo caminho do HUV. Velocidade maxima pelo grau do carro
  dele (V1 ~122 km/h ... V9 ~238 km/h); freia se passar do HUV. Booster: 6 s carregando atras do HUV e 2,5 s de ataque
  (1,6x, em cima do HUV).
- Emblemas: o servidor responde o pedido de imagem (GetImage) em qualquer conexao e registra no log.

**Atualizacao 15 (mesma versao):**
- **Emblemas das crews**: o jogo le os emblemas de `Data\GameUI\teammark\01..10`, e essa pasta so tinha o `00`.
  Copiei os 10 emblemas para la (tambem em `Cliente\Data\GameUI\teammark` no zip, para quem instalar do zero).
- Clima: ciclo 1 limpo -> 4 fim de tarde -> 2 nublado, 15 min cada; em cada fase 30% de chance de chover 5 min,
  comecando no meio da fase.
- Driver Dome: os pontos que voce ja andou ficam fixos (para de aprender depois de 300; voce ja tem mais de 1000);
  30 bots estacionados neles. `DomeLearnLimit`, `DomeParkedBots`. Os 1115 pontos vao embutidos no Rice.exe
  (`res/dome_points.default.json`): quem instalar do zero ja tem os bots espalhados no Dome.
- HUV com party: cada membro pega o trajeto de um HUV; segue de perto 6 s (booster carregando) e ataca 2,5 s
  (vai com tudo em cima do HUV, 1,6x mais rapido). `PartyChaseCharge`, `PartyChaseAttack`, `PartyChaseBoost`.
- Corrigido: os carros dos bots levavam o numero de objeto do HUV (o cliente achava que eram HUVs sem nome).
- Corrigido: em party, o cliente pedia sem parar os bonus de party (pacote 1156, ~700 por segundo); agora responde.
- Pintura dos bots: Marsteh vermelho (Carmine), Brunizin.X preto (Black pearl), os outros uma cor da paleta.
  (Palpite de onde fica a cor: se continuarem brancos, mude `BotColorByte` para 0.)

**Atualizacao 14 (mesma versao):**
- Clima: 1 limpo, 2 nublado, 3 chuva, 4 fim de tarde. Ciclo 1 (25 min) -> 2 (15) -> 4 (20); entre as fases, 30% de
  chance de chover 8 min. `WeatherOrder`, `WeatherMinutes`, `WeatherRainChance`, `WeatherRainMinutes`.
- Driver Dome: os pontos por onde voce anda ficam salvos (`dome_points.json`); 20 bots estacionam em posicoes
  aleatorias entre eles (sempre dentro da area). Quanto mais voce anda no Dome, mais espalhados ficam.
- Pecas visuais (neon, adesivo, rodas, aerofolio) nos bots, menos Marsteh, Brunizin.X e seus corredores. `BotVisuals`.
- Party na perseguicao de HUV: os bots da party vao atras do HUV (0,35 s atras dele, para bater). `PartyChaseHuv`.
- Corrigido: os movimentos do HUV (o cliente manda junto com os seus) entravam no seu rastro e os bots copiavam o HUV.
- Bots andando nao copiam mais as suas acoes (volante, nitro, freio, fumaca): usam o seu estado parado.
  Ultrapassam pelo lado quando ha um carro do transito, outro bot ou voce na frente. `CityOvertakeSide`.
- Party: chega mais perto (5 m), para antes de voce, e nunca fica a menos de 3 m (se encostar, se afasta).
  `CityBotFollowGap`, `CityBotSideGap`, `CityBotMinDistance`.
- Cor da placa das crews: Rookies/KoiFlow azul, NightRiders/MitoKings rosa, CrasGhosts/OrosLegends laranja; crews
  criadas por voce azul (`PlayerCrewLevel`: 0 verde, 1 azul, 2 rosa, 3 laranja).
- `!!recrutar <nome>`: o lider traz um bot especifico (inclusive Marsteh / Brunizin.X) para a crew.

**Atualizacao 13 (mesma versao) — clima:**
- O servidor agora manda o clima (pacote 640, achado no DriftCity.exe): 4 ceus do jogo — limpo, fim de tarde, chuva
  e neblina — com transicao de ~10 s. Ciclo automatico de 60 min (`WeatherOrder` 1,2,4,3 / `WeatherMinutes`
  25,15,10,10). Corrige o ceu preto na Battle Zone (o cliente ficava sem clima definido).
- `!!clima 1`..`!!clima 4` fixa um ceu para testar; `!!clima 0` volta ao ciclo. Me diga qual numero e qual ceu.

**Atualizacao 12 (mesma versao):**
- **Troque de novo o `Data_US\Init_us.agt`**: os pilotos IA nao tem mais "[AI]" no nome (Elio, Rapunzel...).
- **Bots andando pela cidade**: ~70% dos bots da cidade andam pelas faixas do transito (12-20 m/s), o resto estaciona.
  Quem se afasta demais reaparece em outra rua perto de voce. Marsteh e Brunizin.X continuam estacionados.
  `CityDrivingBots`, `CityDrivingShare`, `CityDriveSpeedMin/Max`.
- **Driver Dome (area 0)**: os bots nao aparecem mais fora da area; estacionam em lugares por onde voce ja passou
  (um de cada vez, ao lado do caminho). `DomeParkedBots`.
- **Party sem bater em voce**: o bot segue o seu rastro e, quando chega perto, para a 9 m (18 m o segundo...), desviado
  3,5 m para o lado, com velocidade zero. `CityBotFollowGap`, `CityBotSideGap`.
- Rotacao: a posicao do carro tem a rotacao (radianos) no 4o valor; carros dos bots ficam alinhados com a rua.
  Se ficarem virados errado, teste `CityBotHeadingMode` 1, 2 ou 3.

**Atualizacao 11 (mesma versao):**
- Corrigido: abrir o perfil de um bot derrubava o servidor (posicao nula); mensagens de erro (janela) derrubavam o
  cliente: agora vao para o chat.
- **Bots estacionados pela cidade**: 12 bots parados em pontos de RUA (faixas do transito, nunca em cima de predios),
  ate 600 m de onde voce entra; Marsteh e Brunizin.X perto de voce (20-90 m), separados.
  `CityParkedBots`, `CityParkedRadius`, `CityBotViewRange`.
- **Seus outros corredores viram bots**: todos os corredores da sua conta aparecem como amigos (online). Chame para a
  party: eles seguem voce, correm junto e ganham a mesma exp/mito (corrida, HUV, combo da party). Quando voce entrar
  com eles, as recompensas ja estao la. `AltRewardShare` (1.0 = 100%).
- **Combo da party**: o contador de combo da party funciona (EnterGroup/GroupCombo na conexao de area); cada bot da
  party soma combos tambem (50%). A cada 20 combos da party: +150 mito x (combos/20).
  `PartyBotComboChance`, `PartyComboRewardEvery`, `PartyComboRewardMito`.

**Ajuste:** transito mais denso: `CityTrafficCars` 450 por cidade (era 300).

**Atualizacao 10 (mesma versao) — cartas "?" e bots na rua (etapa 1):**
- **Cartas "?" do fim da corrida**: achei no DriftCity.exe o pacote da carta (483 -> 484 Cmd_PvpBattleBonusResult).
  Clicar numa carta da um item aleatorio (ate o seu nivel); as IAs da corrida abrem as outras cartas.
  `BattleRewardCards`.
- **Bots na rua (experimental)**: os bots da party seguem o seu carro pelo mesmo caminho (com 1,2 s de atraso cada);
  Marsteh e Brunizin.X aparecem estacionados num ponto da rua por onde voce passou (uns 8 s depois de entrar na cidade).
  Nome e crew em cima do carro (PlayerInfo 809). `CityBotsEnabled`, `CityBotsSellers`, `CityBotFollowDelay`.

**Atualizacao 9 (mesma versao) — nomes dos bots na corrida:**
- **Troque o `Data_US\Init_us.agt`** pelo da pasta `Cliente`: a tabela de IA do jogo agora tem 62 linhas com os nomes
  dos bots (Marsteh, Brunizin.X, DriftKing...). O cliente mostra o nome da IA pela linha da tabela, por isso antes
  aparecia "[AI] Karlos". Cada bot usa a linha do nivel dele (velocidade de acordo com o nivel).
- Os bots agora sao uma lista fixa (`res/bots.default.json`, embutida) igual a do cliente. O `bots.json` antigo e
  trocado sozinho; amigos com Marsteh/Brunizin.X continuam. Bots tambem aparecem como rivais nas salas.
- Personagens diferentes por bot (`BotAvatarIds`, padrao 1,2,3,4 em rodizio).
- IA um pouco mais dificil: `BattleAiDifficulty` 0.45 (era 0.35) e `BattleAiMaxLevel` 50 (era 40).

**Atualizacao 8 (mesma versao) — Bots, parte 2 (amigos e party):**
- **Amigos**: adicione um bot pela janela de amigos (nome) ou `!!amigo <nome>`. Ele aceita em segundos e fica salvo em
  `friends.json`. A lista mostra nivel, crew e se esta online.
- **Party**: convide um bot pela janela (lista de amigos) ou `!!party <nome>`. Amigos sempre aceitam; outros podem recusar
  (depende do estilo). Ate 3 bots. `!!party` mostra quem esta, `!!party sair` desfaz. O cliente manda "sair da party"
  ao trocar de area: a party de bots continua e e reenviada.
- **O que a party faz hoje**: ao criar sala no Battle Zone, os bots da party entram sozinhos como corredores IA, com o
  nome e o carro deles (so em pista com icone de IA). No HUV, cada bot da +10% de exp/mito (`PartyBotBonus`).
- **Perfil**: clicar/ver informacoes de um bot (GameCharInfo) mostra nivel, carro, crew e status.
- Ainda nao: bots seguindo voce na cidade, Rush e Undercity com bots (dependem de colocar os bots na rua — proxima etapa).

**Atualizacao 7 (mesma versao) — Bots, parte 1:**
- **Perfis de bots** (`bots.json`, criado na 1a vez ao lado do Rice.exe): 60 jogadores simulados com nome, nivel, carro,
  crew e estilo (amigavel, competitivo, comerciante, calado). Apague `bots.json` para gerar outros.
- **Crews de bots** (em `crews.json`): Rookies (nivel 1+), KoiFlow (10+), NightRiders (20+), MitoKings (30+), CrasGhosts (45+),
  OrosLegends (70+). No Crew Center use "entrar/aplicar" digitando o nome da crew: o lider bot aprova na hora.
  A lista de membros mostra os bots (nivel, carro, online). No chat da crew ("team") eles respondem as vezes.
- **Leilao vivo** (`auction.json`): ~80 anuncios de bots (pecas N/S/H, opcoes, docking, enhancers, kits...), estoque que gira
  sozinho. Comprar funciona. Voce pode anunciar seus itens (taxa de 3%, o item fica bloqueado): os bots compram com o tempo
  (preco ate o valor de loja = rapido; ate 1.5x = medio; ate 2.5x = devagar; acima = nunca). Quando vender, abra
  "Meus anuncios" e clique Completar para receber o mito. Cancelar devolve o item.
- Comandos novos no chat: `!crews`, `!bots`, `!perfil <nome>`, `!recrutar` (lider de crew: um bot entra na sua crew), `!leilao`.
- Opcoes: `BotsEnabled`, `BotsCount`, `BotsAppearOnline`, `BotsCrewChat`, `AuctionEnabled`, `AuctionBotStock`,
  `AuctionFeePercent`, `AuctionDays`, `AuctionBuyChanceCheap/Fair/Pricey`, `AuctionBotTurnover`.
- Proximas partes: salas de bots no Battle Zone, mensagens/correio dos bots, bots andando na cidade.
- Bots fixos: **Marsteh** (PLUCK ST, V9) e **Brunizin.X** (Mento MINI, V9), nivel 99. Serao os primeiros a aparecer
  na rua quando os bots forem para a cidade, vendendo esses carros. Ja aparecem em `!perfil Marsteh` / `!perfil Brunizin.X`.
- Crew: o botao de fechar/apagar crew (lider) agora apaga na hora (no jogo original demorava 7 dias).
  Pelo chat: `!sair crew` (membro sai; lider apaga a crew).
- **Leilao de carros** (aba de carros): ~15 carros de bots + **Marsteh** vendendo PLUCK ST V9 e **Brunizin.X** vendendo
  Mento MINI V9 (sempre a venda; se comprar, ele anuncia outro). Comprou, o carro vai direto para a garagem com a chave.
  Respeita o nivel do grau (e nivel 100 para ST/BT/MINI/Xmas). Opcao `AuctionCarStock`.
- Crew por chat (se a janela de entrar nao abrir): `!entrar <nome da crew>` e `!membros`.
- **Comandos usam `!!`** (o cliente come o primeiro `!`): `!!help` mostra todos. Ao entrar no jogo aparece o lembrete.
- **Emblema da crew**: os 10 emblemas do jogo (Data_US/gameui/teammark) vao embutidos no Rice.exe e sao enviados
  quando o cliente pede (GetImage 64 / GetTeamMark 832).
- **Carros do leilao com km e combustivel**: cada carro anunciado tem km rodados e litros no tanque. Muito km e tanque
  vazio = um pouco mais barato (ate ~12% pelo km e ~8% pelo combustivel); pouco km e tanque cheio = preco cheio.
  Vale para todos, inclusive Marsteh e Brunizin.X. Comprou, o carro chega com esse km e esse combustivel.
  Opcoes `AuctionCarMaxKm`, `AuctionCarKmDiscount`, `AuctionCarFuelDiscount`.
- Janela de Channel Battle agora abre (lista vazia: offline nao ha disputa de canais).

**Atualizacao 6 (mesma versao):**
- HUV: chamado (ChaseRequest) respondido com um HUV da tabela do jogo perto do seu nivel; perseguicao com 1-4 HUVs
  (missoes: a quantidade da missao); exp e mito da tabela por HUV pego; tela de resultado. Tabela em `res/HuvTable.json` (embutida).
  Opcoes: `HuvLevelFactor`, `HuvCountMin`, `HuvCountMax`, `HuvRewardMultiplier`.

**Atualizacao 5 (mesma versao):**
- Trafego da cidade (Moon Palace, Koinonia, Cras, Oros): o servidor move ~300 carros por cidade nas faixas do proprio jogo,
  com semaforos, e manda os que estao a ate 320 m de voce. Mapas tirados dos .agt das cidades (`res/traffic`, embutidos no Rice.exe).
  Opcoes: `CityTrafficEnabled`, `CityTrafficCars`, `CityTrafficRange`. HUVs: proxima etapa.

**Atualizacao 4 (mesma versao):**
- IAs mais lentas: o nivel do piloto IA define a velocidade (nivel 1 ~227 km/h ... nivel 99 ~408 km/h). Alvo = nivel do jogador x `BattleAiDifficulty` (0.35), no maximo `BattleAiMaxLevel` (40), variando +-`BattleAiLevelSpread` (6).
- Carro 54 (Nevera/Enzo) fora da IA (`BattleAiExcludedCars`): o bot ficava parado.
- `BattleFrontAi` = 0: as IAs largam ao seu lado e aparecem na apresentacao (antes o cliente recebia 3 e as punha na frente).

**Atualizacao 3 (mesma versao):**
- `BattleAI.json` agora vai embutido no Rice.exe (antes faltava na pasta `res` do servidor e o bot nao entrava).
- Ao sair de uma sala de time, o carro e remontado com a cor original (`BattleTeamColorReset`).

**Atualizacao 2 (mesma versao):**
- Battle Zone: criar sala, assentos, corredores IA (botao de IA no assento vazio), largada, portoes, chegada e resultado com exp/mito.
  Sozinho sem IA = treino (sem resultado). IA so corre nas pistas com icone de IA (16 pistas). Dados em `res/BattleAI.json` (tirados do Init_us.agt).
  Opcoes: `BattleAiEnabled`, `BattleExpPerPlace` (120), `BattleMitoPerPlace` (400).
- Crew: verificar nome, criar crew (nivel 20, 10.000 mito: `CrewCreateLevel`, `CrewCreateFee`), sair. Crews ficam em `crews.json` ao lado do Rice.exe.

**Atualizacao (mesma versao):**
- Modify: resposta 414 no formato do cliente 2016 (taxa, resultado, eco do pedido, +4 bytes) enviada ANTES da lista de itens.
- Battle Zone: responde a entrada do lobby (441 + lista de salas vazia 464). Criar sala/correr ainda nao.
- Crew Center: responde MyTeamInfo (841) como "sem crew".
- Formatos tirados da documentacao do emulador openDC (github.com/Xerender13/openDC).


**V0.9.9:** segunda tabela de carros aplicada (60 carros no total, somando as duas tabelas; a segunda manda quando o carro aparece nas duas).
Mesma regra: cada grau soma o total padrao mantendo a proporcao da tabela. Preco 0 na tabela = mantem o preco original. Eficiencia aplicada quando informada.
Nomes trocados: Sporty = Caliber (Dodge Caliber), US R = USR, Dtwo = Viper, MMT T1 = MMT1. G-LUX nao existe no jogo (pulado).

# (V0.9.8)

**V0.9.8:** nos 31 carros da sua tabela, cada grau soma exatamente o total padrao (V1 160, V2 190, V3 230, V4 290, V5 360, V6 440, V7 540, V8 660, V9 800), mantendo a proporcao Speed/Accel/Durab./Boost da tabela. Cliente (Init.agt + Init_us.agt) e servidor atualizados.

# (V0.9.7)

**V0.9.7:** tabela de carros aplicada a todos os 31 carros (Tigris = Tiger) e a todos os graus a partir do grau de venda.
V1-V3 foram reescalados para a forca normal do grau mantendo a proporcao da sua tabela; os graus acima seguem a mesma proporcao.
Use junto com `Cliente/Init.agt` (pasta Data) e `Cliente/Init_us.agt` (pasta Data_US) desta versao.

# (V0.9.6)

**V0.9.6:** acompanha o `Init.agt` novo (pasta `Cliente`). Use os dois juntos.
- Servidor: categorias Xmas/BT/ST/MINI exigem nivel **100** (`SpecialCarsMinLevel`); carros de cupom tambem podem ser comprados.
- `res/VehicleList.json` e `res/VehicleUpgrade.json` atualizados com os mesmos atributos, precos e graus de venda do cliente.

# (V0.9.5)

**V0.9.5 (EXPERIMENTAL, nao compilado por mim):**
- **Part Shop travando** (botoes escuros) apos Modify/Convert: agora o servidor envia uma confirmacao (eco do pedido nos pacotes 414/427/431) — e um palpite; se piorar, ponha `PartShopSendAcks` = false.
- **Itens empilham**: Booster Protector, Enhancer etc. somam no mesmo slot (ate 99).
- **Booster Protector**: uso (pacote 417) agora consome 1 e confirma (palpite do formato).
- **Combustivel**: `FuelEfficiencyMultiplier` = 68.
- **Comandos de chat** (use `!` ou `/`): `!tirar`, `!tirar todos` (pecas Sorched), `!carro <id ou nome>` compra carro de gacha/evento que a loja do cliente nao lista (cupom/SMALL fica de fora; Xmas/BT/ST/MINI exigem nivel 90). Cada mensagem recebida aparece no console como "Chat recebido".

# (V0.9.4)

**V0.9.4 (EXPERIMENTAL, nao compilado por mim):**
- **Pecas Sorched (U/H):** digite no chat `/tirar` (carro atual) ou `/tirar todos` para desequipar as pecas Sorched sem o kit (o cliente bloqueia o kit nessas pecas).
- **Modify:** agora responde (o Part Shop nao trava). Peca + Mittron Enhancer: sobe +1 com +1..3 pontos; taxa `ModifyFeePerLevel x (nivel+1)` mito; chance 100% ate +2, depois cai 10% por nivel (minimo 15%). Formato do pacote 413/430 e um palpite: me mande o log.
- **Dockers:** bonus em % dos pontos da peca, sorteado pela letra: A 10-18, B-D 19-28, E-F 29-36, 7a posicao ("S") 37-48, G-I 49-58, J-K 59-72 (N/S/H/U usam a mesma tabela, escalada pelo numero de letras de cada grau). Vale para docks NOVOS.
- **Loja de carros:** o servidor deixa comprar todos (`DealershipUnlockAll`), exceto Xmas/BT/ST/MINI, que exigem nivel `SpecialCarsMinLevel` (90). A lista mostrada na loja vem do cliente: carros que ele nao lista nao aparecem.
- **Combustivel:** tanque no padrao da tabela (multiplicador 1, minimo 0) e `FuelEfficiencyMultiplier` = 80 na "eficiencia" enviada ao carro. Se o consumo AUMENTAR, use 0.0125.

# (V0.9.3)

**V0.9.3:** (1) corrigido "could not equip item": o limite de 3 itens por slot contava todos os carros; agora conta so o carro alvo.
(2) Os pontos de Convert (Speed +N etc.) e de docks (+% dos pontos da peca) agora entram nos stats do carro.
(3) Mittron Enhancer N (`i_n_00043`), S (`00044`), H (`00045`) e o generico (`i_n_00003`, no lugar do "U", que nao existe) estao no inicio do inventario do RiceAdmin. **Apague o db.sdf.**


**V0.9.2:** com 99 carros e 260 itens o jogo travava em "Loading data". Agora o RiceAdmin tem so **Shadow (140), BMW Patrol (88) e Shadow Xmas (276)** e **1 pagina de inventario cheia**. **Apague o db.sdf** para recriar o perfil. Para testar mais carros/paginas depois: `UppedCarScope` ("dealership"), `UppedExtraCarIDs`, `UppedInvenPages`.


Foco: **Part Shop** (Combine com Seed, Convert) + **recompensas de nível e de combo**. Inclui tudo da V0.7.2
(perfil upado com garagem/inventário cheios) e da V0.8 (Combine de 3 peças iguais).

> EXPERIMENTAL: não consegui compilar nem testar aqui. Compile em **Release**, teste e me mande o console (log) de cada ação.

## O que há de novo
### 1. Combine com Seed Part -> Docking Part (sorteio feito pelo servidor)
- Coloque **3 peças do mesmo tipo e do mesmo grau** (não precisam ser iguais) + **Seed do mesmo grau** (Normal/Super/Hyper/Ultra).
- O servidor consome as 3 peças e a Seed e entrega **1 dock** do tipo certo (velocidade/aceleração/durabilidade/booster).
- A **letra** (A mais comum e fraca … última letra mais rara e forte) é sorteada. Peças melhores e **peça mais forte no meio**
  (o slot do meio pesa o dobro) aumentam a qualidade e deslocam o sorteio para letras mais raras. Seed PLUS dá um bônus.
- Pontos **brancos** (pontos base das peças, meio com peso 2) e **roxos** (nível médio de upgrade) aparecem no chat e,
  se `DockStorePoints` = true, são gravados no dock (UpgradePoint / Upgrade) — não sei como o cliente exibe isso.
- Combine de 3 peças **iguais** sem Seed continua subindo a peça para o grau seguinte (N→S→H→U).

### 2. Convert (EXPERIMENTAL — formato do pacote desconhecido)
- Registrei o pacote 426 (ModifyItem). Ele **sempre grava o conteúdo no console**. Se os números do pacote apontarem para
  uma **peça + Converting Solution (i_n_00040/41/42 ou Change Solution 116/117/118)** (+ **Enhancement Formula** i_n_00061),
  o servidor sorteia **2 opções** (atributo + Drop Rate), consome os itens e avisa no chat.
- Solution Type A = velocidade, B = aceleração, C = durabilidade. Change Solution refaz a opção do tipo da própria peça.
- Valores: Hyper segue a tela do jogo (84/81/78/75/72 e 5/4/4/3/3 %). **N, S e U são estimativas** minhas.
- Formula melhora as chances dos graus 1-2. Se o Convert usar outro pacote, nada acontece além do log — me mande o log.

### 3. Heat Treatment Kit
- O kit normal (`i_n_00025`) só serve para peças **H**. As peças "Sorched" do Shadow são **U** e precisam do
  **Ultra Heat Treatment Kit (`i_n_00025_U`)** — já está no inventário do RiceAdmin. O servidor ainda **não** trata o uso do kit
  (pacote desconhecido): use o kit e me mande o log (pacotes 417/419).

### 4. Recompensas de nível (a cada 10 níveis)
| Níveis | Mito | Itens |
|---|---|---|
| 10, 20, 30 | 10.000 | 4 docks N (letra A) + 4 peças N (Riddle/Shinden/Diablo/Gravity) |
| 40, 50, 60, 70 | 50.000 | 4 docks S + 4 peças S (Myth/Tomcat/Vampire/Ion) |
| 80, 90, 100+ | 100.000 | 4 docks U + 4 peças U (Gaia/Valkyrie/Thor/Gram) |
| 100 | — | carro **Shadow Xmas** (Lamborghini Reventon, id 276) na garagem |
- Entregue ao subir de nível numa missão; o aviso sai no chat. O mito atualiza ao reentrar no jogo.

### 5. Recompensas de combo
- A cada 20 combos: 250 × n mito (já existia) + **Booster Protector F ou D** (alternando).
- Combo ≥ 60: **Mittron Enhancer N**; ≥ 120: **S**; > 200: no jogo **não existe enhancer U**, então entrega o **H**.
  (Cada marco de 20 dentro da faixa entrega o enhancer da faixa.)

## Opções novas no server.json
| Chave | Padrão | Função |
|---|---|---|
| DockDrawEnabled / DockStorePoints | true / true | sorteio de dock / gravar pontos brancos e roxos |
| ConvertEnabled | true | liga o Convert experimental |
| ComboItemRewards | true | itens do combo |
| ComboEnhancerNAt / SAt / UAbove | 60 / 120 / 200 | faixas do Mittron Enhancer |
| LevelRewardsEnabled | true | recompensas de nível |
| LevelRewardMitoN / S / U | 10000 / 50000 / 100000 | mito por faixa |
| Level100CarSort | 276 | carro do nível 100 (0 = desliga) |

## Perfil upado (da V0.7.2)
Garagem completa (cerca de 99 carros + Shadow + BMW Patrol com peças), inventário com 8 páginas preenchidas e a 9ª vazia,
empilháveis em 99. Opções: `UppedCarScope`, `UppedExtraCarIDs`, `UppedPoliceCarID`, `UppedInvenPages`, `UppedInvenPageSize`,
`UppedStackSize`, `UppedInvenExpansion`, `UppedGarageExpansion` (valores de expansão são palpites).

## O que me mandar para eu fechar o Part Shop
Rode e copie o console depois de cada ação: (a) usar o Heat Treatment Kit (normal e Ultra); (b) Combine com Seed;
(c) Convert (peça + solution, com e sem Formula); (d) Modify / Upgrade de uma peça.

## Como atualizar
Recompile em **Release** e copie `Rice.exe` e `Rice.pdb` para a pasta `Rice` do servidor. Mantenha o `db.sdf`.
Novas chaves do `server.json` são opcionais (ver `server.json.exemplo`).
