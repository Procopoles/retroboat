# Registro de patches do Retroboat

Toda mudança do fork em relação ao upstream tem uma linha aqui. Critérios em [FORK.md](FORK.md#3-classificação-de-cada-mudança).

Status: `candidato` (identificado, não feito) · `em andamento` · `ativo` (em `main`) ·
`enviado` (PR aberto no upstream) · `absorvido` (o upstream corrigiu; o nosso foi removido) · `descartado`.

| ID | Mudança | Repo | Camada | Calor | Risco de merge | Upstreamável | Status |
|---|---|---|---|---|---|---|---|
| RB-001 | Pasta `retroboat/` com guia, registro e scripts de sincronização | retrobat | C0 | — | nenhum | não | em andamento |
| RB-002 | ES ignora todos os overlays seguintes quando um `es_systems_*.cfg` é inválido | ES (Batocera) | C1 | média | baixo | sim | candidato |
| RB-003 | Biblioteca isolada: pasta de jogos escolhida pelo usuário, gamelists e mídia fora dela | retrobat (+ componente novo) | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-004 | Biblioteca na nuvem: instalar, fila, desinstalar, verificar (integração C0 com o ES; menu do jogo no nosso ES) | retroboat/app e ES (Batocera) | C0 (+ 3 linhas de gancho no ES) | média (`GuiGameOptions.cpp`) | baixo (semântico: médio) | não | em andamento |
| RB-005 | Perfil e configurações na nuvem: contas, opções do ES, controles, configurações dos emuladores (todos), estatísticas | retroboat/app | C0 | — | nenhum (semântico: médio) | não | em andamento |
| RB-006 | Base da nuvem: provedor de pasta (Google Drive para computador, NAS), verificação de integridade, lançador, `retroboat-launch` | retroboat/app | C0 | — | nenhum | não | em andamento |
| RB-007 | ExoDOS cria atalhos em `retrobat\roms` ignorando a chave `roms=` | emulatorlauncher | C1 | média | baixo | sim | candidato |
| RB-008 | Opção para o `retrobat.exe` esperar um `AppLauncher` terminar antes de abrir o ES | retrobat-executable | C1 | média | baixo | sim | candidato |
| RB-009 | Saves e memory cards na nuvem: sync por jogo, conflito, offline, snapshots (PS2 com regra própria; os outros sistemas pela regra genérica) | retroboat/app | C0 | — | nenhum (semântico: médio) | não | em andamento |
| RB-010 | Nuvem nativa no ES: BAIXAR sem abrir o jogo, região do cabeçalho, status ao vivo, lista esmaecida | ES (Batocera), retroboat/app e `status.xml` dos temas | C1 (32 linhas de gancho em 10 arquivos) | média | médio | não | em andamento |
| RB-011 | Build Windows do ES no nosso fork (GitHub Actions) | ES (Batocera) | C0 | — | nenhum | não | em andamento |
| RB-012 | Sistema Ghostship cadastrado com o nome "starship" (nome duplicado no `es_systems.cfg`) | retrobat | C1 | ALTA | médio | sim | candidato |
| RB-013 | BIOS na nuvem: organizar, sincronizar e mostrar status (lançador + notificação no ES) | retroboat/app | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-014 | Tema Retroboat (fork do Carbon): status Baixar/Jogar nos detalhes; base do tema Steam | es-theme-carbon (fork) + retrobat (`build.ini`) | C1 no tema e no `build.ini` | baixa (a linha `theme_path`) | baixo | não | em andamento |
| RB-015 | Conteúdo dos jogos na nuvem: metadados e mídia compartilhados, mídia fora da pasta de jogos | retroboat/app | C0 | — | nenhum (semântico: médio) | não | em andamento |
| RB-016 | ES não compila com o Visual Studio 2026 (MSVC 14.50): falta `#include <chrono>` em `Log.cpp` | ES (Batocera) | C1 | média | baixo | sim | em andamento |
| RB-017 | Tema PlayStation-X com o status do Retroboat (Baixar/Jogar, nuvem, Drive fechado) | es-theme-PlayStation-X (clone local) | C1 no tema (2 linhas no `theme.xml`) | média | baixo (semântico: médio) | não | em andamento |
| RB-018 | Aviso falso de BIOS ao abrir jogo: o lançador desliga a checagem do ES uma vez | retroboat/app | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-019 | Google Drive fechado: perguntar, abrir e acompanhar com o ES aberto | retroboat/app e `status.xml` dos temas (RB-014, RB-017) | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-020 | Downloads em segundo plano: fila, serviço e região de ícones que gira no cabeçalho | retroboat/app e `status.xml` dos temas (RB-014, RB-017) | C0 | — | nenhum (semântico: médio) | não | em andamento |

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
  ao sair, nada escrito na pasta de jogos. Desde 09/11/2026 a pasta de jogos escolhida não ganha mais
  uma pasta por sistema: as pastas de console ficam só no Drive, e a de um console surge com o primeiro
  jogo (download ou marcador). O ES pula um sistema sem pasta (registra "path does not exist" no
  `es_log.txt`), como pula um sem jogos.
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
  Memory cards do PS2: o emulador gerencia os cartões e o Retroboat sincroniza todos os da pasta que
  o emulatorLauncher configura (decisão de 25/10/2026; detalhes na seção RB-009 abaixo).
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
- **Testado em 25/10/2026** com os 8 ZIPs de PS2 novos do Drive (todos com um ISO só, deflate):
  janela de download em tela cheia com progresso e tempo restante; Shadow of the Colossus e Black
  extraídos uma vez, com CRC conferido; Esc no God of War apaga o parcial e volta sem abrir o
  emulador; a segunda abertura de um jogo instalado passa direto (0,6 s) e devolve o código de saída.
- **Correções do teste:**
  - pastas e arquivos que começam com `_` (ex.: `_controle`, com as planilhas de quem organiza a
    biblioteca) não são mais jogos;
  - um jogo trocado no Drive por outro formato com o mesmo disco (`.iso` → `.zip`) perdia o
    marcador: a limpeza dos jogos que saíram apagava o marcador do jogo novo.

## RB-004 — Biblioteca na nuvem (fase 2: menu do jogo no ES)

- **Onde:** fork `retroboat-emulationstation`: os nossos `es-app/src/retroboat/RetroboatCloud.cpp` e `.h`, e 3 linhas marcadas
  `RETROBOAT RB-004` em `GuiGameOptions.cpp`. No app: `Core\Cloud\LocalCopy`, `OnDemandInstaller.FindByRom` e
  `retroboat-launch --desinstalar/--verificar/--reparar` (`Launch\LocalCopyFlow`).
- **Pedido (08/11/2026):** seguir com o M2: desinstalar e verificar pelo ES. O selo de status na lista e na grade já tinha vindo
  com o RB-010.
- **Como funciona:** no menu do jogo (segurar A), um jogo da biblioteca na nuvem ganha entradas próprias:
  - fora deste PC, no lugar de JOGAR: BAIXAR com o tamanho na nuvem, CANCELAR DOWNLOAD com o andamento (sem perguntar de novo)
    ou INDISPONÍVEL com o motivo;
  - neste PC, no lugar de EXCLUIR JOGO: VERIFICAR INTEGRIDADE e DESINSTALAR com o espaço ocupado. O EXCLUIR JOGO sai dos jogos
    da nuvem: apagaria a mídia e a entrada do gamelist, e o marcador voltaria na abertura seguinte.
  - **Desinstalar** pergunta (NÃO com o foco e no B), apaga a cópia (e um `.retroboat-part`) e põe o marcador de volta: o jogo
    vira BAIXAR na hora, com as informações e a mídia. Os saves ficam. Antes, confere no Drive que o jogo continua na nuvem e que
    a cópia de lá não está zerada; com o Drive fechado, faz a mesma pergunta do BAIXAR. Sem o Drive, ou sem uma cópia boa na
    nuvem, não apaga e diz o motivo.
  - **Verificar** mostra "VERIFICANDO <JOGO>" com a porcentagem e depois o resultado. Disco extraído de ZIP: lê a cópia inteira e
    confere o CRC-32 do ZIP (do Drive sai só o diretório do ZIP). Outro arquivo: o tamanho e 32 blocos de 256 KB comparados byte a
    byte com a nuvem, do começo ao fim (8 MB lidos do Drive). Jogo em pasta: a lista de arquivos, os tamanhos e o maior arquivo por
    blocos; arquivos a mais aqui não contam (alguns emuladores gravam na pasta do jogo). Com o Drive fechado, não pergunta: confere
    só se a cópia local não está zerada e avisa que não comparou.
  - **Danificado:** REPARAR (com o foco) ou AGORA NÃO (no B). REPARAR desinstala e põe o jogo na fila, e ele aparece como BAIXANDO
    na hora.
  - O C# deixa o texto para o usuário em `estado\resposta-<pedido>.txt` e o andamento em `estado\verificando`. Deu certo, uma
    notificação; não deu, uma mensagem com o motivo.
- **Decisões:**
  - **Verificar não baixa o jogo de novo.** Sem um manifesto de hashes, como o da Steam, comparar por blocos é o que dá para fazer
    sem ler o jogo inteiro do Drive. Um byte trocado fora dos blocos só é achado nos discos de ZIP, pelo CRC.
  - **Reparar pergunta,** em vez de agir sozinho como a Steam: uma cópia diferente da nuvem pode ser uma tradução ou um patch de
    propósito.
  - **DESINSTALAR só na interface completa do ES,** como o EXCLUIR JOGO; VERIFICAR em todas.
- **Classificação:** C1, risco de merge baixo: 3 linhas em `GuiGameOptions.cpp` (6 alterações/ano): o include, um `if` antes do
  JOGAR e um antes do EXCLUIR JOGO, cada um valendo para o comando seguinte.
- **Risco semântico:** médio. Depende de o JOGAR (o if/else do SAVESTATES) e o EXCLUIR JOGO continuarem sendo um comando só logo
  depois dos nossos `if`. Se o upstream mudar isso, o JOGAR de um jogo da nuvem volta ao menu, mas cai no BAIXAR pelo gancho do
  `ViewController::launch`.
- **Upstreamável:** não.
- **Testado em 08/11/2026** com 8 testes automáticos novos (244 no total) e com o nosso ES e o `retroboat-launch` de verdade,
  numa cópia isolada (nuvem em pasta local, tema PlayStation-X, janela 1280x720 fora da tela):
  - jogo instalado: o menu mostra VERIFICAR INTEGRIDADE e DESINSTALAR ("Ocupa 150 MB neste PC"), sem EXCLUIR JOGO;
  - verificar: a cópia boa confere (150 MB em cerca de 1 s); um ISO com 4 bytes trocados perto do fim é achado ("perto de
    79,8 MB"); um disco de ZIP de 2 GB é conferido pelo CRC em cerca de 5 s, com a porcentagem na tela;
  - REPARAR: desinstala, entra na fila, baixa e volta a JOGAR, e a verificação seguinte confere;
  - desinstalar: o B mantém o jogo; DESINSTALAR mostra "Alfa Bom (USA) desinstalado · 150 MB livres" e o jogo vira BAIXAR na
    hora;
  - jogo fora do PC: BAIXAR ("Na nuvem · 150 MB") baixa; com a nuvem fora do ar, CANCELAR DOWNLOAD ("Aguardando o Google
    Drive") tira da fila; corrompido: INDISPONÍVEL com o motivo, e a explicação ao apertar;
  - sem a nuvem: desinstalar não apaga e explica; verificar confere só a cópia local e avisa.
- **Corrigido no teste:** a porcentagem não aparecia, porque o `readAllText` do ES procura um BOM e devolve vazio num arquivo
  de menos de 3 bytes; o motivo de corrompido não cabia numa linha.
- **Efeito conhecido:** durante o VERIFICANDO, a barra de ajuda continua com a do menu que fechou (a tela de espera do ES não
  tem ajuda própria). A entrada é ignorada até o fim.

## RB-005 — Contas do ES na nuvem (primeira parte do perfil)

- **Onde:** `retroboat/app` (`Accounts\AccountSync`, `Accounts\EsSettings`, `Cloud\StartupSync`).
  Nada no upstream.
- **Como funciona:** os logins que o ES guarda no `es_settings.cfg` (ScreenScraper, IGDB,
  RetroAchievements) vão para `retroboat\contas\<PC>.xml` no Drive. Ao abrir, um PC sem login
  recebe o da nuvem; um login digitado ou trocado no ES vai para a nuvem e chega aos outros PCs na
  próxima abertura deles. Usuário e senha de cada serviço viajam juntos. Para saber de que lado
  veio a diferença, cada PC guarda só o hash do que sincronizou por último
  (`retroboat\contas-sync.xml`). Com o ES aberto, não sincroniza, porque o ES regrava o
  `es_settings.cfg` ao sair.
- **Segurança:** os valores vão em Base64 no Drive, só para não aparecerem na pré-visualização e na
  busca. Não é criptografia: quem tem acesso à pasta do Drive consegue ler, assim como o
  `es_settings.cfg` já guarda a senha em texto puro no PC. Uma criptografia de verdade precisaria de
  uma chave que o usuário digita em cada PC.
- **Validado em 25/10/2026** com o ES real: o lançador gravou o login no `es_settings.cfg`, o ES
  abriu sem erro e o login continuou lá depois de fechar.
- **Risco semântico baixo:** depende dos nomes das chaves no `es_settings.cfg`
  (`GuiScraperStart::loadAccountsPage`, `GuiRetroAchievementsSettings`). Chave renomeada no
  upstream deixa de sincronizar sem conflito de merge; os dois arquivos entram na lista de vigilância.

## RB-005 — Configurações do ES, dos controles e dos emuladores na nuvem (08/11/2026)

- **Onde:** `retroboat/app` (`Config\`, `Cloud\StartupSync`, `ConfigFlow` no `retroboat-launch`, lançador, CLI
  `config status|sync`). Nada no upstream.
- **Escopo (decisão de 08/11/2026):** as opções do ES **e** os arquivos do próprio emulador, começando pelo PCSX2.
  Substitui a ideia de 01/11 de deixar os `.ini` dos emuladores no PC. Os emuladores (programas) continuam por PC.
- **O que viaja:**
  - **opções do ES** (`es_settings.cfg`): globais (`global.*`), por sistema (`ps2.*`) e por jogo (`ps2["Jogo.iso"].*`), que o
    emulatorLauncher aplica no emulador a cada jogo, e as preferências do ES;
  - **controles** (`es_input.cfg`): um controle por entrada, pelo GUID, como o ES do Windows;
  - **PCSX2** (`emulators\pcsx2`): `inis\PCSX2.ini` chave a chave, `gamesettings\*.ini` (ajustes por jogo) e
    `inputprofiles\*.ini`, inteiros;
  - **os outros emuladores** (`AutoEmulatorRules`, mesmo dia): regra genérica para todo emulador baixado em
    `emulators\`, sem lista. `.ini`, `.cfg`, `.conf`, `.opt` e `.rmp` vão chave a chave (as linhas que não são
    chave, como códigos de cheat, vão por seção); `.json`, `.xml`, `.yml` e `.toml` vão inteiros, só sem caminho
    e sem login. Ficam de fora as pastas de BIOS, firmware, chaves, NAND, saves, cache, shaders, jogos instalados e o
    que vem com o emulador. Uma chave de versão do formato separa PCs com versões diferentes do emulador. Exceções
    com armadilha própria ganham regra própria (`EmulatorConfigRules`), como o PCSX2.
- **O que fica no PC:** tela, monitor, placa de vídeo, áudio, caminhos absolutos, tema escolhido, controle de cada jogador,
  sessão e senhas (`EsSettingsRules`); no `PCSX2.ini`, pastas, lista de jogos, memory cards, BIOS escolhida, atualizador,
  rede, janelas, monitor, dispositivo de áudio e login do RetroAchievements (`EmulatorConfigRules.Pcsx2`); nos
  outros emuladores, chaves com caminho absoluto, monitor, janela, geometria, placa de vídeo, dispositivo, driver,
  "último", "recente", histórico, login, senha e token (`AutoEmulatorRules`).
- **Como funciona:** `ConfigSync` grava `retroboat\configuracoes\<fonte>\<PC>.xml` (cada PC só o seu) com a última versão
  de cada entrada; vale a mais nova entre os PCs. O estado da última sincronização (`retroboat\configuracoes-sync`) separa
  "mudou aqui" de "mudou na nuvem"; mudou nos dois, vale a mudança mais nova. No primeiro uso num PC, e quando um arquivo
  já sincronizado some (apagado, emulador reinstalado), vale a nuvem. Antes de gravar, cópia em
  `retroboat\historico\configuracoes` (10 últimas).
- **Quando:** ao abrir o `Retroboat.exe`; antes do jogo (o emulador recebe e envia) e depois (o emulador envia); com o ES
  aberto, as opções e os controles só são enviados, em segundo plano durante o jogo; quando o ES fecha, recebem também.
- **Versão do `PCSX2.ini`:** o PCSX2 zera o arquivo (e pergunta) se `[UI] SettingsVersion` falta ou é de outra versão
  (`VMManager::Internal::CheckSettingsVersion`, versão 1), e o emulatorLauncher não grava essa chave. As chaves só vão e
  vêm entre PCs com a mesma versão, e o Retroboat grava a versão ao criar o arquivo.
- **Testado em 08/11/2026:** 19 testes automáticos (215 no total) e um teste de ponta a ponta pelo CLI com cópias do
  `es_settings.cfg`, do `es_input.cfg` e do `PCSX2.ini` deste PC e um PC novo com os arquivos de fábrica do RetroBat:
  nenhuma senha, caminho, tela ou janela foi para a nuvem, o PC novo recebeu tudo e as duas pontas ficaram em dia.
  **Falta:** testar com o ES e o PCSX2 abrindo de verdade em dois PCs.
- **Regra genérica testada em 08/11/2026:** 12 testes automáticos (227 no total) e os arquivos reais do RetroArch
  deste build entre dois PCs simulados: as 3.256 chaves portáveis do `retroarch.cfg` chegaram iguais, as 145 do PC
  ficaram, e nenhuma senha, token ou caminho foi para a nuvem. Para isso, todos os sistemas passam a abrir pelo
  `retroboat-launch` (antes, só os que tinham jogos, saves ou configurações na nuvem).
- **Risco semântico médio:**
  - chave nova do upstream no `es_settings.cfg` que dependa da máquina precisa entrar em `EsSettingsRules`, senão viaja;
  - o emulatorLauncher reescreve parte do `PCSX2.ini` a cada jogo, a partir das opções do ES. Se ele passar a gravar algo
    da máquina numa chave nova, essa chave precisa entrar em `EmulatorConfigRules`;
  - `Settings.cpp` e `InputManager.cpp` do ES e `Pcsx2.Generator.cs` entram na lista de vigilância;
  - na regra genérica, uma chave da máquina com nome fora do padrão (sem "monitor", "device", "driver" e afins)
    viaja. Se o emulatorLauncher a regrava a cada jogo, não faz mal; se não, o emulador ganha regra própria.

## RB-013 — BIOS na nuvem

- **Onde:** `retroboat/app` (`Bios\`, `Cloud\StartupSync`, `Es\EsApi`, lançador). Nada no upstream.
- **Desenho:** seção 4b do [cloud-first](design/cloud-first.md).
- **Risco semântico baixo:** o catálogo de BIOS copia a lista de nomes e a ordem de busca do
  `Pcsx2.Generator.cs` (`SetupConfigurationQT`). Se o upstream mudar essa lista ou a pasta
  `bios\pcsx2\bios`, o status fica errado sem conflito de merge. Esse trecho entra na lista de
  vigilância.
- **Correção (25/10/2026):** uma BIOS zerada na nuvem com o mesmo nome, tamanho e data da cópia boa
  do PC (o caso da recuperação de disco) era tratada como igual e nunca era consertada. Agora a
  cópia zerada, de qualquer lado, é substituída pela boa do outro lado, mesmo que pareça mais nova.

## RB-009 — Memory cards do PS2 na nuvem (primeira parte dos saves)

- **Regra genérica (08/11/2026):** os saves de todos os sistemas (`SaveRules.Generic`): a pasta
  `saves\<sistema>` inteira, com as subpastas de cada emulador, no mesmo motor dos memory cards
  (`SaveRule.Recursive`; o nome de cada arquivo é o caminho relativo). Fora: pastas de BIOS,
  firmware, chaves, jogos instalados, capas, capturas e caches, arquivos de mais de 256 MB e as pastas
  de uma regra própria (`pcsx2\memcards`). Só entra o que mudou depois que a regra começou a valer
  no PC (`OnlyChangedHere`, data em `saves-sync.xml`), então o que veio com o RetroBat nunca sobe; num
  PC novo, o arquivo de fábrica dá lugar ao da nuvem sem conflito. Ficam as 10 versões mais novas de
  cada arquivo na nuvem (`CloudVersionsKept`; cada PC apaga só as suas). Com o Drive fechado, só
  pergunta se o sistema já tem saves sincronizados neste PC. PS3, Switch, Wii U, Xbox, Xbox 360, Vita,
  PS4 e 3DS ficam fora (`SystemsNotYetSupported`): os saves deles dividem a pasta com jogos
  instalados, firmware ou chaves. Testado com 9 testes automáticos e uma cópia da pasta `saves` real
  em dois PCs simulados. **Risco semântico médio:** se o emulatorLauncher mudar onde um emulador grava,
  o save novo entra sozinho se ficar em `saves\<sistema>`; fora dela, não.

- **Onde:** `retroboat/app` (`Saves\SaveRules`, `SaveSync`, `SaveSession`; `Retroboat.Launch`:
  `SaveFlow`, `ChoiceForm`; `Cloud\StartupSync`; comandos `saves` do CLI). Nada no upstream.
- **Desenho:** seção 9 do [cloud-first](design/cloud-first.md), "Memory cards: o emulador gerencia,
  o Retroboat sincroniza".
- **Como funciona:** o PCSX2 cria, escolhe e grava os cartões. O Retroboat sincroniza todo `*.ps2`
  de `saves\ps2\pcsx2\memcards` com `retroboat\saves\ps2\pcsx2\memcards` no Drive, em versões
  imutáveis comprimidas, uma por gravação. O `retroboat-launch` cuida de cada partida:
  - antes: trava de outro PC, versão que ainda está chegando e conflito, cada um com sua tela;
  - durante: envia cada gravação 3 s depois que o emulador para de gravar;
  - depois: envia o que faltar e libera a trava.
  O `Retroboat.exe` envia, ao abrir, o que ficou de uma partida offline. O comando do PS2 passa
  sempre pelo `retroboat-launch`, mesmo sem jogos na nuvem.
- **Testado em 25/10/2026** no Drive real, com um script no lugar do PCSX2 (que ainda não está
  instalado na build) e um segundo PC simulado (`retroboat-cli saves ... --pc PC-SALA`):
  - um cartão criado durante o jogo subiu 5 s depois, e cada save subiu em até 6 s;
  - as gravações feitas logo antes de fechar subiram ao fechar;
  - cada versão de um cartão de 8 MB ficou com cerca de 73 KB no Drive;
  - a tela de trava apareceu e fechou sozinha quando o outro PC terminou;
  - na tela de conflito, a escolha da versão da nuvem trouxe o save do outro PC e guardou o deste
    em `conflitos\`;
  - uma partida offline foi enviada pelo `Retroboat.exe` na abertura seguinte.
- **Confiabilidade (25/10/2026):** o caso de referência é um RetroBat portátil num pen drive arrancado
  no meio do jogo.
  - Conferência do formato do memory card (`Ps2MemoryCard`) antes de enviar ou pôr no lugar.
  - Conserto automático de um cartão cortado a partir da nuvem, com aviso na tela.
  - Versões danificadas na nuvem são puladas.
  - Pasta inacessível durante o jogo não vira exclusão.
  - Gravação local com escrita forçada no disco.
  - `.bak` do estado de sincronização.
  - `saves verify`, `saves history` com o estado de cada cópia, e `saves restore`.
  - Testado com um pen drive simulado (`subst`) arrancado durante o jogo.
- **Cartão novo em branco (02/11/2026):** o PCSX2 cria os cartões com a memória "apagada" (tudo em
  0xFF) e o jogo só os formata no primeiro save. O Retroboat tratava esses cartões como danificados
  e repetia o aviso a cada abertura. Agora um cartão em branco não vai para a nuvem e não gera aviso
  (só uma linha de informação no log); se a nuvem já tem esse cartão, a versão da nuvem vem para cá,
  como num PC novo. Zerado (tudo em 0x00) ou cortado continua sendo danificado.
- **Falta testar com o PCSX2 de verdade:** ver se ele segura o arquivo do cartão aberto durante o
  jogo. Se segurar, os envios durante o jogo falham e tudo vai ao fechar.
- **Risco semântico médio:** depende do bloco "Memory cards management" de `Pcsx2.Generator.cs`
  (pasta `saves\ps2\pcsx2\memcards`, extensão `.ps2`, `Slot1_Filename`/`Slot2_Filename` no
  `PCSX2.ini`) e da chave `saves=` do `emulatorLauncher.cfg`. Mudança ali deixa de sincronizar sem
  conflito de merge; os dois pontos entram na lista de vigilância.

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
- **Capas automáticas (25/10/2026):** o scraper do ES não funciona sem conta no ScreenScraper (a
  API responde 403 "Erreur de login" e o ES cancela tudo). Agora o lançador busca sozinho, para
  todo jogo da biblioteca sem imagem, capa, tela do jogo e tela de título nos thumbnails do
  libretro (`Media\AutoMedia`, `LibretroThumbnails`, `ThumbnailIndex`), sem conta nem chave, e envia
  para o Drive na mesma abertura. Os nomes são casados com o padrão No-Intro/Redump, aceitando
  "_" no lugar de ",", "The" no fim, região GoodTools e título sem subtítulo; achou os 26 jogos
  de PS2 do Drive (77 imagens em 24 s na primeira vez; depois só o que faltar). Desliga com
  `[Media] Automatic=0`. Descrição e vídeo continuam vindo do scraper do ES, com conta no
  ScreenScraper.
- **Informações automáticas (01/11/2026):** como na Steam, a página de um jogo mostra o conteúdo
  dele antes do download. Os jogos não baixados apareciam só com o nome do arquivo ("God of War
  (USA)") e a linha genérica "Na nuvem · 7,0 GB · baixa na primeira vez que você jogar." como
  descrição. Agora o lançador preenche, para todo jogo da biblioteca, o que estiver vazio
  (`Media\AutoInfo`, sem conta nem chave):
  - **libretro-database** (`metadat`, pelo nome Redump; `LibretroMetadata`): desenvolvedora,
    editora, data, jogadores, gênero e descrição em inglês. No PS2 cobre cerca de 4.700 jogos.
  - **Wikidata + Wikipédia** (`WikidataInfo`): resumo da página do jogo em português, gêneros
    específicos em português e o que o libretro não tem (GTA San Andreas e The Warriors não estão
    lá). Só aceita item que seja jogo eletrônico e tenha saído para a plataforma do sistema, para não
    trazer outro jogo de mesmo nome.
  - **Ordem por campo, com o scraper do ES por último (decisão de 01/11/2026).** Numa comparação de
    50 jogos de PS2 (26 com dados do scraper), o scraper errou o gênero em 5 (Shadow of the Colossus
    e Ben 10 como "Briga de rua", True Crime como "Corrida") e inverteu desenvolvedora e editora em 2
    (The Punisher, Tomb Raider Underworld). O libretro põe "Action" em quase tudo e tem erros (Black =
    RPG, Just Cause = Avalanche Software); o Wikidata acerta o estúdio e o gênero, mas mistura
    editoras de outras regiões e temas como "personagem LGBT" (filtrados).
    - Nome, gênero e desenvolvedora: Wikidata → libretro → scraper.
    - Editora, data e jogadores: libretro (a edição do arquivo) → Wikidata → scraper.
    - Descrição: Wikipédia em português (se não for só uma frase) → libretro (inglês) → scraper.
    - O que nenhuma fonte tem continua como estava. Uma edição feita à mão num desses campos também é
      trocada, porque o gamelist não diz quem escreveu cada valor.
  - Cada jogo é consultado no máximo uma vez por mês, achado ou não; com uma fonte fora do ar, tenta
    de novo na próxima abertura. O que vier vai para o Drive na mesma abertura, e os outros PCs
    recebem sem buscar.
  - A linha "Na nuvem · ..." saiu da descrição: os dois temas mostram o status no botão (RB-014,
    RB-017). O aviso de corrompido continua. Sem o Drive, a abertura também tira as linhas antigas,
    a partir do índice deste PC.
  - Testado na build: os 6 jogos sem informações (5 ainda não baixados) ficaram completos na
    primeira abertura, que levou 8 s ao todo com o Drive conectado; as informações chegaram ao Drive.
  - Risco semântico baixo: as fontes são externas e podem mudar de formato. Sem resposta, o jogo só
    fica sem informações, sem erro visível; o log do lançador registra.
- **Correção:** a nuvem juntava os PCs por jogo inteiro (a entrada mais nova vencia). Um PC que só
  tinha o nome escondia a descrição e a capa enviadas por outro. Agora junta campo a campo.
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
- **Como funciona (25/10/2026, segunda versão):** segue o
  [design system do Retroboat](design/design-system-steam.md), com base no Steam Big Picture.
  - Visão detalhada: barra de ação abaixo da mídia, com JOGAR (verde), BAIXAR (azul) ou
    INDISPONÍVEL (cinza) e o status em legenda + valor.
  - Visões em grade e carrossel: ícone de status na faixa de informações do jogo.
  - Cabeçalho: só o ícone global de nuvem. O lançador cria
    `retroboat\estado\nuvem-conectada` (`Cloud\CloudStatusFile`) quando acha o Drive.
  - A primeira versão punha o status no cabeçalho, onde outros itens o cobriam (ex.: o aviso
    "Procurando mídias" do scraper).
- **De onde vem o status:** do tamanho do arquivo (marcador de 0 bytes = na nuvem). A primeira
  versão lia a descrição, e o scraper do ES a troca: um jogo da nuvem aparecia como instalado.
  "Corrompido" ainda vem da descrição.
- **Validado em 25/10/2026** com o ES real nas visões Icons e detailed: jogo instalado (JOGAR,
  check verde) e da nuvem (BAIXAR), ícone de nuvem aceso com o Drive conectado.
- **Risco semântico baixo:** depende do marcador de 0 bytes, do prefixo "Corrompido na nuvem"
  e do caminho `retroboat\estado` a partir da pasta do tema. Com o RB-010, o tema passa a usar
  `{game:cloud}`.
- **Entrada no build (25/10/2026):** `build.ini: theme_path` aponta para o fork (C1, 1 linha).
  O `build.ini` é quente (31 alterações/ano), mas quase sempre em `retrobat_version` e `branch`;
  a linha `theme_path` não muda desde o primeiro commit. O RetroBuild clona para a pasta
  `es-theme-carbon`, então quem já usa o Carbon não perde a configuração.
- **Risco semântico:** o fork precisa acompanhar o Carbon oficial. Se ficar parado, o RetroBat
  sai com um tema velho sem conflito de merge. A rotina de sincronização do FORK.md vale para
  ele também (`upstream-check.sh -C ../es-theme-carbon -b master`).
- **Drive fechado (01/11/2026):** sem o arquivo `nuvem-conectada`, um jogo da nuvem mostrava BAIXAR
  azul e "Baixa na primeira vez que você jogar", o que não dá para fazer sem o Drive. Agora mostra
  BAIXAR desativado, "NA NUVEM · DRIVE FECHADO" em amarelo e "Abra o Google Drive para baixar"; o
  ícone da faixa fica cinza. O lançador já tratava esse caso como "não disponível" (e não como
  corrompido): `OnDemandInstaller` responde "Abra o Google Drive e tente de novo" e o jogo continua
  na nuvem. Validado com o ES real na visão detailed. O RB-019 reativou o BAIXAR, porque agora o
  Retroboat oferece abrir o Drive.

## RB-017 — Status do Retroboat no tema PlayStation-X

- **Onde:** clone local de [pajarorrojo/es-theme-PlayStation-X](https://github.com/pajarorrojo/es-theme-PlayStation-X)
  (versão 43.1, `1e4650e`) em `..\es-theme-PlayStation-X`, com o upstream só de leitura. Arquivos
  novos `retroboat/status.xml` e `retroboat/README.md`; duas linhas no fim do `theme.xml`. Ainda
  não há fork no GitHub (os commits ficam só no clone até o fim das etapas).
- **Por quê:** o PlayStation-X foi instalado pelo ES nesta build e precisa mostrar o mesmo status do
  Carbon do Retroboat (RB-014).
- **Como funciona:** as mesmas regras do RB-014 (marcador de 0 bytes, prefixo "Corrompido na
  nuvem", arquivo `nuvem-conectada`), encaixadas no layout do tema:
  - ps4Style (visão padrão) e single: o "Iniciar" do jogo em foco vira JOGAR, BAIXAR ou
    INDISPONÍVEL, com legenda + valor ao lado do nome;
  - detailed: barra de ação abaixo da mídia (a mídia fica um pouco menor);
  - todas as visões em grade: selo no fim da faixa de ícones (`contenedor-iconos`);
  - cabeçalho: ícone global de nuvem entre "★ 2026" e o relógio (na linha do "v.43" em 4:3, 5:4
    e 3:2, onde o tema não deixa espaço);
  - Drive fechado: BAIXAR desativado com aviso, como no Carbon;
  - o testador de mídia do tema (test-medias) continua sem o botão.
- **Validado em 01/11/2026** com o ES real (janela de 1280×720 e de 1024×768), em todas as visões do
  tema: jogo instalado, na nuvem, corrompido (descrição alterada só para o teste) e Drive conectado e
  fechado. O log do ES não tem avisos dos elementos novos.
- **Classificação:** C1 no `theme.xml` (duas linhas no fim, depois de `</variables>`), calor média:
  4 alterações nos últimos 12 meses, duas delas no bloco de variáveis, logo acima das nossas linhas.
  Um merge pode pedir uma resolução simples ali (manter as duas linhas no fim).
- **Risco semântico médio:** o `status.xml` copia posições do tema: o retângulo do "Iniciar" do
  ps4Style e do single, a posição da mídia e do vídeo na detailed e no test-medias, e o vão entre
  "★ 2026" e o relógio. Uma versão nova que mude isso desalinha o status sem conflito de merge.
  Depois de cada sincronização, conferir as visões ps4Style, single e detailed.
- **Entrada no build:** o RetroBuild só baixa um tema (`theme_path`, o Carbon do Retroboat). O
  PlayStation-X vem do instalador de temas do ES, que apaga a pasta do tema e baixa o oficial ao
  instalar, reinstalar ou atualizar (`ApiSystem::installBatoceraTheme`).
- **Reaplicação pelo lançador (01/11/2026):** o app leva os `status.xml` do Carbon e do
  PlayStation-X (`Themes\ThemeOverlays`, recursos do `Retroboat.Core.dll`). O `Retroboat.exe` os põe
  de volta a cada abertura, antes do ES, e grava o script
  `.emulationstation\scripts\theme-changed\retroboat-temas.bat`, que faz o mesmo quando um tema é
  escolhido no menu (`retroboat-cli themes apply`).
  - Só grava o que falta ou mudou; nos testes, a conferência leva cerca de 8 ms.
  - Antes de mexer, confere que a pasta é mesmo do tema (um arquivo que só ele tem). Se a versão
    instalada do PlayStation-X não for a 43, aplica e avisa no log.
  - Testado na build: tema "reinstalado" (`theme.xml` oficial e sem `retroboat\`) voltou com o status
    pelo `Retroboat.exe --no-launch` e pelo script do evento, e o ES mostrou o botão.
  - Classificação: C0 no app. Grava só arquivos nossos (`retroboat\status.xml`, o script) e a linha de
    include no `theme.xml` do tema instalado, que não é versionado aqui.
  - Risco semântico: se o ES mudar a pasta de temas, o nome da pasta do tema ou parar de rodar o
    evento `theme-changed` de forma síncrona, o status só volta na abertura seguinte, sem erro
    visível. Os três pontos (`Paths.cpp`, `ApiSystem::installBatoceraTheme`, `Scripting.cpp`) entram
    na lista de vigilância.
- **Upstreamável:** não (depende do lançador do Retroboat).

## RB-018 — Aviso falso de BIOS ao abrir jogo

- **Onde:** `retroboat/app` (`Bios\EsBiosCheck`, `Accounts\EsSettings`, lançador). Nada no upstream.
- **Problema:** com uma BIOS de PS2 válida e usada pelo PCSX2 (ex.: `SCPH-39001_BIOS_V7_USA_160.BIN`), o ES
  avisa "THE SYSTEM HAS MISSING BIOS FILE(S)" a cada jogo. Ele pergunta ao `batocera-systems.exe`, cuja lista
  embutida só tem, no PS2, `bios/pcsx2/bios/ps2-0230a-20080220.bin`.
- **Correção:** o lançador grava `CheckBiosesAtLaunch=false` no `es_settings.cfg` uma única vez por PC
  (`[Bios] EsCheckHandled` no `retroboat.ini`), com o ES fechado. Quem religar em "CHECK BIOS FILES BEFORE
  RUNNING A GAME" não é contrariado. A BIOS continua conferida pelo conteúdo na notificação do início (RB-013).
- **Limite conhecido:** a chave vale para todos os sistemas, e o catálogo do Retroboat só cobre o PS2. Um
  sistema fora do catálogo sem BIOS deixa de avisar antes do jogo. Resolve de vez ao corrigir a lista do
  `batocera-systems` ou ao aumentar o catálogo.
- **Classificação:** camada C0, risco de merge nenhum.
- **Risco semântico:** baixo: se o ES renomear a chave, o aviso volta, sem conflito de merge.
- **Upstreamável:** não; o que serve ao upstream é aceitar as outras BIOS no `batocera-systems`.
- **Como testar:** fechar o ES, abrir pelo `Retroboat.exe` e abrir um jogo de PS2: o jogo abre sem o aviso.

## RB-019 — Google Drive fechado: perguntar, abrir e acompanhar

- **Onde:** `retroboat/app` (`Cloud\GoogleDriveApp`, `Cloud\CloudStatusFile`, `Es\EsApi`, `Retroboat.Launch\CloudAppPrompt`,
  `Retroboat.Launcher\CloudWatcher`) e os `status.xml` dos temas (RB-014, RB-017). Nada no upstream.
- **Problema:** com o Drive fechado, o Retroboat só avisava "Abra o Google Drive e tente de novo". O ícone de nuvem
  desligada só mudava de cor, e não dava para diferenciar. Abrir ou fechar o Drive com o ES aberto não mudava
  nada até reiniciar. Com o Drive fechado no início, sistemas com jogos na nuvem (fora o PS2) ficavam sem o
  `retroboat-launch`, e o marcador vazio ia direto para o emulador.
- **Correção:**
  - `CloudAppPrompt`: pergunta em tela cheia, com controle, com "Abrir o Google Drive", "Sempre abrir, sem perguntar"
    (`[Cloud] AutoOpen=1`) e "Agora não". Aparece ao iniciar, ao apertar BAIXAR e ao abrir um jogo com memory card.
    "Agora não" não pergunta de novo para memory card até o próximo início; BAIXAR pergunta sempre, porque
    sem o Drive o jogo não abre. A pergunta some se o Drive abrir por outro caminho. Sem o Drive instalado, ou com
    `[Cloud] Path` num disco que está lá, não há pergunta.
  - `GoogleDriveApp`: acha a versão mais nova em `%ProgramFiles%\Google\Drive File Stream` e abre com
    `--startup_mode`, o mesmo argumento da inicialização do Windows (fica na bandeja, sem janela).
  - `CloudWatcher`: o `Retroboat.exe` fica em segundo plano enquanto o ES estiver aberto e confere o Drive a cada
    5 s. Mudou (duas leituras iguais, por causa da montagem): sincroniza se conectou, recarrega o ES
    (`GET /reloadgames`) e notifica. Com jogo aberto ou scraper rodando (`/isIdle`), espera.
  - Temas: nuvem `neutral-11` com um X `neutral-2` por cima. BAIXAR fica ativo com o Drive fechado.
  - `CreateCommandWrapper` também lê os sistemas do índice local, para o `retroboat-launch` estar lá com o Drive fechado.
- **Decisões:**
  - Com o ES aberto, a pergunta não aparece quando o Drive fecha: uma tela por cima tiraria o foco de quem está
    navegando ou jogando. Fica a notificação, o X e a pergunta ao apertar BAIXAR.
  - O ícone não muda por `<visible>`: o `exists()` do ES guarda o resultado em cache (`FileCache`) até o próximo reset,
    que só acontece em reload e ao voltar de um jogo. Por isso o `/reloadgames`, que fecha um menu aberto e leva o
    cursor para o primeiro jogo da lista.
  - O Drive puxa o foco ao abrir. `Form.Activate` sozinho é negado pelo Windows, então as telas usam
    `AttachThreadInput` por 15 s depois de abrir o Drive (`Theme.HoldFocus`).
- **Classificação:** camada C0, risco de merge nenhum (RB-014 e RB-017: arquivos só nossos).
- **Risco semântico:** se o Google mudar a pasta de instalação ou o nome do processo, o Retroboat volta a só avisar,
  sem pergunta. Se o ES mudar `/reloadgames` ou `/isIdle`, o ícone fica parado até reiniciar.
- **Upstreamável:** não.
- **Limite conhecido:** só o Google Drive para computador. Com a conta sem login, o Drive abre, não conecta e a
  tela explica depois de 60 s; a janela de login dele fica atrás da tela do Retroboat até lá.
- **Validado em 07/11/2026** com o ES real e o Drive real. O X, nos dois temas; o resto, no PlayStation-X:
  pergunta com "Sempre abrir" no início e "Agora não" ao apertar BAIXAR; X e notificação cerca de 12 s depois de o
  Drive fechar com o ES aberto; BAIXAR abrindo o Drive e começando o download, com o foco de volta; cancelar mantém
  o marcador; Drive abrindo com o ES aberto sincroniza, traz os jogos e acende a nuvem; o `Retroboat.exe` sai sozinho
  cerca de 15 s depois do ES fechar. Não exercitados: a pergunta do memory card, a conta sem login, a resposta
  "Abrir o Google Drive" (mesmo caminho de "Sempre abrir"), o controle (só teclado) e a barra de ação na visão
  detalhada do Carbon com o texto novo.
- **Como testar:** com o Drive fechado, abrir o `Retroboat.exe`: a pergunta aparece. Responder "Agora não", ver o X no
  cabeçalho e abrir o Drive pela mão: em cerca de 10 s o ES recarrega com a nuvem acesa.

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

## RB-020 — Downloads em segundo plano e região de ícones que gira

- **Onde:** `retroboat/app`: `Core\Downloads` (fila, serviço, rotação, posição da região), `Retroboat.Launch\DownloadHost`,
  `HeaderBar` e `HeaderBarFace`, e os `status.xml` dos temas (RB-014, RB-017). A `DownloadForm` saiu. Nada no upstream.
- **Pedido (07/11/2026):** continuar navegando durante um download. O andamento aparece discreto no topo: a região dos
  ícones gira, mostra a barra com o título e volta aos ícones, com status de baixando.
- **Como funciona:**
  - O ES só continua quando o programa que ele abriu termina. Por isso o `retroboat-launch` põe o jogo na fila, inicia o
    serviço (`--downloads`, um processo à parte) e sai com código 1: o ES não conta partida, e só mostra erro de 200 a 300.
  - Fila em `estado\downloads.xml`, protegida por um mutex, porque dois processos a usam. Tirar o jogo da fila é o pedido
    para cancelar. Um arquivo por jogo em `estado\baixando` serve ao `exists()` do tema.
  - O índice (`biblioteca-nuvem.xml`) é lido de novo antes de gravar: a sincronização do `CloudWatcher` pode ter
    rodado durante o download.
  - `OnDemandOutcome.CloudUnavailable`: sem o Drive, ou com ele caindo no meio, o serviço espera e tenta de novo.
- **Decisões:**
  - **Janela por cima do ES, e não dentro dele.** O tema não recebe dados ao vivo: `<visible>` e `<text>` só são
    reavaliados quando o jogo selecionado muda, `exists()` tem cache e `/reloadgames` fecharia menus. A alternativa é
    um componente no `Window.cpp` do fork do ES, mas o build usa o ES oficial até as credenciais de API
    (RB-011) e trocar agora perderia os scrapers. Quando o RB-010 existir, a região vira um componente do ES e a janela sai.
  - **A face de ícones é uma foto do cabeçalho**, tirada na hora de girar. Serve para qualquer tema sem desenhar ícones de
    novo, e com os ícones à mostra a janela nem existe na tela: o que aparece é o ES de verdade.
  - **A captura enxerga janelas em camadas** no Windows 11, mesmo sem `CAPTUREBLT`. Na volta, a janela sai das capturas por
    60 ms (`WDA_EXCLUDEFROMCAPTURE`), para a foto não ser dela mesma. Sem esse recurso (Windows antigo), vale a foto da ida.
  - **O download continua com um jogo aberto**, em prioridade baixa. A Steam pausa por padrão; aqui, pausar faria o jogo
    demorar mais para chegar, e o Drive já limita a velocidade.
  - **Rotação de 450 ms com aceleração e pouso**, e não 200 ms em ease-out: com ease-out, o giro acabava em 100 ms e não aparecia.
- **Classificação:** camada C0, risco de merge nenhum.
- **Risco semântico:** médio. Depende de o ES continuar: (1) esperando só o processo que abriu, sem job object; (2) tratando
  código de saída 1 como erro silencioso, sem contar partida; (3) com `POST /addgames` limpando o cache e redesenhando a lista;
  (4) em janela sem borda, senão a janela do Retroboat não aparece por cima. Uma mudança de layout de um tema não dá conflito,
  mas pode deixar a região fora do lugar (tabela em `HeaderRegion`).
- **Upstreamável:** não.
- **Testado em 07/11/2026** com o ES oficial real e o `retroboat-launch` de verdade, numa cópia isolada com nuvem simulada em
  pasta local, em janela (1280x720) e em tela cheia sem borda (1920x1080, Windows em 125%), nos dois temas:
  - BAIXAR volta ao ES em cerca de 2 s (inclui a animação de abertura do ES), o botão vira BAIXANDO e, ao terminar, JOGAR ao vivo;
  - nomes com apóstrofo e vírgula; fila com dois jogos; jogo novo durante o PRONTO; cancelar pela tela um jogo da fila;
  - sem a nuvem: AGUARDANDO; ES fechado e aberto de novo: a fila continua; jogo zerado na nuvem: NÃO BAIXOU e notificação;
  - o foco fica no ES, e a região some quando outra janela vem para a frente;
  - gamelist com campos desconhecidos e datas do scraper: idêntico depois do `POST /addgames`.
- **Ainda não testado no app:** com o Google Drive de verdade, o download continuando com um emulador aberto e a tela de cancelar no jogo que está baixando (esse caso só nos testes automáticos do serviço).
- **Efeitos conhecidos do ES, que só o RB-010 resolve:** apertar BAIXAR passa pela animação de abertura de jogo e reinicia a
  música, com o aviso do nome dela.

## RB-010 — Nuvem nativa no ES

- **Onde:** fork `retroboat-emulationstation`: arquivos novos em `es-app/src/retroboat/` (`RetroboatCloud`, `CloudHeaderRegion`,
  `RetroboatHttp.h`, `retroboat.cmake`) e ganchos de uma linha, marcados `RETROBOAT RB-010`, em `es-app/CMakeLists.txt`,
  `ViewController.cpp`, `ISimpleGameListView.cpp`, `SystemScreenSaver.cpp`, `FileData.cpp`, `BasicGameListView.cpp`,
  `GameNameFormatter.cpp`, `HttpServerThread.cpp`, `TextListComponent.h` e `ImageGridComponent.h`. No app:
  `retroboat-launch --baixar/--cancelar`, `NativeStatusReporter`, `DownloadStatusJson`, `EsApi`. Nos temas: o valor do BAIXANDO.
  Desenho: [design/rb010-es-nativo.md](design/rb010-es-nativo.md), com o que mudou na implementação na seção 11.
- **Problema:** com o ES oficial, BAIXAR passa pela abertura de jogo (animação, música reiniciada, aviso do nome da música), a
  região do cabeçalho é uma janela por cima do ES e o tema não recebe o andamento.
- **Correção:** o nosso ES lê o índice da nuvem e a fila (`biblioteca-nuvem.xml`, `estado\downloads.xml`) e atende o A num jogo
  da nuvem sem abrir nada:
  - na nuvem: roda `retroboat-launch --baixar` (a pergunta do Drive, a fila e o serviço são os do RB-019 e do RB-020);
  - na fila: pergunta "Cancelar o download?" (NÃO com o foco e no B; SIM roda `--cancelar`);
  - corrompido: explica o motivo.
  O serviço manda o andamento por `POST /retroboat/download`; o ES desenha a região que gira dentro do próprio quadro e expõe
  `{game:cloudState}`, `{game:inCloud}`, `{game:downloading}`, `{game:downloadPercent}` e `{game:downloadProgress}`. A barra de
  ajuda mostra BAIXAR, BAIXANDO ou INDISPONÍVEL; listas de texto esmaecem o nome, com ícone de nuvem, download ou alerta; blocos
  de grade ficam com 63% da opacidade. Quando o estado muda, a lista é refeita mantendo o cursor.
- **ES oficial ou o nosso:** o serviço pergunta `GET /retroboat/download` a cada sessão. 200: manda o andamento ao ES e não usa a
  `HeaderBar` nem o `/addgames`. Outra resposta: o RB-020 como antes. O tema usa `estado\es-nativo`, que o nosso ES cria antes de
  carregar o tema e apaga ao sair (o `Retroboat.exe` apaga um que tenha ficado de um ES que caiu).
- **Classificação:** C1, risco de merge médio: 32 linhas em 10 arquivos do upstream, três deles de calor médio (`FileData.cpp`,
  `ViewController.cpp`, `HttpServerThread.cpp`). Toda a lógica está nos nossos arquivos.
- **Risco semântico:** médio. Depende de: `ISimpleGameListView::launchSelectedGame` e `ViewController::launch` continuarem sendo a
  entrada do A; `FileData::getProperty` cair no fim para nomes desconhecidos; `BindingManager::updateBindings` reavaliar sem
  reiniciar animações; o relógio e a bateria do ES seguirem desenhados depois das visões (a região termina antes deles: 0,94 no
  PlayStation-X, 0,95 nos demais).
- **Upstreamável:** não.
- **Limites conhecidos:** o build ainda usa o ES oficial (o nosso não tem as chaves de API do ScreenScraper, TheGamesDB, HfsDB e
  RetroAchievements, RB-011). O `es-update` do RetroBat pode trocar o executável pelo oficial; aí volta o RB-020 sozinho.
- **Testado em 08/11/2026** com o nosso ES e o `retroboat-launch` de verdade, numa cópia isolada (nuvem em pasta local, janela
  1280x720 fora da tela), nos dois temas:
  - A num jogo da nuvem: sem animação; fila, serviço no modo nativo, download e JOGAR ao vivo com o cursor no lugar;
  - região com andamento sintético: PREPARANDO, BAIXANDO com tamanho, velocidade e tempo restante, cubo girando na ida e na volta,
    ciclo de 10 s e 6 s, PRONTO, NÃO BAIXOU e a notificação com o motivo depois da volta;
  - valor do BAIXANDO ao vivo no jogo ("61% · restam 0:08"); fila percebida pelo arquivo em até 2 s;
  - cancelar: B mantém, SIM cancela pelo `--cancelar` e o jogo volta a BAIXAR; jogo corrompido: aviso com o motivo;
  - ajuda BAIXAR, BAIXANDO, INDISPONÍVEL; lista do Carbon esmaecida com ícones; grade com ícones e blocos esmaecidos.
- **Corrigido no teste:** textos da região viravam quadrados pretos (a fonte era descartada pelo ES); o relógio e a bateria do ES
  ficavam por cima da região.
- **Ainda não testado:** com o Google Drive de verdade, com controle (só a API de entrada), em tela cheia sem borda e com o
  renderer OpenGL 2.1.
- **Como testar:** abrir o RetroBat com o nosso ES, apertar A num jogo da nuvem: o ES não sai da lista, o botão vira BAIXANDO e a
  região do topo gira com o andamento.

## RB-021 — Biblioteca identificada, setup inicial e menu NUVEM

- **Onde:** app (`Core\Cloud\LibraryIdentity`, `LibraryChoice`, `LibrarySetup`, `CloudLocator`, `Core\Settings\SettingsSummary`,
  `Launcher\SetupFlow`, `CloudWatcher`, `retroboat-cli settings`); fork `retroboat-emulationstation`: os nossos
  `es-app/src/retroboat/RetroboatSettingsGui.cpp` e `.h` e 2 linhas marcadas `RETROBOAT RB-021` em `GuiMenu.cpp` (calor ALTA).
  Desenho: [design/rb021-setup-e-configuracoes.md](design/rb021-setup-e-configuracoes.md), com o que mudou na seção 13.
- **Problema:** o Retroboat pegava a primeira conta do Google Drive com a pasta `retroboat` (com duas contas, decidia pela
  letra) e não tinha como criar a biblioteca sem risco de uma segunda pasta vazia. O primeiro uso era uma janela comum do
  Windows, e as opções só mudavam editando o `retroboat.ini`.
- **Correção:** `retroboat\biblioteca.xml` (criado uma vez, nunca reescrito) dá um id à biblioteca, e o PC só usa a pasta com
  esse id. Uma biblioteca só é adotada sem perguntar. Nos outros casos, um setup em tela cheia com controle pergunta, e criar
  pede confirmação depois de uma segunda busca. O menu NUVEM do nosso ES mostra o estado e muda as opções principais pelo
  `retroboat-cli`.
- **Classificação:** app C0. ES: C1, risco de merge baixo: 2 linhas (um `#include` e uma entrada) num arquivo de calor ALTA;
  toda a lógica nos nossos arquivos.
- **Risco semântico:** baixo. Depende de `GuiMenu` continuar montando o menu principal no `mMenu` dentro do construtor (a
  nossa entrada monta a própria linha, como o `addEntry`, para usar o ícone de nuvem), da caixa de ícone do
  `MenuComponent::addMenuIcon` (1,25 letra) para alinhar com as outras entradas, do
  `GuiSettings::addSaveFunc` rodar ao sair e de `Utils::Platform::quitES` fechar o ES devolvendo o controle ao `retrobat.exe`.
- **Upstreamável:** não.
- **Escritas no Drive:** só as da seção 9 do desenho: pastas (`CreateDirectory`) e o `biblioteca.xml` (temporário + mover que
  falha se o nome existe). O teste de armadilhas confere hash e data de todos os arquivos.
- **Testado em 09/11/2026** numa cópia isolada, com o nosso ES fora da área visível e o `G:` real só lido. Os detalhes estão
  na seção 13 do desenho.
- **Ainda não testado:** o ciclo com o `Retroboat.exe` em segundo plano e o `retrobat.exe` de verdade, a busca de mídia com o ES
  aberto, o Drive real (a primeira abertura cria `G:\Meu Drive\retroboat\biblioteca.xml`) e o controle.
- **Como testar:** com uma conta só, abrir o Retroboat: a notificação diz "Biblioteca da nuvem: Meu Drive (G:)". No menu do ES,
  NUVEM: ligar BAIXAR VÍDEOS e sair. O `retroboat.ini` muda, e os vídeos chegam com o ES parado.
