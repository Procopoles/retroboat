# Desenho: Retroboat cloud-first

Etapa 1 do [ROADMAP](../ROADMAP.md). Atualiza e amplia [biblioteca-e-nuvem.md](biblioteca-e-nuvem.md):
a parte de biblioteca isolada (RB-003) continua valendo como está; a parte de nuvem passa a seguir
este documento. Investigado em 24/10/2026.

**Modelo, igual ao Steam:** a nuvem é a fonte da verdade, mas nada roda direto da nuvem. O jogo é
baixado, fica instalado no PC e é jogado localmente. Saves, memory cards e configurações são
sincronizados antes e depois de cada sessão.

## 1. O que "cloud-first" cobre

| Área | Experiência estilo Steam |
|---|---|
| **Jogos** | A biblioteca mostra todos os jogos da nuvem, instalados ou não. Cada jogo tem um status (☁ na nuvem · ⬇ baixando 42% · ✔ instalado · ⚠ corrompido). Dá para instalar sob demanda ou em fila em segundo plano, pausar e retomar (inclusive depois de reiniciar o PC), desinstalar só a cópia local, ver o espaço usado e verificar a integridade. A verificação rápida lê alguns setores sem baixar o jogo e acha arquivos zerados; a completa compara o hash com bases conhecidas (Redump/No-Intro). |
| **Saves e save states** | Sincronizados por jogo: puxa antes de abrir e envia ao fechar. Se os dois lados mudaram, aparece um diálogo de conflito ("Este PC: hoje 14:32 · Nuvem: ontem 22:10, PC-SALA"). Funciona offline e sincroniza quando a conexão volta. Guarda um histórico de versões. |
| **Memory cards** | No PS2, o emulador gerencia os cartões como no console (quantos quiser, um por slot, um por jogo) e o Retroboat sincroniza todos os que ele usa com o Drive sempre: antes de jogar, a cada gravação e ao sair, com trava para não serem usados em dois PCs ao mesmo tempo (seção 9). GameCube (GCI) e PS1 (DuckStation) já usam um arquivo por jogo no padrão do RetroBat. |
| **Configurações** | Opções do ES e dos emuladores (só as portáveis), controles, coleções, metadados, favoritos e tempo jogado (somado entre PCs, não sobrescrito). |
| **Perfil e PCs** | Lista de PCs e aviso de "perfil em uso no PC-SALA". A conta é a do Google Drive para computador instalado no PC. |
| **PC novo** | Instala o Google Drive para computador e o Retroboat, e está tudo lá: a biblioteca aparece inteira, e cada jogo baixa na primeira vez que for jogado. |

## 2. Transporte: o Google Drive para computador

