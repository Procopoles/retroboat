# Desenho: biblioteca isolada, biblioteca na nuvem e perfil na nuvem

> **Atualizado por [cloud-first.md](cloud-first.md) (24/10/2026).** A parte de biblioteca isolada
> (F1/RB-003) e o levantamento de pontos de extensão continuam valendo. Para a nuvem (F2, F3 e
> RB-006), passa a valer o cloud-first: motor de sincronização próprio com interface de provedor
> no lugar do rclone, saves e memory cards como marco próprio (RB-009) e nova ordem dos marcos.

Patches: RB-003 (biblioteca isolada), RB-004 (biblioteca na nuvem), RB-005 (perfil na nuvem) e
RB-006 (base da nuvem, compartilhada pelos dois últimos). Investigado em 24/10/2026 contra
`batocera-emulationstation@master`, `emulatorlauncher@master` e `retrobat-executable@main`.

## Resumo

As três features cabem num **componente novo nosso** que conversa com o RetroBat por pontos de
extensão que o ES e o emulatorLauncher **já têm**. A primeira versão de cada uma não precisa mudar
nenhuma linha de código do upstream: é camada C0, com risco de merge praticamente zero.

O que só é possível mudando o ES fica para uma fase 2 opcional, que depende do build próprio do ES
(decisão 2 do FORK.md). Nessa fase entram o selo de "na nuvem" desenhado na própria lista de jogos e
o item "Baixar" no menu do jogo.

Ordem recomendada: **F1 → base da nuvem → F3 → F2**. As duas features de nuvem dependem da F1:
os jogos da nuvem precisam de uma pasta de jogos própria, e o perfil só sincroniza de forma limpa
quando metadados e configurações não estão misturados com as ROMs.

## Pontos de extensão usados (o nosso "contrato" com o upstream)

Se o upstream mudar um destes pontos, a feature quebra **sem conflito de merge**. Por isso eles
entram numa lista de vigilância (seção final).

