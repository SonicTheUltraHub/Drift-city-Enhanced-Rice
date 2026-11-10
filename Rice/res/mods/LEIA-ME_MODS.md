# Mods

## Escada de crews (`mod_escada_de_crews.json`)

Desligado por padrao. Para instalar, ligar ou desligar: `Menu.bat` > `8) Mods`. O console do servidor mostra
"Mod ligado: Escada de crews" quando ele esta ligado. (Por baixo: ligado = `mod_escada_de_crews.json` na pasta do
`Rice.exe`; desligado = o mesmo arquivo com `.desligado` no fim, guardando os seus ajustes.)

O que o mod faz:
- Crews por cidade (Rookies/MoonFlow, MitoKings/KoiGhost, CrasRacers/CrasElite, OrosLegends, LegendsDC), 10 membros cada,
  cada uma no seu lado da cidade; os bots nao recrutam.
- Escada de desafios mano a mano na Battle Zone (perto de um membro da crew da vez por 10 s), chefes com premio
  (carro, mito, +1 andar na garagem), doacoes dos vencidos, corrida em dupla com um bot da sua crew.
- Most Wanted (Blacklist, chefe Razor) depois da LegendsDC.
- Precisa ter crew para desafiar (crew desde o nivel 1); vagas da crew (3, +3 por chefe); bot de nivel maior recusa
  entrar; amigo sem crew entra na sua; metade do mito dos membros para o lider; `!!escada` mostra o progresso.

Os numeros do arquivo (tempo, premio, vagas, lados das crews) podem ser mudados no `mod_escada_de_crews.json` da pasta do
`Rice.exe`; o menu tem a opcao de voltar eles ao padrao.
Ligar/desligar refaz as crews dos bots (classico <-> escada); bots que ja estao na sua crew continuam nela.
