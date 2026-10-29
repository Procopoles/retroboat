# Registro de patches do Retroboat

Toda mudança do fork em relação ao upstream tem uma linha aqui. Critérios em [FORK.md](FORK.md#3-classificação-de-cada-mudança).

Status: `candidato` (identificado, não feito) · `em andamento` · `ativo` (em `main`) ·
`enviado` (PR aberto no upstream) · `absorvido` (o upstream corrigiu; o nosso foi removido) · `descartado`.

| ID | Mudança | Repo | Camada | Calor | Risco de merge | Upstreamável | Status |
|---|---|---|---|---|---|---|---|
| RB-001 | Pasta `retroboat/` com guia, registro e scripts de sincronização | retrobat | C0 | — | nenhum | não | em andamento |
| RB-002 | ES ignora todos os overlays seguintes quando um `es_systems_*.cfg` é inválido | ES (Batocera) | C1 | média | baixo | sim | candidato |
| RB-003 | Biblioteca isolada: pasta de jogos escolhida pelo usuário, gamelists e mídia fora dela | retrobat (+ componente novo) | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-004 | Biblioteca na nuvem: instalar, fila, desinstalar, verificar (integração C0 com o ES) | retroboat/app | C0 | — | nenhum (semântico: médio) | não | em andamento |
| RB-005 | Perfil e configurações na nuvem: chaves portáveis, controles, metadados, estatísticas | retroboat-cloud | C0 | — | nenhum (semântico: médio) | não | candidato |
| RB-006 | Base da nuvem: provedor de pasta (Google Drive para computador, NAS), verificação de integridade, lançador, `retroboat-launch` | retroboat/app | C0 | — | nenhum | não | em andamento |
| RB-007 | ExoDOS cria atalhos em `retrobat\roms` ignorando a chave `roms=` | emulatorlauncher | C1 | média | baixo | sim | candidato |
| RB-008 | Opção para o `retrobat.exe` esperar um `AppLauncher` terminar antes de abrir o ES | retrobat-executable | C1 | média | baixo | sim | candidato |
| RB-009 | Saves e memory cards na nuvem: sync por jogo, conflito, offline, snapshots | retroboat-cloud | C0 | — | nenhum (semântico: médio) | não | candidato |
| RB-010 | Interface nativa de nuvem no ES: status, instalar/desinstalar, tela de downloads | ES (Batocera) | C2 | ALTA/média | alto | talvez (só se genérico) | candidato |
| RB-011 | Build Windows do ES no nosso fork (GitHub Actions) | ES (Batocera) | C0 | — | nenhum | não | em andamento |
| RB-012 | Sistema Ghostship cadastrado com o nome "starship" (nome duplicado no `es_systems.cfg`) | retrobat | C1 | ALTA | médio | sim | candidato |
| RB-013 | BIOS na nuvem: organizar, sincronizar e mostrar status (lançador + notificação no ES) | retroboat/app | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-014 | Tema Retroboat (fork do Carbon): status Baixar/Jogar nos detalhes; base do tema Steam | es-theme-carbon (fork) + retrobat (`build.ini`) | C1 no tema e no `build.ini` | baixa (a linha `theme_path`) | baixo | não | em andamento |
| RB-015 | Conteúdo dos jogos na nuvem: metadados e mídia compartilhados, mídia fora da pasta de jogos | retroboat/app | C0 | — | nenhum (semântico: médio) | não | em andamento |
| RB-016 | ES não compila com o Visual Studio 2026 (MSVC 14.50): falta `#include <chrono>` em `Log.cpp` | ES (Batocera) | C1 | média | baixo | sim | em andamento |

---

## RB-001 — Infraestrutura do fork

- **Arquivos:** `retroboat/**` (todos novos).
- **Por quê:** dar ao fork um lugar próprio para documentação e ferramentas, sem tocar em nada do upstream.
- **Risco semântico:** nenhum. O RetroBuild só copia `system/` e os `*.txt` da raiz, então a pasta não entra na distribuição.
- **Configuração local (não versionada, refazer em cada clone):**

  ```bash
  git remote add upstream https://github.com/RetroBat-Official/retrobat.git
  git remote set-url --push upstream DISABLED-read-only
  git config rerere.enabled true
  git config rerere.autoupdate true
  git config merge.conflictstyle zdiff3
  ```

## RB-002 — Um overlay inválido desliga os outros (candidato)

- **Onde:** `es-app/src/SystemData.cpp`, `SystemData::loadAdditionnalConfig`, upstream `batocera-linux/batocera-emulationstation`.
- **Problema:** quando um `es_systems_*.cfg` não é XML válido, ou não tem `<systemList>`, a função dá
  `return` em vez de `continue`. Todos os overlays que viriam depois, nas duas pastas, são ignorados
  sem aviso. A função equivalente de `es_features_*.cfg` (`CustomFeatures.cpp`) já usa `continue`.
- **Por que importa para nós:** a regra 1 do FORK.md coloca as nossas mudanças de sistema num
  overlay. Um arquivo `es_systems_*.cfg` quebrado do usuário, lido antes do nosso, desligaria em
  silêncio todas as nossas correções.
- **Correção:** trocar os dois `return;` por `continue;` (2 linhas).
- **Caminho recomendado:** PR direto no Batocera. Como não precisa de build próprio do ES, é só
  esperar o merge e o espelho do RetroBat. Não precisa virar patch do fork.

## RB-003 a RB-006, RB-009 a RB-011 — Biblioteca isolada e cloud-first

Desenho em [design/cloud-first.md](design/cloud-first.md) (nuvem) e
[design/biblioteca-e-nuvem.md](design/biblioteca-e-nuvem.md) (biblioteca isolada e pontos de
extensão). O cloud-first trocou o rclone por um motor de sincronização próprio, que usa como
transporte o app Google Drive para computador (sem API e sem login nosso). Resumo da classificação:

- **RB-003 (biblioteca isolada):** C0. Usa overlay de `es_systems` gerado a cada início, a chave
  `roms=` do emulatorLauncher e a pasta alternativa de gamelists que o ES já suporta. Risco
  semântico baixo: como o overlay é regenerado, sistemas novos do upstream entram sozinhos.
  Validado em 24/10/2026 com o ES real: jogos lidos da pasta nova, gamelist local usado e regravado
  ao sair, nada escrito na pasta de jogos.
- **RB-004 (biblioteca na nuvem):** C0 na fase 1 (arquivos marcadores, coleção "☁ Na nuvem",
  notificações pela API HTTP do ES, download com progresso no `retroboat-launch`). A fase 2 (selo e
  "Baixar" nativos no ES) é C2 no ES e só deve começar depois do build próprio do ES.
- **RB-005 (perfil na nuvem):** C0. Risco semântico médio: chaves novas do `es_settings.cfg` que
  dependem da máquina precisam entrar na lista de chaves locais, senão viajam de um PC para o outro.
- **RB-006 (base):** C0. Componente novo em repositório próprio (`retroboat-cloud`); o build
  baixa os releases dele.
- **RB-009 (saves e memory cards):** C0. Risco semântico médio: se o upstream mudar onde um
  emulador grava os saves, a regra correspondente no catálogo de saves fica errada sem conflito de
  merge. O `upstream-check.sh` deve alertar quando um gerador mudar caminhos de `saves`.
  Memory cards do PS2 funcionam como no console: cartões com nome, escolhidos por slot e por jogo,
  colocados nos arquivos fixos `Mcd001.ps2`/`Mcd002.ps2` que o emulatorLauncher configura no PCSX2.
  Isso depende do bloco de memory cards de `Pcsx2.Generator.cs` (`SetupConfigurationQT`), que
  entra na lista de vigilância.
- **RB-010 (ES nativo):** C2 em arquivos quentes do ES (`FileData.cpp`, `GuiGameOptions.cpp`,
  bindings de tema). Só começa depois do RB-011. Lê a API local do `retroboat-cloud`, então a
  lógica de nuvem continua fora do ES.
- **RB-011 (build do ES):** C0. Arquivo de workflow novo no nosso fork do ES. Também é pré-requisito
  do redesign (etapa 2).

**Implementação (24/10/2026):** o código fica em [app/](app/README.md), dentro deste repositório,
e não num repositório separado como o cloud-first propunha. A pasta não é tocada pelo upstream nem
empacotada pelo RetroBuild, então o risco de merge continua zero, e o build e a documentação ficam
num lugar só. Se um dia precisar de releases independentes, `git subtree split` extrai a pasta com
o histórico.

## RB-012 — Ghostship cadastrado como "starship" (candidato)

- **Onde:** `system/templates/emulationstation/es_systems.cfg` (calor ALTA), commit upstream `0c78f16` ("Add ghostship", 27/03/2026).
- **Problema:** o sistema da pasta `ghostship` (fullname "Ghostship") tem `<name>starship</name>`,
  igual ao sistema Starship. O ES não junta nomes repetidos: os dois sistemas dividem o mesmo
  gamelist local, tema e opções, e o `es_features.cfg` já tem as opções em `emulator name="ghostship"`.
- **Efeito no Retroboat:** um overlay só consegue alterar o primeiro sistema com aquele nome. A
  RB-003 detecta, avisa e deixa o Starship na pasta padrão.
- **Correção:** trocar o `<name>` para `ghostship` (1 linha). **Caminho recomendado:** PR no
  upstream. Editar aqui seria C1 no arquivo mais quente do repositório.

## RB-004 — Biblioteca na nuvem (fase 1)

- **Onde:** `retroboat/app` (`Cloud\CloudLibrarySync`, `CloudIndex`, `OnDemandInstaller`,
  `ZipCatalog`, projeto `Retroboat.Launch`). No RetroBat, só o overlay gerado
  `es_systems_retroboat.cfg`, que agora também troca o `<command>` dos sistemas com jogos na nuvem.
- **Como funciona:** marcador vazio com o nome final do jogo + descrição no gamelist local.
  `retroboat-launch` na frente do emulatorLauncher baixa o jogo quando o `-rom` é um marcador.
- **Risco semântico médio:**
  - Depende do formato do `<command>` do `es_systems.cfg`: o comando original é envolvido como está,
    e o `-rom` é procurado nos argumentos.
  - Depende de o ES aceitar arquivo vazio como jogo, o que foi verificado no ES de 22/10/2026.
  - Se o upstream passar a validar o tamanho das ROMs, os marcadores somem da lista sem conflito de merge.
  - Os três pontos entram na lista de vigilância.

## RB-013 — BIOS na nuvem

- **Onde:** `retroboat/app` (`Bios\`, `Cloud\StartupSync`, `Es\EsApi`, lançador). Nada no upstream.
- **Desenho:** seção 4b do [cloud-first](design/cloud-first.md).
- **Risco semântico baixo:** o catálogo de BIOS copia a lista de nomes e a ordem de busca do
  `Pcsx2.Generator.cs` (`SetupConfigurationQT`). Se o upstream mudar essa lista ou a pasta
  `bios\pcsx2\bios`, o status fica errado sem conflito de merge. Esse trecho entra na lista de
  vigilância.

## RB-015 — Conteúdo dos jogos na nuvem

- **Onde:** `retroboat/app` (`Media\GameMedia`, `MediaRelocator`, `CloudMediaSync`,
  `Cloud\GameDescription`, `StartupSync`). Nada no upstream.
- **Desenho:** seção 1 de [design/conteudo-e-ui-steam.md](design/conteudo-e-ui-steam.md).
- **Como funciona:** ao abrir, o lançador move a mídia que o scraper gravou na pasta de jogos para
  `.emulationstation\media\<sistema>` (caminhos `~/...` no gamelist), envia para
  `retroboat\midia\<sistema>` no Drive os metadados e a mídia que este PC tem e os outros não, e
  preenche nos outros PCs só os campos vazios. Imagens sempre; vídeos e manuais com
  `[Cloud] Videos=1` / `Manuals=1` no `retroboat.ini`.
- **Validado em 25/10/2026** com o ES real: a capa movida para `.emulationstation\media` aparece
  (API `/systems/ps2/games/<id>/media/image`) e o ES mantém o caminho `~/...` ao salvar o gamelist.
- **Risco semântico médio:** depende da lista de campos de mídia do gamelist (`MetaData.cpp`) e de
  o ES resolver `~/` a partir da pasta dele. Campo de mídia novo no upstream fica fora da nuvem sem
  conflito de merge; os dois pontos entram na lista de vigilância.

## RB-011 — Build Windows do ES no nosso fork

- **Onde:** fork [Procopoles/retroboat-emulationstation](https://github.com/Procopoles/retroboat-emulationstation)
  (de `batocera-linux/batocera-emulationstation`), arquivos novos
  `.github/workflows/retroboat-windows.yml` e `retroboat/build-windows.ps1`. Clone local em
  `..\retroboat-emulationstation`, ao lado deste repositório.
- **Como funciona:** compila x64 Release com `-DRETROBAT=ON`, monta `EmulationStation-Win64.zip`
  com o mesmo conteúdo do zip oficial (executável, DLLs de `win32-dependencies`, plugins do VLC,
  `resources` com as traduções, `version.info`) e publica no release `continuous-master`, que é o
  formato que o RetroBuild baixa. Para usar: `build.ini: emulationstation_url` apontando para o
  release do fork.
- **Validado em 25/10/2026**, no build local (Visual Studio 2026 Build Tools) e no GitHub Actions
  (run 40163435118, cerca de 5 minutos): o pacote tem os mesmos 346 arquivos do oficial (só falta
  o `main_test.exe`, binário de teste) e o executável abre o RetroBat com o Carbon. O release
  `continuous-master` do fork já tem `EmulationStation-Win64.zip` e `version.info`.
- **Atenção, credenciais de API:** o ES oficial compila credenciais de desenvolvedor no executável
  (ScreenScraper, TheGamesDB, HfsDB, RetroAchievements). Sem elas, o nosso build não tem esses
  scrapers nem o menu do RetroAchievements. Precisamos das nossas, como *secrets* do fork; a lista
  está em `retroboat/README.md` do fork. Enquanto não houver, o `build.ini` deve continuar
  apontando para o ES oficial.
- **Risco:** nenhum de merge (arquivos novos). O CMake baixa as dependências de
  `batocera-emulationstation-win32-dependencies` na versão mais recente; se elas mudarem de
  estrutura, o empacotamento falha no CI, sem efeito silencioso.

## RB-016 — ES não compila com o Visual Studio 2026

- **Onde:** `es-core/src/Log.cpp` (5 alterações/ano, calor média), ES do Batocera.
- **Problema:** `Log::stream()` usa `std::chrono::system_clock` sem incluir `<chrono>`. Compilava
  porque outro cabeçalho da STL o trazia; no MSVC 14.50 não traz mais, e o build para em `es-core`.
- **Correção:** `#include <chrono>` (1 linha). **Upstreamável:** sim, é o caminho recomendado.
- **Classificação:** C1, calor média, risco baixo. Some quando o upstream absorver.

## RB-014 — Status Baixar/Jogar no tema

- **Onde:** fork [Procopoles/es-theme-carbon](https://github.com/Procopoles/es-theme-carbon) (de `RetroBat-Official/es-theme-carbon`). Arquivo novo
  `retroboat/status.xml` e uma linha de include no fim do `theme.xml` (calor baixa: nenhuma
  alteração no último ano). Clone local em `..\es-theme-carbon`.
- **Como funciona:** no cabeçalho, à direita, o jogo selecionado mostra "☁ Na nuvem" com o botão
  azul "BAIXAR", "✓ Instalado" com o botão verde "JOGAR", ou "Corrompido na nuvem" em vermelho. O
  cabeçalho é igual em todas as visões de jogos do Carbon, então um arquivo cobre Icons, Tiles,
  Boxes, Content, detailed e gamecarousel. O status vem da primeira linha da descrição, que o
  lançador escreve (`GameDescription`).
- **Validado em 25/10/2026** com o nosso build do ES nas visões Icons e detailed.
- **Risco semântico baixo:** depende do prefixo da descrição ("Na nuvem", "Corrompido na nuvem").
  Mudar esses textos em `GameDescription` exige mudar o tema junto. Com o RB-010, o tema passa a
  usar `{game:cloud}` e essa dependência some.
- **Entrada no build (25/10/2026):** `build.ini: theme_path` aponta para o fork (C1, 1 linha).
  O `build.ini` é quente (31 alterações/ano), mas quase sempre em `retrobat_version` e `branch`;
  a linha `theme_path` não muda desde o primeiro commit. O RetroBuild clona para a pasta
  `es-theme-carbon`, então quem já usa o Carbon não perde a configuração.
- **Risco semântico:** o fork precisa acompanhar o Carbon oficial. Se ficar parado, o RetroBat
  sai com um tema velho sem conflito de merge. A rotina de sincronização do FORK.md vale para
  ele também (`upstream-check.sh -C ../es-theme-carbon -b master`).

## RB-007 — ExoDOS ignora a pasta de jogos configurada (candidato)

- **Onde:** `emulatorLauncher/Generators/ExoDOS.Generator.cs:483` (6 alterações/ano), `RetroBat-Official/emulatorlauncher`.
- **Problema:** `CreateShortcuts` monta o caminho como `GetFullPath("retrobat") + "roms"` em vez de
  `GetFullPath("roms")`, então ignora `roms=` no `emulatorLauncher.cfg`. Com a RB-003, os atalhos
  do ExoDOS iriam para a pasta errada.
- **Correção:** uma linha. **Caminho recomendado:** PR no upstream.

## RB-008 — `AppLauncher` com espera (candidato)

- **Onde:** `RetroBat/RetroBat/EmulationStationLauncher.cs` (5 alterações/ano), `RetroBat-Official/retrobat-executable`.
- **Problema:** os apps do `AppLauncher` são abertos em paralelo, sem esperar. Não dá para rodar
  algo que precise terminar antes do ES abrir, como baixar o perfil da nuvem.
- **Correção:** um sufixo `-wait`, análogo ao `-nowindow` que já existe. **Caminho recomendado:**
  PR no upstream. Se for aceito, o `Retroboat.exe` deixa de ser necessário como lançador.

---

## Modelo para novas entradas

```markdown
## RB-NNN — Título curto

- **Onde:** repositório e arquivos (com o calor de cada um, de HOTSPOTS.md).
- **Problema:** o que o usuário vê / como reproduzir.
- **Correção:** o que muda, em uma ou duas frases.
- **Classificação:** camada C?, calor ?, risco de merge ?. Justifique se for C2 ou C3.
- **Risco semântico:** o que pode quebrar em silêncio num merge futuro (ex.: tags que o overlay sobrescreve).
- **Upstreamável:** sim/não; link do PR quando enviado.
- **Como testar:** passos curtos.
```
