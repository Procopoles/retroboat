# RB-023 — Menu EMULADORES e consoles ativos

Pedido de 09/11/2026: "como nossa aplicação está gerenciando os emuladores, precisamos de uma parte no menu para o
gerenciamento dos emuladores, apenas opções principais, mostra opções de emuladores de consoles que estão ativos". E:
"abstraia essa parte do código", porque várias opções do app dependem dos consoles ativos.

Vem logo depois do RB-022 (o Retroboat escolhe o emulador padrão de um sistema; PS1: SwanStation). O resto do plano de
emuladores (versão fixa testada, download junto com o jogo, aviso quando falta) continua fora: o download segue sendo o do
RetroBat, no primeiro jogo.

## 1. Console ativo

**Um console é ativo quando tem pelo menos um jogo disponível: na biblioteca do Drive ou na pasta do console neste PC.**
Jogo da nuvem que ainda não foi baixado conta (ele tem marcador). Pasta vazia, só com `gamelist.xml` ou `_info.txt`, não.

A mesma regra vale nos dois lados do app, cada um com uma peça só para isso:

| Lado | Peça | Usa hoje |
|---|---|---|
| App (C#) | `Retroboat.Library.ActiveSystems` | aviso de BIOS só de console com jogo (`StartupSync`) |
| ES (fork, C++) | `Retroboat::ActiveSystems` (`es-app/src/retroboat/ActiveSystems.*`) | menu EMULADORES; achar o sistema de uma pasta (status ao vivo do download, `RetroboatCloud.cpp`) |

No ES, os sistemas carregados já são os que têm jogo (o ES ignora sistema vazio, e os marcadores contam), mas a peça confere
assim mesmo: com a opção "carregar sistemas vazios" do ES ligada, eles também entram na lista do ES. Coleções e o sistema de
grupo (que só junta outros) não são consoles.

Daqui para frente, toda opção que depende de "consoles com jogos" usa uma dessas duas peças, em vez de olhar pastas por conta
própria.

## 2. Menu EMULADORES

- **Onde:** menu principal do ES, logo abaixo de NUVEM, só com o Retroboat instalado (como NUVEM). Ícone próprio da fonte do
  ES, como a nuvem da NUVEM: cubos (U+F1B3). O `iconGames` do tema seria o mesmo de AJUSTES DE JOGO no PlayStation-X, e a
  fonte do ES não tem o ícone de chip. A entrada com ícone da fonte virou uma peça só (`Retroboat::Menu::addEntry`,
  `es-app/src/retroboat/RetroboatMenu.*`), usada pelas duas entradas; ela diminui um ícone mais largo que a caixa.
- **O que mostra:** uma linha por console ativo, na ordem do ES. À esquerda, o nome do console e, embaixo, o estado do
  emulador escolhido: "Instalado" ou "O RetroBat baixa no primeiro jogo". À direita, a lista dos emuladores que o RetroBat
  oferece para aquele console, numa lista só (emulador e core juntos: SWANSTATION, BEETLE PSX HW, DUCKSTATION...). O padrão
  vem marcado "(PADRÃO)".
- **Só as opções principais:** só a escolha do emulador. As dezenas de ajustes por sistema continuam no menu do próprio ES.
- **Console com um emulador só:** aparece igual, com a lista de uma opção, para mostrar qual é e se já está instalado.

## 3. Onde a escolha fica

Nas opções do próprio ES, `<sistema>.emulator` e `<sistema>.core` no `es_settings.cfg`, as mesmas do menu avançado do ES por
sistema. Assim:

- as duas telas mostram sempre a mesma escolha;
- vale no próximo jogo, sem recarregar nada;
- vai para a nuvem e chega aos outros PCs pelo RB-005 (opções do ES por sistema), como qualquer opção do ES;
- escolher o padrão grava vazio (AUTO). O console segue o padrão do Retroboat (RB-022) ou do RetroBat, inclusive se ele mudar
  numa versão nova.

A escolha por jogo (menu do jogo no ES) continua valendo por cima.

## 4. Instalado ou não

O ES olha os arquivos que o RetroBat usa: o core em `emulators\retroarch\cores\<core>_libretro.dll` (com o RetroArch em
`emulators\retroarch`) ou a pasta `emulators\<emulador>` com algum arquivo. É só informação: quem baixa continua sendo o
RetroBat, no primeiro jogo (foi assim com o PCSX2 e com o Beetle PSX HW).

## 5. Fora deste passo

Versão fixa por emulador, download junto com o jogo, aviso quando falta e tamanho do download: são o plano de gerenciamento de
emuladores, que ainda não começou.