**Decisão (24/10/2026):** o Retroboat usa o **app Google Drive para computador** já instalado no
PC, e não a API do Drive. Neste PC: versão 132, unidade `G:\`, modo streaming. O Retroboat lê e
grava arquivos em `G:\Meu Drive\retroboat\`, e o app do Google cuida de login, envio, download e
modo offline.

**O que ganhamos:**

- Nenhum projeto no Google Cloud, nenhum login OAuth nosso e nenhuma verificação do Google, para
  qualquer número de usuários. Isso elimina o principal limite levantado antes (escopo restrito do Drive).
- Retomada, fila de envio e funcionamento offline já vêm prontos do Google.
- O provedor passa a ser simplesmente "uma pasta". Qualquer nuvem com app de sincronização de pasta
  (OneDrive, Dropbox) ou um NAS na rede funciona com o mesmo código.

**Medido neste PC:**

- Listar `G:\` lê só os metadados; nada é baixado.
- Leitura parcial funciona: 3 leituras de 2 KB em pontos diferentes de um ISO de 2,5 GB que estava
  só na nuvem levaram cerca de 2 s, sem baixar o arquivo inteiro. Isso permite copiar com barra de
  progresso real e verificar integridade por amostragem.
- Os atributos dos arquivos aparecem como `Normal`, com ou sem cache local. O Retroboat não pode
  depender do cache do Google para saber o que está instalado: "instalar" é copiar para a pasta de
  jogos local e registrar no índice.

**Limitações e como o desenho compensa:**

| Limitação | Consequência | Como lidamos |
|---|---|---|
| Não há como saber quando um envio terminou | Outro PC pode ainda não enxergar o save mais novo | Saves gravados como **versões imutáveis** (um arquivo por versão) e um índice por PC. O outro PC sabe que existe versão nova pelo índice e espera o arquivo chegar ("Aguardando a nuvem...") |
| Dois PCs gravando o mesmo arquivo geram "cópias de conflito" do Drive | Arquivos duplicados e ambíguos | Cada PC só grava arquivos próprios (com o nome do PC no caminho). Nenhum arquivo é editado por dois PCs |
| O cache do Drive fica no `C:` | Instalar um jogo ocupa o espaço duas vezes por um tempo (cache + pasta de jogos). Hoje o `C:` tem cerca de 98 GB livres | Fila de instalação respeitando o espaço livre; recomendar mover o cache do Drive para o disco da pasta de jogos |
| A letra da unidade e o nome da pasta mudam (`G:`, "Meu Drive" / "My Drive") | Caminho diferente em cada PC | Detectar a unidade com rótulo "Google Drive" e guardar o caminho como configuração local do PC |
| Depende do app do Google rodando e logado | Sem ele, a nuvem fica indisponível | Modo offline: jogos instalados e saves locais continuam funcionando; o Retroboat sincroniza quando o Drive volta |

A API do Drive continua possível como provedor alternativo no futuro, por exemplo para saber
exatamente quando um envio terminou, sem mudar o resto do desenho.

Ressalva sobre "nosso drive": se a ideia for **uma** pasta servindo jogos para várias pessoas, o
Google limita downloads repetidos de arquivos compartilhados, e a licença do RetroBat proíbe
distribuir junto conteúdo protegido. O desenho assume que cada pessoa usa a própria biblioteca.

**Organização na nuvem:**

```
G:\Meu Drive\retroboat\
  jogos\<sistema>\<arquivos do jogo>         ← a biblioteca: você coloca os jogos aqui
  bios\<mesma estrutura de <RetroBat>\bios>  ← suas BIOS (ex.: bios\pcsx2\bios\scph39001.bin)
  .retroboat\                                ← dados do app (não mexer à mão)
    saves\<sistema>\<jogo>\<data>-<PC>.zip   ← versões imutáveis dos saves
    indices\<PC>.json                        ← o que cada PC já enviou (cada PC grava só o seu)
    perfil\<PC>\...                          ← configurações e estatísticas por PC, mescladas na leitura
    pcs\<PC>.json                            ← PCs registrados e trava de perfil
```

## 3. Motor próprio de sincronização (muda a escolha anterior do rclone)

O documento anterior propunha o rclone. Para uma experiência estilo Steam, um **motor próprio** é
melhor. Motivos:

- **Unidade de sincronização = o jogo**, não a pasta. O rclone sincroniza árvores de arquivos; nós
  precisamos de "os arquivos deste jogo", com regras diferentes por emulador.
- **Conflito de três vias:** estado local, estado na nuvem e estado da última sincronização
  (manifesto). Só assim dá para distinguir "mudou só aqui" de "mudou nos dois", e mostrar o diálogo.
- **Progresso, fila e prioridade por item**, para a interface. Com o rclone isso exigiria
  interpretar a saída dele.
- **Tempo jogado e favoritos se mesclam por campo** (soma, máximo, união); não basta ficar com o
  arquivo mais novo.
- **Funcionar sobre uma pasta sincronizada**, onde o envio é assíncrono e sem confirmação. Isso
  pede versões imutáveis e índices por PC (seção 2), algo que nenhuma ferramenta de sync de pasta faz.

```
                ┌──────────────────────── Retroboat Cloud (processo nosso) ────────────────────────┐
  ES (UI)  ◄──► │ API local HTTP ── Biblioteca ── Fila de transferências ── Sync de saves/config │
  retroboat-    │                        │                    │                     │            │
  launch   ◄──► │                        └──── Índice/manifestos (SQLite local) ────┘            │
                │                                         │                                       │
                │                  Interface de provedor (listar, ler com progresso, gravar)      │
                └──────────────┬──────────────────────────┬──────────────────────────┬────────────┘
             Pasta sincronizada: Google Drive     NAS / pasta local          (API do Drive, opcional)
             para computador (G:\), OneDrive...