| # | Ponto de extensão | Onde está no upstream | Alterações/ano no arquivo | Usado por |
|---|---|---|---:|---|
| E1 | O ES mescla `es_systems_*.cfg` por cima do `es_systems.cfg`, sistema por sistema e tag por tag | ES `SystemData::loadAdditionnalConfig` | 17 | F1, F2, F3 |
| E2 | O ES lê e grava `.emulationstation\gamelists\<sistema>\gamelist.xml` quando a pasta de jogos não tem `gamelist.xml` | ES `SystemData::getGamelistPath` | 17 | F1, F3 |
| E3 | A chave `roms=` do `emulatorLauncher.cfg` muda a pasta de jogos para todos os geradores (81 usos de `GetFullPath("roms")`) | EL `ConfigFile.GetFullPath` | 2 | F1 |
| E4 | Scripts em `.emulationstation\scripts\<evento>\`. `quit` espera o script terminar; `game-start` só espera se o nome terminar em `-wait`; `game-end` não informa qual jogo terminou | ES `Scripting.cpp` | 1 | F3 |
| E5 | API HTTP local do ES em `127.0.0.1:1234`, sempre ligada: `/notify`, `POST /systems/<s>/games/<id>` (metadados), `/addgames`, `/removegames`, `/reloadgames` | ES `HttpServerThread.cpp` | 9 | F2 |
| E6 | Coleções personalizadas em `.emulationstation\collections\custom-<nome>.cfg` | ES `CollectionSystemManager.cpp` | — | F2 |

## Componente novo: Retroboat Cloud (RB-006)

| Peça | O que faz |
|---|---|
| `Retroboat.exe` (lançador) | Vira o atalho principal. Antes de abrir o RetroBat: gera os overlays (E1), aplica `roms=` (E3), puxa o perfil da nuvem; depois chama o `retrobat.exe` de sempre. |
| Agente em segundo plano | Lista a biblioteca da nuvem, baixa jogos, manda notificações e status para o ES (E5). |
| `retroboat-launch` | Fica na frente do `emulatorLauncher.exe` no `<command>` de cada sistema (overlay E1, gerado a cada início). Puxa os saves do jogo antes de abrir e envia ao fechar; se o jogo está só na nuvem, baixa com tela de progresso antes. |
| Motor de nuvem: **rclone** (licença MIT) | Google Drive, e de graça também OneDrive, Dropbox, S3 e NAS/SMB. Faz download retomável, confere checksum e guarda histórico de versões. |

Por que um lançador na frente do `retrobat.exe`: o `retrobat.ini` já tem `AppLauncher`, mas o
`retrobat.exe` abre esses apps em paralelo, sem esperar (`Program.cs:105`). Não daria para garantir
que o perfil fosse baixado antes do ES ler as configurações. Uma opção "esperar" no `AppLauncher`
resolveria e serve para qualquer usuário (RB-008, upstreamável); se for aceita, o lançador deixa de
ser necessário.

Risco do `retroboat-launch`: é um processo a mais entre o ES e o emulatorLauncher. Ele precisa
repassar o código de saída e não pode atrapalhar o encerramento do jogo pelo atalho do controle nem
o `/emukill` do ES. Isso entra no teste de cada sincronização com o upstream.

Tecnologia: C# com .NET Framework 4.8, igual ao resto do RetroBat. Já vem instalado no Windows
10/11 e tem WinForms para o assistente e as telas de progresso. Os arquivos de runtime ficam em
`system/retroboat/`: o RetroBuild copia `system/` inteiro, então tudo entra na distribuição sem
tocar em nenhuma lista do upstream.

## F1: biblioteca isolada (RB-003)

Hoje `retrobat\roms\<sistema>\` mistura três coisas com donos diferentes: os jogos (do usuário),
o `gamelist.xml` (metadados, favoritos, tempo jogado; escrito pelo ES) e `images\ videos\ manuals\`
(mídia do scraper). Os saves já ficam separados, em `retrobat\saves`.

| Parte | Como | Camada |
|---|---|---|
| 1a. Pasta de jogos escolhida pelo usuário (ex.: `D:\Jogos`) | O lançador gera `es_systems_retroboat.cfg` com o `<path>` de cada sistema apontando para `D:\Jogos\<sistema>` (E1) e grava `roms=D:\Jogos` no `emulatorLauncher.cfg` (E3) | C0 |
| 1b. Gamelists fora da pasta de jogos | Usa o caminho alternativo que o ES já suporta (E2). O lançador migra os `gamelist.xml` existentes e pré-cria um vazio para cada sistema, senão o ES criaria um novo na pasta de jogos | C0 |
| 1c. Mídia do scraper fora da pasta de jogos | Fase 1: depois de um scrape, o agente move `images/videos/manuals` para `.emulationstation\media\<sistema>\` e reescreve os caminhos no gamelist (o ES aceita caminho absoluto). Fase 2: opção nativa no ES em `Scraper::getSaveAsPath` (2 alterações/ano, upstreamável) | C0 → C1 no ES |
| 1d. Escolher a pasta | Assistente no lançador, com seletor de pasta | C0 |

Riscos:

- **Sistema novo no upstream:** coberto. O overlay é gerado a cada início a partir do
  `es_systems.cfg` atual, não mantido à mão, então não há como ficar defasado.
- **Caminho montado à mão no emulatorLauncher:** achei um lugar que ignora `roms=`, o ExoDOS
  (`ExoDOS.Generator.cs:483`, cria atalhos em `retrobat\roms`). É um bug de uma linha, upstreamável (RB-007).
- **Atualizador oficial:** algumas ações do `upgrade.xml` assumem a pasta padrão (ex.:
  `move roms\doom`) e não valem para a pasta nova. É raro; o `upstream-check.sh` mostra quando
  `upgrade.xml` muda.
- **HD externo desconectado:** o ES esconde os sistemas cuja pasta não existe. É aceitável; o
  lançador avisa na hora de abrir.

## F3: perfil na nuvem (RB-005)

O ponto que torna isso viável: o RetroBat **regera a configuração de cada emulador a partir do
`es_settings.cfg` a cada jogo aberto**. Sincronizar as configurações do ES leva junto a maior parte
da configuração dos emuladores.

| O quê | Onde | Sincroniza? |
|---|---|---|
| Opções do ES e dos emuladores (globais, por sistema, por jogo) | `.emulationstation\es_settings.cfg` | sim, **menos as chaves da máquina** (resolução, monitor, áudio, pasta de jogos...), que ficam locais |
| Controles | `.emulationstation\es_input.cfg` | sim (mapeado pelo GUID do controle) |
| Metadados, favoritos, tempo jogado | `.emulationstation\gamelists\` (depois da F1) | sim |
| Coleções | `.emulationstation\collections\` | sim |
| Saves e save states | `retrobat\saves\` | sim, por jogo: puxa antes de abrir e envia ao fechar |
| Saves que alguns emuladores gravam dentro de `emulators\<emu>\` | (issue aberta no upstream: emulatorlauncher#656) | fase 2, com uma lista curada |
| `retrobat.ini` | raiz | sim |
| Mídia do scraper | `.emulationstation\media\` (depois da F1) | opcional (pode ser grande) |
| BIOS | `bios\` | opcional, **desligado por padrão** (decisão 4) |
| Emuladores | `emulators\` | não: o RetroBat baixa cada emulador na primeira vez que ele é usado |

Fluxo:

- **Abrir o Retroboat:** o lançador puxa o perfil antes do ES.
- **Abrir um jogo:** o `retroboat-launch` puxa os saves daquele jogo antes de chamar o emulatorLauncher.
- **Fechar um jogo:** o `retroboat-launch` envia os saves assim que o emulador termina. Os scripts
  de evento não serviriam aqui, porque o `game-end` do ES não diz qual jogo terminou.
- **Sair do ES:** o script `quit` (E4, síncrono) envia o perfil.
- **Dois PCs ao mesmo tempo:** o perfil tem uma trava na nuvem ("em uso no PC-SALA: continuar
  offline ou assumir?"). Vence a última escrita, mas o rclone guarda as versões anteriores, então
  nada se perde.
- **PC novo:** instalar o Retroboat, conectar a conta, escolher o perfil e pronto.

Risco: se o Windows abrir o `retrobat.exe` direto (opção `Autostart`), o perfil não é puxado.
Mitigação: o lançador se registra como autostart, e o script do evento `start` avisa quando o
perfil não foi sincronizado.

## F2: biblioteca na nuvem (RB-004)

Organização na nuvem: `Retroboat/Jogos/<sistema>/<arquivo>`, com os mesmos nomes de pasta das ROMs.

**Como os jogos aparecem:** o agente lista a nuvem e cria **arquivos marcadores** (vazios, com o
mesmo nome do jogo) na pasta de jogos, além de um índice `cloud-index.json`. O ES lista esses jogos
como qualquer outro.

**Como o status fica claro (fase 1, sem mudar o ES):**

- coleção **"☁ Na nuvem"** mantida pelo agente (E6);
- descrição do jogo marcada pela API (E5): "☁ Na nuvem · 1,4 GB · baixa ao jogar";
- notificações do próprio ES (E5) com o progresso e com "pronto para jogar";
- opção de baixar os favoritos automaticamente em segundo plano.

**Download:** ao abrir um jogo que está só na nuvem, o `retroboat-launch` mostra uma tela de
progresso em tela cheia, navegável por controle. B cancela e volta ao ES sem erro; ao terminar, o
jogo abre. A alternativa seria um script `game-start`, mas ele não consegue cancelar o lançamento: se
o download falhasse, o emulador abriria um arquivo vazio. O `<command>` envolvido também é gerado
pelo lançador a cada início (overlay E1), então acompanha qualquer mudança de comando do upstream.

Detalhes:

- Jogos de vários arquivos (`.cue`+`.bin`, `.m3u`, `.gdi`, pastas de PS3/Switch): o marcador
  representa o arquivo principal, e o download traz a pasta toda.
- O espaço livre é verificado antes; "remover cópia local" volta o jogo a marcador.
- Limitação da fase 1: scraper por hash não funciona em marcador. Os metadados e a mídia vêm do
  perfil na nuvem (F3) ou do scrape por nome.

**Fase 2 (fork do ES):** selo nativo na lista e na grade, "Baixar / Remover cópia local" no menu
do jogo e fila de downloads com barra. Toca `FileData.cpp` (11 alterações/ano), `GuiGameOptions.cpp`
(6) e os bindings de tema: é C2 no ES. Só seria upstreamável num desenho genérico ("status de
disponibilidade do jogo" + evento), sem nada específico do Google.

Alternativas descartadas:

| Alternativa | Por que não |
|---|---|
| Google Drive para Desktop (unidade `G:`) | Não precisa de login nosso, mas o arquivo só baixa quando o emulador tenta abri-lo: sem progresso no ES, e alguns emuladores desistem antes de terminar. Continua servindo como fonte para quem preferir. |
| Loja do ES (`batocera-store`, que aceita `batocera-store*.cfg` extras e já tem tela de instalação com progresso) | Instala pacotes compactados extraindo na raiz do RetroBat: dobra o espaço com ROMs grandes e ignora a pasta de jogos da F1. |
| API Cloud Files do Windows (a tecnologia do OneDrive) | Melhor integração com o Explorer, mas é complexa e tem o mesmo problema de baixar sem progresso no ES. |

## Login no Google (decisão 1 e 3)

O rclone precisa de um "cliente OAuth". O escopo de acesso muda tudo:

| Escopo | O que permite | Custo |
|---|---|---|
| `drive.file` | só arquivos criados pelo próprio Retroboat | escopo não sensível, sem auditoria. Os jogos precisam ser **enviados pelo Retroboat** ("Enviar biblioteca para a nuvem") |
| `drive.readonly` / `drive` | ler pastas que já existem no seu Drive | escopo restrito: distribuir publicamente exige verificação do Google com avaliação de segurança anual. Para uso pessoal (até 100 contas de teste) funciona, mas o login expira a cada 7 dias |

O perfil (F3) funciona com `drive.file` de qualquer forma, porque são arquivos que o próprio
Retroboat cria.

## Tamanho e ordem

| Etapa | Tamanho | Depende de |
|---|---|---|
| 1. F1 (1a, 1b, 1d) + migração | M | — |
| 2. Base da nuvem: lançador, agente, rclone, login | M | 1 |
| 3. F3: perfil + saves | M | 2 |
| 4. F2: biblioteca, fase 1 | G | 1, 2 |
| 5. 1c: mover mídia depois do scrape | P | 1 |
| 6. Fase 2 nativa no ES | G | build próprio do ES |

## Lista de vigilância

Arquivos do upstream dos quais estas features dependem sem editá-los. Quando algum deles mudar,
reveja o ponto de extensão antes de atualizar:

- ES: `es-app/src/SystemData.cpp` (`loadAdditionnalConfig`, `getGamelistPath`, `loadSystem`/`<path>`),
  `es-core/src/Scripting.cpp`, `es-app/src/services/HttpServerThread.cpp`, `es-app/src/CollectionSystemManager.cpp`
- emulatorlauncher: `EmulatorLauncher.Common/FileFormats/ConfigFile.cs` (`GetFullPath`)
- retrobat-executable: `RetroBat/RetroBat/Program.cs`, `EmulationStationLauncher.cs`
- este repo: `system/templates/emulationstation/es_systems.cfg` (formato do `<command>`), `system/upgrade.xml`
