# Roadmap do Retroboat

Duas etapas, nesta ordem. Cada entrega tem ID no [registro de patches](PATCHES.md) e é
classificada pelos critérios do [FORK.md](FORK.md) antes de começar.

## Etapa 1: cloud-first

Jogos, saves, memory cards e configurações na nuvem, no modelo do Steam: a nuvem é a fonte da
verdade e tudo roda localmente. O transporte é o app Google Drive para computador (`G:\`), sem API
e sem login próprio. Desenho completo em [design/cloud-first.md](design/cloud-first.md).

| Marco | Entrega | Patches | Status |
|---|---|---|---|
| M0 | Fundações: build, provedor de pasta sobre o Google Drive para computador, verificação de integridade | RB-006 | base pronta; falta empacotar no build |
| M1 | Biblioteca isolada: pasta de jogos própria, metadados fora dela | RB-003 | base pronta; falta teste com o ES real e empacotar |
| M2 | Biblioteca na nuvem: instalar, fila, desinstalar, verificar | RB-004 | fase 1 pronta: jogos da nuvem no ES e download ao jogar |
| M3 | Saves e memory cards na nuvem, com conflito e histórico | RB-009 | não iniciado |
| M4 | Perfil e configurações na nuvem, PC novo em poucos passos | RB-005 | contas do ES (ScreenScraper, IGDB, RetroAchievements) prontas |
| M5 | Interface nativa de nuvem no ES (fork e build do ES) | RB-010, RB-011 | build do ES pronto no GitHub Actions; falta o RB-010 |

M0 e M1 podem andar em paralelo. Código em [app/](app/README.md).

**Testes com PS2 primeiro** (decisão de 24/10/2026). Todos os marcos são validados primeiro com
jogos de PS2. Plano e pré-requisitos (jogos válidos, BIOS do PS2, RetroBat instalado) na
[seção 9 do cloud-first](design/cloud-first.md#9-testes-com-ps2-primeiro).

### Estado em 24/10/2026

- **Pronto e testado** (42 testes automáticos, mais testes manuais no `G:\` e numa instalação simulada):
  - CLI `retroboat-cli` com `where`, `ls`, `verify`, `install` e `uninstall` sobre a pasta do Drive;
  - instalação retomando depois de o processo ser derrubado (testado no Drive real, a cerca de 6 a 9 MB/s);
  - verificação rápida, que apontou os 5 ISOs zerados do Drive em cerca de 11 s por jogo;
  - biblioteca isolada (overlay, `roms=`, gamelists locais) e lançador `Retroboat.exe`.
- **Validado com o ES real** (release `continuous-master` de 22/10/2026 + emulatorLauncher `continuous`):
  - o ES listou os jogos da pasta nova e ignorou os da pasta padrão;
  - o sistema `gw` (pasta `gameandwatch`) funcionou;
  - o gamelist movido foi usado, com nome e favorito preservados;
  - um favorito alterado pela API foi gravado, ao sair, em `.emulationstation\gamelists\snes`;
  - nenhum arquivo além dos jogos foi criado ou alterado na pasta de jogos.
- **Build da nossa versão:** o RetroBuild (opção 1) gera `build\`, e o
  `retroboat/tools/package-app.sh` coloca o Retroboat em `build\retroboat\`.
- **Biblioteca na nuvem, fase 1 (RB-004):**
  - todo jogo do Drive aparece no ES como marcador, com "Na nuvem · 3,0 GB · baixa na primeira vez
    que você jogar" na descrição;
  - ao abrir, o `retroboat-launch` baixa o jogo numa tela cheia com progresso (B ou Esc cancela) e só
    então chama o emulador;
  - um ZIP com um disco dentro é extraído uma vez, direto do Drive, com conferência de CRC;
  - jogo zerado na nuvem é marcado como corrompido e explicado no ES;
  - testado no Drive real:
    - os 23 jogos de PS2 viraram marcadores em 3 s e o ES real listou todos com a descrição;
    - o Clock Tower 3 foi extraído do ZIP (4,29 GB, CRC ok) em 74 s;
    - na abertura seguinte, o jogo passou a "instalado" e a descrição saiu.
  - **Ainda não testado na tela:** a janela de download do `retroboat-launch` (só pelos testes
    automáticos do instalador). Falta abrir um jogo da nuvem pelo ES; isso precisa da BIOS do PS2
    para o jogo abrir depois do download.
- **BIOS na nuvem (RB-013, adiantado do M4):**
  - organização, sincronização nos dois sentidos e status validado pelo conteúdo;
  - retorno na janela do lançador e por notificação no ES;
  - testado no Drive real: a BIOS de PS2 enviada estava zerada e foi barrada com aviso.
- **Falta no M0/M1:**
  - atalho e instalador apontando para o `Retroboat.exe`.

### Primeiros ajustes de UI estilo Steam (adiantados da etapa 2)

Desenho e decisão em [design/conteudo-e-ui-steam.md](design/conteudo-e-ui-steam.md):

- **Conteúdo na nuvem (RB-015):** sem mudar o upstream.
- **Lista esmaecida, ícone de nuvem e "BAIXAR" na ajuda (RB-010):** mudança nativa no ES, que
  funciona em qualquer tema.
- **Status Baixar/Jogar nos detalhes (RB-014):** no tema Retroboat, derivado do Carbon.

Depende do fork e do build do ES (RB-011).

### Estado em 25/10/2026

- **Conteúdo na nuvem (RB-015), pronto e testado** (71 testes automáticos):
  - a mídia que o scraper grava na pasta de jogos vai para `.emulationstation\media` (fecha o 1c);
  - nome, descrição, capa e demais campos de um PC aparecem nos outros antes do download;
  - vídeos e manuais só com `[Cloud] Videos=1` / `Manuals=1` no `retroboat.ini`;
  - validado com o ES real: a capa movida aparece e o ES mantém o caminho `~/...` ao salvar.
- **Build do ES (RB-011), pronto:**
  - fork [Procopoles/retroboat-emulationstation](https://github.com/Procopoles/retroboat-emulationstation)
    (clone em `..\retroboat-emulationstation`), com workflow do GitHub Actions e script de build
    que gera o mesmo pacote do RetroBat oficial;
  - cada push no `master` compila em cerca de 5 minutos e publica no release `continuous-master`;
    o primeiro (`42.2026.1025.0456-retroboat-8592101`) tem os mesmos arquivos do oficial e abre o
    RetroBat com o Carbon;
  - compilado também aqui com o Visual Studio 2026, o que exigiu uma correção de uma linha (RB-016);
  - **falta:** o build oficial traz credenciais de API compiladas (ScreenScraper, TheGamesDB, HfsDB,
    RetroAchievements). Precisamos das nossas, como *secrets* do fork, antes de trocar o ES do
    `build.ini`.
- **Tema (RB-014), primeira versão:** no fork
  [Procopoles/es-theme-carbon](https://github.com/Procopoles/es-theme-carbon) (clone em
  `..\es-theme-carbon`), o cabeçalho mostra "Na nuvem · BAIXAR" (azul) ou "Instalado · JOGAR"
  (verde) para o jogo selecionado, em todas as visões do Carbon. Testado com o nosso ES nas visões
  Icons e detailed. O `build.ini` já usa o fork (`theme_path`), então o próximo build do
  RetroBuild sai com ele.

## Etapa 2: redesign visual e de UX

Começa depois da etapa 1. Aproveita o fork e o build do ES montados no M5. A primeira escolha de
desenho será onde cada mudança visual mora:

- **tema novo:** C0, sem conflito com o upstream;
- **mudança de comportamento no ES:** C1 a C3, no fork do ES.