```

- **API local HTTP do Retroboat Cloud:** o mesmo contrato serve a integração C0 de agora
  (lançador, `retroboat-launch`, notificações no ES) e a interface nativa do ES no M5, sem retrabalho.
- **Tecnologia:** C# / .NET Framework 4.8, igual ao resto do RetroBat. Vem no Windows, tem WinForms
  e permite reaproveitar o `EmulatorLauncher.Common` (leitura de `es_settings.cfg`, gamelists e
  `emulatorLauncher.cfg`). Com o Drive para computador, não há dependência de biblioteca do Google.
- **Onde mora:** repositório próprio (`retroboat-cloud`), com releases que o nosso build baixa, do
  mesmo jeito que o RetroBuild baixa ES e emulatorLauncher. Isso mantém este repositório (o fork do
  RetroBat) enxuto e fácil de mesclar. É C0 para o upstream em qualquer caso.

## 4. Saves e memory cards: o que existe hoje no RetroBat

O emulatorLauncher já centraliza boa parte dos saves em `saves\<sistema>\<emulador>\`: 60 das 114
famílias de geradores apontam para lá. Isso facilita, mas há armadilhas:

| Emulador | Onde o RetroBat põe | Granularidade | Cuidado |
|---|---|---|---|
| RetroArch (libretro, a maioria dos sistemas) | `saves\<sistema>\<rom>.srm`, `.state*` | **por jogo** (pelo nome da ROM) | — |
| DuckStation (PS1) | `saves\<sistema>\duckstation\memcards` | por jogo (padrão do RetroBat: um cartão por título) | a opção `duckstation_memcardtype` pode trocar para cartão compartilhado |
| PCSX2 (PS2) | `saves\ps2\pcsx2\memcards\` | depende da opção `pcsx2_slot1_memory`. Padrão: **um cartão só para todos os jogos** (`Mcd001.ps2`, 8 MB). `game`: um cartão por jogo (`<nome da ROM>.ps2`). `folder`: cartão em pasta (`Mcdf01.ps2`) | o cartão é um arquivo só: sincroniza inteiro, com trava. O Retroboat gerencia cartões com nome escolhidos pelo usuário (ver seção 9) |
| Dolphin (GC/Wii) | `saves\<gamecube\|wii>\dolphin-emu\User` | GameCube: pasta GCI, um arquivo por jogo (padrão do RetroBat); Wii: NAND por título | a opção `dolphin_slotA` troca para cartão inteiro; a NAND inclui configuração do sistema (`SYSCONF`) |
| PPSSPP (PSP) | `saves\psp` (`PSP\SAVEDATA`) | por jogo (pasta por ID) | — |
| RPCS3 (PS3) | `saves\ps3\rpcs3` (raiz do emulador) | por jogo (`savedata`) | **contém jogos instalados, updates e DLC**: sincronizar só `savedata` |
| Cemu (Wii U) | `saves\wiiu\cemu\mlc01` | por jogo (`usr\save`) | **contém títulos, updates e DLC instalados** |
| Ryujinx (Switch) | `saves\switch\ryujinx\portable` (raiz do emulador) | por jogo (`bis\user\save`) | **contém chaves e firmware**: nunca sincronizar fora de `save` |
| Xenia (X360) | `saves\xbox360\xenia\content` | por título | inclui DLC e title updates |
| MAME / PSXMAME | `saves\mame\nvram`, `saves\psxmame\nvram` | por jogo | — |

As outras 54 famílias não apontam para `saves\`: os saves ficam dentro de `emulators\<emu>\` (há
uma issue aberta sobre isso no upstream, emulatorlauncher#656) ou o sistema não tem save (ports,
lançadores de PC). Cada uma precisa de uma regra própria.

**Consequência de desenho:** um **catálogo de regras de save** por emulador (arquivos incluídos,
excluídos e como agrupar por jogo) fica como dado versionado, não espalhado no código. Começamos
pelo RetroArch, que cobre a maioria dos sistemas, e pelos emuladores standalone mais usados. O
catálogo cresce sem tocar no motor.

**Segurança dos saves:** antes de sobrescrever qualquer save local, o motor guarda um snapshot
local. Save é dado insubstituível, então um bug de sincronização não pode apagar progresso.

## 4a. Biblioteca na nuvem, fase 1 (RB-004, implementada em 25/10/2026)

- **Marcadores:** a cada início, o lançador lista `retroboat\jogos` no Drive (só metadados) e cria, na
  pasta de jogos local, um arquivo vazio com o nome que cada jogo terá quando instalado. Um ZIP com
  um único disco dentro aparece com o nome do disco, lido no diretório central do ZIP sem baixá-lo.
- **Descrição no gamelist local:**
  - "Na nuvem · 3,0 GB · baixa na primeira vez que você jogar.", que sai quando o jogo é instalado;
  - "Corrompido na nuvem: …" quando o arquivo do Drive não presta;
  - uma descrição escrita pelo usuário ou pelo scraper nunca é trocada.
- **Índice local** (`<RetroBat>\retroboat\biblioteca-nuvem.xml`): estado de cada jogo (na nuvem,
  instalado, corrompido). Se o arquivo mudar no Drive, o estado é refeito.
- **Download ao jogar:** o overlay troca o `<command>` dos sistemas que têm jogos na nuvem por
  `"%HOME%\..\retroboat\retroboat-launch.exe" <comando original>`. Se o `-rom` é um marcador, o
  `retroboat-launch` mostra uma tela cheia com progresso, feita para controle (B ou Esc cancela e
  volta ao ES), instala e só então chama o emulatorLauncher. Jogos já instalados passam direto.
- **ZIP:** o disco é extraído direto do Drive para a pasta de jogos, numa passada, sem guardar o ZIP.
  No fim, o CRC-32 é conferido. Uma extração interrompida recomeça do início.
- **Ainda não tem:** fila em segundo plano, desinstalar pelo ES, selo visual na lista (fica para o M5)
  e verificação dos jogos em segundo plano. Hoje, um jogo zerado só é descoberto quando alguém tenta
  jogá-lo.

## 4b. BIOS na nuvem (RB-013, implementado em 25/10/2026)

As BIOS ficam em `retroboat\bios\` no Drive, com a mesma estrutura de `<RetroBat>\bios`. A cada
início, o lançador:

1. **Organiza:** BIOS conhecidas soltas na raiz da pasta do Retroboat ou de `bios\` vão para a pasta
   que o emulador usa (ex.: `bios\pcsx2\bios`), junto com os arquivos que as acompanham (`.MEC`,
   `.NVM`).
2. **Baixa** o que falta ou mudou neste PC.
3. **Envia** as BIOS conhecidas que só existem neste PC. O pacote de BIOS livres do RetroBat não é
   enviado, porque já vem em toda instalação.

Se um arquivo estiver nos dois lados e diferente, vence o mais novo. Um arquivo zerado nunca é
copiado e nunca substitui um bom.

**Status de BIOS.** Para cada sistema que tem jogos, o Retroboat confere o arquivo que o emulador
**vai usar** (na mesma ordem de nomes do emulatorLauncher) e valida o conteúdo. No PS2, ele lê o
ROMDIR/ROMVER e mostra versão e região. O resultado pode ser:

- ok (com versão e região);
- sem BIOS;
- corrompida;
- válida com nome que o emulador não procura (e qual nome usar).

O "Verificar BIOS" nativo do ES não serve para isso. Ele usa um catálogo embutido no
`batocera-systems.exe` que, para PS2, só conhece `ps2-0230a-20080220.bin`, e acusaria falta mesmo com
uma BIOS SCPH válida.

**Retorno na interface:**

- Janela "Sincronizando com a nuvem" no lançador, mostrada só se a sincronização passar de 0,4 s.
- Notificação no ES (API local `/notify`) quando ele termina de abrir. Exemplos:
  - "Nuvem: BIOS sincronizadas (1 baixada)" e "PlayStation 2: BIOS ok (v1.60, EUA)";
  - "Atenção: PlayStation 2: a BIOS do Drive (…) está zerada".
- `retroboat-cli bios status | sync | organize` e o log do lançador.

A tela nativa de status dentro do ES fica para o M5.

## 5. Configurações: o que viaja e o que fica

O RetroBat regera a configuração de cada emulador a partir do `es_settings.cfg` a cada jogo. Por
isso, sincronizar as configurações do ES leva junto quase tudo. A regra é por chave:

- **Viaja:** opções globais, por sistema e por jogo (shaders, núcleo escolhido, proporção de tela,
  idioma, bezels...), controles (`es_input.cfg`), coleções, favoritos, metadados.
- **Fica no PC:** resolução, monitor, dispositivo de áudio, pasta de jogos, rede, tudo que depende
  do hardware. É uma lista mantida no catálogo, igual às regras de save.
- **Mescla por campo:** tempo jogado e número de partidas são somados entre os PCs, a data do último
  jogo fica com a maior, favoritos são unidos.

Risco semântico (para o registro de patches): chaves novas que o upstream criar no
`es_settings.cfg` e que dependem da máquina precisam entrar na lista "fica no PC". O
`upstream-check.sh` deve passar a alertar quando o emulatorLauncher introduzir chaves novas.

## 6. Como aparece no ES

| Fase | Como | Camada |
|---|---|---|
| C0 (M2 a M4) | Arquivos marcadores na pasta de jogos, coleção "☁ Na nuvem", status na descrição e notificações via API HTTP do ES, tela de download e de conflito do `retroboat-launch` | C0 |
| Nativa (M5) | Selo de status na lista e na grade, "Instalar / Desinstalar / Verificar" no menu do jogo, tela de Downloads, indicador de sincronização. Lê a API local do Retroboat Cloud | C2 no fork do ES |

O M5 cria o fork do ES e o build Windows. É a mesma infraestrutura que o redesign (etapa 2) vai
usar, então fechar a etapa 1 já deixa a etapa 2 destravada.

## 7. Marcos da etapa 1

| Marco | Entrega | Pronto quando |
|---|---|---|
| **M0. Fundações** | Ferramentas de build, código em `retroboat/app`, provedor de pasta com detecção do Drive para computador, verificação de integridade, CLI de teste | `retroboat-cli cloud ls` lista `G:\Meu Drive\retroboat\jogos`; `install` copia um jogo para a pasta local com progresso e retoma se cair; `verify` aponta os ISOs zerados |
| **M1. Biblioteca isolada** (RB-003) | Pasta de jogos própria, gamelists fora dela, lançador `Retroboat.exe` | jogos em `D:\Jogos` abrem em todos os sistemas testados; nada é escrito na pasta de jogos além dos jogos |
| **M2. Biblioteca na nuvem** (RB-004) | Índice da nuvem, marcadores, instalar ao jogar com progresso, fila em segundo plano, desinstalar, verificar | um jogo que só está na nuvem aparece no ES, baixa com progresso e abre; desinstalar devolve ao estado "☁" |
| **M3. Saves e memory cards** (RB-009) | Catálogo de regras (RetroArch + 5 standalone), sync por jogo, conflito, offline, snapshots | jogar no PC A, abrir no PC B e continuar do mesmo save; editar nos dois gera o diálogo de conflito |
| **M4. Perfil e configurações** (RB-005) | Chaves portáveis, mescla de estatísticas, PC novo em poucos passos | PC limpo + login = mesma biblioteca, opções e controles |
| **M5. ES nativo** (RB-010, RB-011) | Fork e build do ES, status e ações nativos | as ações da biblioteca funcionam sem sair do ES, sem marcadores na descrição |

M1 e M0 são independentes e podem andar juntos. A biblioteca na nuvem (M2) vem antes dos saves
(M3): é a parte mais visível, e o motor de transferência que ela cria é reaproveitado pelos saves.

## 8. Estado da biblioteca e pré-requisitos

**A biblioteca do Drive ainda não tem nenhum jogo válido** (verificado em 24/10/2026 com
`retroboat-cli cloud verify`):

- A estrutura já segue o formato do RetroBat: `G:\Meu Drive\retroboat\jogos\ps2\`. Os nomes de
  sistema precisam continuar iguais aos do RetroBat (`ps2`, `psx`, `snes`...).
- Há 9 ISOs de PS2 (18,3 GB), **todos zerados**. Todos fazem parte dos 44 ISOs (122 GB) listados
  em `registro-transferencia-ps2-2026-10-24.json`, vindos de `C:\Stuffs\recuperacao_nvme`, que o
  registro dá como "conteúdo integralmente zerado" e "removido do Drive".
- A remoção não chegou ao Drive, e a pasta de origem vem perdendo arquivos: estava com 44 e agora
  tem 33. Algum processo ainda mexe nesses arquivos. Não apaguei nada.
- Os nomes vieram com `_` no lugar de `,` (ex.: `(Europe_ Australia)`), herdado da recuperação.
  Isso atrapalha um pouco o scraper por nome.

Pré-requisitos que faltam: jogos válidos e a BIOS do PS2 (seção 9) e um RetroBat instalado para
testes. A pasta `C:\RetroBat` existe, mas está vazia. O Google Drive para computador (`G:\`) e o
.NET SDK já estão prontos.

## 9. Testes com PS2 primeiro

Decisão (24/10/2026): todos os testes começam com jogos de PS2. É um bom caso difícil:

- **Downloads grandes:** 1 a 4 GB por jogo, entre 3 e 8 minutos a 6–9 MB/s, a velocidade medida no
  Drive deste PC. É onde a experiência de download precisa ser boa.
- **Memory card:** um cartão serve a vários jogos, como no console, e precisa estar sempre
  sincronizado.

O que cada marco testa com PS2:

| Marco | Teste com PS2 |
|---|---|
| M1 | `D:\Jogos\ps2` (ou a pasta escolhida) aparece no ES e o PCSX2 abre o jogo de lá |
| M2 | Jogo só na nuvem aparece no ES, baixa com progresso, abre no PCSX2; desinstalar e reinstalar retoma |
| M3 | Salvar no "Cartão 1" no PC A e continuar no PC B; dois jogos no mesmo cartão; cartão em uso em outro PC; save state também |
| M4 | Opções do PCSX2 (resolução interna, widescreen) escolhidas no ES chegam ao outro PC |

**Memory cards: o emulador gerencia, o Retroboat sincroniza** (decisão de 25/10/2026, substitui a
de 24/10, em que o Retroboat trocava cartões com nome dentro do `Mcd001.ps2`):

- **Quem manda é o emulador.** O PCSX2 cria, formata, renomeia, apaga e escolhe os cartões de cada
  slot, inclusive por jogo, e o emulatorLauncher escolhe o arquivo de cada slot pela opção
  `pcsx2_slot1_memory` do ES. A cada jogo, ele aponta o PCSX2 para `saves\ps2\pcsx2\memcards` e
  regrava os slots (`Generators\Pcsx2.Generator.cs`, "Memory cards management"):
  - padrão: `Mcd001.ps2` no slot 1 e `Mcd002.ps2` no slot 2;
  - `game`: `<nome da ROM>.ps2` no slot 1;
  - `folder`: a pasta `Mcdf01.ps2`.
- **O Retroboat não troca nada de lugar.** Ele sincroniza todo cartão `*.ps2` dessa pasta, qualquer
  que seja o nome. Toda gravação que o emulador fizer num cartão vai para a nuvem.
- **Quando:**
  - antes de abrir um jogo de PS2: puxa a versão mais nova de cada cartão;
  - durante o jogo: confere os cartões a cada 2 s e envia uma versão nova 3 s depois que o
    emulador para de gravar, então uma queda de energia não perde o save;
  - ao fechar o jogo: envia o que faltar e libera a trava;
  - ao abrir o Retroboat: envia o que ficou de uma sessão offline e baixa o que chegou.
- **Na nuvem** (`retroboat\saves\ps2\pcsx2\memcards\`, o mesmo caminho de `<RetroBat>\saves`):
  - `<cartão>\<data UTC>-<PC>.gz`: uma versão imutável por gravação, comprimida. Um cartão de
    8 MB quase vazio fica com poucos KB;
  - `<cartão>\<data UTC>-<PC>.apagado`: o cartão foi apagado no emulador;
  - `<cartão>\conflitos\`: a versão que perdeu num conflito. Nada é jogado fora;
  - `pcs\<PC>.xml`: a trava e a última versão que cada PC enviou. Cada PC só grava o próprio arquivo.
- **Neste PC:**
  - `<RetroBat>\retroboat\saves-sync.xml` guarda a versão e o SHA-1 de cada cartão na última
    sincronização. Com isso, o Retroboat distingue "mudou aqui", "mudou na nuvem" e "mudou nos dois";
  - antes de substituir ou apagar um cartão local, ele guarda uma cópia em
    `<RetroBat>\retroboat\historico\saves\...`, as 20 últimas de cada cartão.
- **Conflito:** se o cartão mudou nos dois lados, uma tela pergunta qual manter, com a data e o PC de
  cada um. A outra versão vai para `conflitos\`. Juntar saves de jogos diferentes do mesmo cartão
  exigiria ler o sistema de arquivos do memory card; fica para depois.
- **Trava:** enquanto um PC joga, o `pcs\<PC>.xml` dele diz "em uso desde 14:32" e é renovado a cada
  2 minutos. Outro PC que abrir um jogo de PS2 vê "Os memory cards estão em uso no PC-SALA", com
  "Jogar mesmo assim" ou "Voltar". Uma trava sem renovação há mais de 10 minutos (PC desligado no
  meio do jogo) é ignorada.
- **Aguardando a nuvem:** se o `pcs\` de outro PC já cita uma versão que ainda não chegou a este
  PC, o Retroboat espera por ela, com a opção de jogar sem esperar.
- **Apagar:**
  - um cartão só é apagado na nuvem se sumir durante o jogo, ou seja, se o emulador o apagou;
  - um cartão que sumiu fora do jogo (pasta `saves` apagada, RetroBat reinstalado) é baixado de novo;
  - um cartão vazio ou zerado nunca é enviado nem substitui um bom.
- **Primeiro uso:** cartões que já existem neste PC e não existem na nuvem são enviados. Se a nuvem
  já tiver um cartão com o mesmo nome e conteúdo diferente, é um conflito.
- **Confiabilidade** (25/10/2026; o caso de referência é o RetroBat portátil num pen drive arrancado
  no meio do jogo):
  - **Conferência do conteúdo:** antes de enviar e antes de pôr no lugar do local, o cartão precisa
    estar inteiro: cabeçalho "Sony PS2 Memory Card Format", tamanho batendo com o número de clusters
    (com ou sem ECC) e diretório raiz no lugar (`Saves\Ps2MemoryCard`). Um cartão cortado, zerado ou
    sem formatar nunca vai para a nuvem.
  - **Conserto automático:** um cartão danificado neste PC é trocado pela versão boa mais nova da nuvem
    antes do jogo. A cópia danificada fica no histórico local, e uma tela avisa o que aconteceu. Se a
    nuvem não tiver versão, o cartão fica como está e a tela avisa antes de o emulador oferecer formatar.
  - **Versão danificada na nuvem:** é pulada; vale a versão boa anterior, que volta a ser a mais nova,
    para os outros PCs pararem de tropeçar nela.
  - **Pasta sumiu no meio do jogo** (pen drive arrancado, disco desconectado): nada é apagado na nuvem,
    e a trava continua renovada. Todos os cartões sumindo de uma vez também não conta como exclusão.
    Com a unidade fora, nada é planejado.
  - **Gravação segura no PC:** arquivo temporário, escrita forçada no disco e só então a troca. O
    `saves-sync.xml` mantém a versão anterior em `.bak` e, se os dois estiverem danificados, o SHA-1 dos
    dois lados decide.
  - **Restaurar:** `retroboat-cli saves history` lista todas as cópias (nuvem, conflitos, histórico deste
    PC) dizendo se cada uma está boa. `saves restore <cartão> [<cópia>]` volta qualquer uma, que vira a
    mais nova na nuvem. `saves verify` confere tudo sem mudar nada.
- **Ainda não:**
  - cartão em pasta (`folder`, `Mcdf01.ps2`): é avisado e não é sincronizado;
  - núcleo libretro `pcsx2` e PCSX2 1.6 (`pcsx2-16`), que gravam em outro lugar;
  - save states.

**Formato recomendado: CHD.** O PCSX2 e o RetroBat aceitam `.chd`. Converter os ISOs com o
`chdman` (`createdvd`) reduz tamanho e tempo de download sem perda; quanto, varia de jogo para
jogo. A verificação rápida do Retroboat reconhece CHD pela assinatura.

**ZIP na nuvem: extrair uma vez, na instalação.** O primeiro jogo válido da biblioteca (Clock Tower 3,
24/10/2026) é um `.zip` de 3,04 GB com um ISO de 4,29 GB dentro. O RetroBat abre `.zip` de PS2, mas o
emulatorLauncher monta ou descompacta o arquivo a cada vez que o jogo abre (`TryUnZipGameIfNeeded`):
são 4 GB de trabalho por partida. A instalação do Retroboat deve descompactar uma vez, como o Steam
faz, e guardar o ISO (ou CHD) na pasta de jogos local. Outro ponto: com a opção `decompressedfolders=keep`,
o emulatorLauncher descompacta em `<pasta de jogos>\.uncompressed`, ou seja, escreve dentro da pasta
de jogos. Isso não acontece no padrão do RetroBat, mas entra na lista de vigilância da RB-003.

**Identificar o jogo sem baixar.** Todo disco de PS2 tem um `SYSTEM.CNF` com o serial do jogo
(ex.: `SLUS-20312`). Lendo poucos setores do ISO pela pasta do Drive, o Retroboat pode mostrar o
serial antes do download. O serial dá o jogo exato para metadados e para casar com o banco do Redump.
Para ISO isso é viável; para CHD exige descompactar um trecho.

**Pré-requisitos (seus):**

1. **Jogos de PS2 válidos**, dumps dos seus discos. Comece com 3 ou 4, de preferência um pequeno
   (até 1 GB), para os testes rodarem rápido.
2. **BIOS do PS2**, dump do seu console, em `<RetroBat>\bios\pcsx2\bios\`. Sem ela o emulatorLauncher
   não abre o PCSX2 ("No BIOS found in bios/pcsx2/bios folder"). Os nomes aceitos estão na lista
   do `Pcsx2.Generator.cs` (ex.: `SCPH-70012_BIOS_V12_USA_200.BIN`, `scph39001.bin`).
3. **Um RetroBat instalado** para os testes de ponta a ponta. Quando o PCSX2 não está instalado,
   o emulatorLauncher se oferece para baixá-lo na primeira vez que um jogo de PS2 é aberto.
