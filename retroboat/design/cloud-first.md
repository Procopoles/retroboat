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
| **Memory cards** | PS2 e GameCube usam cartões em pasta, com um arquivo por jogo, então a sincronização é por jogo. Cartões "inteiros" (um arquivo para vários jogos) sincronizam como unidade, com trava. |
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
| PCSX2 (PS2) | `saves\ps2\pcsx2\memcards\Mcdf01.ps2` (cartão em pasta) | por jogo (subpasta) | cartões `.ps2` inteiros: por unidade, com trava |
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

**A biblioteca do Drive hoje não tem nenhum jogo válido** (verificado em 24/10/2026):

- A estrutura já segue o formato do RetroBat: `G:\Meu Drive\retroboat\jogos\ps2\`. Os nomes de
  sistema precisam continuar iguais aos do RetroBat (`ps2`, `psx`, `snes`...).
- O registro `registro-transferencia-ps2-2026-10-24.json` lista 44 ISOs de PS2 (122 GB) vindos de
  `C:\Stuffs\recuperacao_nvme`. Os 44 foram marcados como "conteúdo integralmente zerado", devolvidos
  à origem e removidos do Drive.
- Mesmo assim, **2 deles continuam no Drive**: "187 - Ride or Die" e "Ben 10 - Protector of Earth".
  Amostrei 2 KB no início, no meio e no fim de cada um: são só zeros, e falta até a assinatura de
  ISO no setor 16. O registro diz que foram removidos, mas não foram. Não apaguei nada.
- Os nomes vieram com `_` no lugar de `,` (ex.: `(Europe_ Australia)`), herdado da recuperação.
  Isso atrapalha um pouco o scraper por nome.

Pré-requisitos:

1. **Google Drive para computador:** já instalado e funcionando (`G:\`). Não precisa de projeto no
   Google Cloud.
2. **Jogos válidos na biblioteca** para testar o M2 de verdade: pelo menos alguns por sistema, de
   preferência de sistemas diferentes (um de cartucho, um de CD, um de PS2).
3. **Autorização para instalar o .NET SDK** nesta máquina (hoje não há SDK nem Visual Studio).
4. **Um RetroBat instalado para testes.** A pasta `C:\RetroBat` existe, mas está vazia. Dá para
   instalar a versão oficial ou gerar uma pelo nosso build.
