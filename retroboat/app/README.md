# Retroboat (aplicativo)

Código do que o Retroboat acrescenta ao RetroBat. Fica fora de `system/` e não altera nenhum arquivo
do upstream (camada C0). Desenho em [../design/cloud-first.md](../design/cloud-first.md).

| Projeto | Saída | O que é |
|---|---|---|
| `src/Retroboat.Core` | `Retroboat.Core.dll` | Biblioteca na nuvem (pasta do Google Drive para computador), instalação com progresso e retomada, verificação de integridade, biblioteca isolada (RB-003) |
| `src/Retroboat.Cli` | `retroboat-cli.exe` | Linha de comando para testar e operar a biblioteca |
| `src/Retroboat.Launcher` | `Retroboat.exe` | Lançador: prepara a biblioteca, sincroniza BIOS e jogos da nuvem e abre o `retrobat.exe` |
| `src/Retroboat.Launch` | `retroboat-launch.exe` | Fica na frente do emulatorLauncher: BAIXAR põe o jogo na fila e volta para o ES. Com `--downloads`, é o serviço que baixa a fila e mostra o andamento no cabeçalho |
| `tests/Retroboat.Core.Tests` | — | Testes (xUnit) |

.NET Framework 4.8, igual ao resto do RetroBat. Para compilar, basta o .NET SDK (10 ou mais novo):

```bash
dotnet build retroboat/app/Retroboat.sln -c Release
```

```bash
dotnet test retroboat/app/Retroboat.sln
```

`dotnet test` só recompila os testes e as dependências deles. Depois de mudar o Core, rode
`dotnet build` antes de usar o CLI ou o lançador.

## Gerar uma build do Retroboat

1. `RetroBuild.exe`, opção 1: baixa ES, emulatorLauncher, RetroArch, tema e BIOS livres e monta
   `build\`. O RetroBuild sempre clona todos os submódulos do git (inclusive os bezels, 1,3 GB),
   mesmo com `get_decorations=0` no `build.ini`; essa opção só evita copiá-los para o build.
2. `bash retroboat/tools/package-app.sh`: compila em Release e copia `Retroboat.exe`, o CLI e a DLL
   para `build\retroboat\`. Se o PlayStation-X ainda não estiver no build, adiciona a versão 43.1
   para que o tema esteja disponível no menu após a instalação.
3. `RetroBuild.exe`, opção 2 ou 3: gera o `.zip` ou o instalador.

Para jogar, abra `build\retroboat\Retroboat.exe`. Atalho e instalador apontando para ele ainda faltam (M1).

## CLI

```
retroboat-cli cloud where                                  onde está a pasta do Retroboat no Drive
retroboat-cli cloud ls [sistema]                           jogos na nuvem (só metadados, nada é baixado)
retroboat-cli cloud verify [sistema | sistema/jogo]        verificação rápida por amostragem (--full: SHA-1 e CRC-32)
retroboat-cli cloud install sistema/jogo --to <pasta>      copia para a pasta de jogos local, com progresso e retomada
retroboat-cli cloud uninstall sistema/jogo --to <pasta>    apaga a cópia local; o jogo continua na nuvem
retroboat-cli library apply --retrobat <pasta> [--roms <pasta>] [--dry-run]
retroboat-cli bios status --retrobat <pasta>              BIOS que cada emulador vai usar, validada pelo conteúdo
retroboat-cli bios sync --retrobat <pasta> [--dry-run]    organiza, baixa e envia BIOS (retroboat\bios no Drive)
retroboat-cli bios organize [--dry-run]                   só organiza as BIOS soltas no Drive
retroboat-cli saves status --retrobat <pasta>             memory cards: o que mudou aqui, na nuvem ou nos dois
retroboat-cli saves sync --retrobat <pasta> [--keep local|cloud]   sincroniza (conflitos só com --keep)
retroboat-cli saves history <cartão> --retrobat <pasta>   versões do cartão na nuvem
retroboat-cli saves games --retrobat <pasta> [--roms <pasta>]   ESTADO DA NUVEM de cada jogo (só este PC, sem o Drive)
retroboat-cli config status --retrobat <pasta>            configurações: o que iria e viria, sem mudar nada
retroboat-cli config sync --retrobat <pasta>              sincroniza opções do ES, controles, coleções e emuladores
retroboat-cli stats sync --retrobat <pasta> [--roms <pasta>]   favoritos e tempo jogado (com o ES fechado)
retroboat-cli themes status --retrobat <pasta>            temas suportados com ou sem o status do Retroboat
retroboat-cli emulators status --retrobat <pasta> [--emulator X [--core Y]]   emuladores dos consoles com jogos: instalado, atualização, tamanho
retroboat-cli emulators install --retrobat <pasta> --emulator X [--core Y]   instala ou atualiza (última versão), com a conferência
retroboat-cli emulators check --retrobat <pasta> --emulator X [--core Y]   só a verificação de compatibilidade
retroboat-cli media search --retrobat <pasta> --system ps2 --file ARQUIVO|--name NOME [--profile P]   busca um jogo pelo PERSONALIZADO (XML; o PROCURAR MÍDIAS do ES usa; RB-038)
retroboat-cli media systems [--retrobat <pasta>]   sistemas do ES que alguma fonte de mídia conhece
retroboat-cli media fields                         campos e fontes do PERSONALIZADO, para a tela PRIORIDADE DAS FONTES do ES
retroboat-cli integrations status --retrobat <pasta>   estado de cada integração, da última verificação (RB-037)
retroboat-cli integrations check --retrobat <pasta> [--force]   testa as chaves e troca sozinho a que parou, pelo repositório do RetroBat
retroboat-cli themes apply --retrobat <pasta>             põe o status de volta nos temas (Carbon, PlayStation-X)
retroboat-cli settings get --retrobat <pasta>             estado da biblioteca e opções principais, em linhas Chave=valor
retroboat-cli settings set <chave> 0|1 --retrobat <pasta> Videos, Manuals, Automatic ou AutoOpen (o menu NUVEM usa)
retroboat-cli settings set MonitorIndex N --retrobat <pasta>   monitor do ES no retrobat.ini (0 = o principal do Windows; RB-029)
retroboat-cli settings set AudioOutput <id> --retrobat <pasta>   troca a saída de som padrão do Windows e lembra dela (RB-030)
retroboat-cli settings request-setup --retrobat <pasta>   o setup abre quando o ES fechar
```

`--pc NOME` nos comandos `saves`, `config` e `stats` faz a pasta valer como outro PC, para testar dois PCs numa máquina só.
`saves lock` e `saves unlock` existem só para testar a trava.

Códigos de saída: 0 ok, 1 erro, 2 uso incorreto, 3 jogo corrompido encontrado, 130 cancelado.

## Lançador

`Retroboat.exe` fica na pasta do RetroBat (ou em `<RetroBat>\retroboat\`). No primeiro uso abre o setup (RB-021), em
tela cheia e com controle: a biblioteca da nuvem, a pasta de jogos deste PC e as opções principais, gravadas em
`<RetroBat>\retroboat\retroboat.ini`. O setup também abre com a biblioteca em duas contas do Drive, com o Drive aberto
sem biblioteca nenhuma (aí oferece criar uma, só depois de uma segunda busca e com confirmação), com `--setup` e pelo
menu NUVEM do nosso ES. A cada início, regera o overlay de sistemas, a chave `roms=` e os gamelists locais, e então abre
o `retrobat.exe` repassando os argumentos. Se a preparação falhar, o RetroBat abre mesmo assim com a configuração
anterior.

Argumentos próprios: `--setup` (abrir o setup), `--no-launch` (só preparar, sem perguntar
nada e sem ficar em segundo plano), `--steam-switch` (só o vigia do botão Xbox, aberto pelo evento
"start" do ES e fechado junto com ele; RB-040), `--media` (só baixar as mídias secundárias em segundo plano; RB-041)
e `--retrobat <pasta>`. Log em `<RetroBat>\retroboat\logs\launcher.log` (o `--media`, em `midias.log`).

**Menu NUVEM (nosso ES).** Mostra o estado do Drive e da biblioteca e a pasta de jogos, e muda as quatro opções
principais pelo `retroboat-cli settings`. O `Retroboat.exe` em segundo plano percebe a mudança na hora; ligar vídeos,
manuais ou capas automáticas busca o que faltava com o ES parado. "Trocar biblioteca ou pasta de jogos" fecha o ES; o
`Retroboat.exe` abre o setup e reabre o RetroBat.

Com o Google Drive fechado, o lançador pergunta se abre: ao iniciar, e o `retroboat-launch` ao apertar BAIXAR
ou ao abrir um jogo com memory card. "Sempre abrir, sem perguntar" grava `AutoOpen=1`. Depois de abrir o
RetroBat, o `Retroboat.exe` fica em segundo plano, sem janela, até o ES fechar: quando o Drive conecta, sincroniza e
recarrega o ES; quando desconecta, recarrega o ES com o X na nuvem. Com jogo aberto ou scraper rodando, espera.

No Drive, `retroboat\jogos` tem as mesmas pastas de console do `roms` do RetroBat (`psx`, `n64`, `gc`,
`gameandwatch`...), uma por sistema do `es_systems.cfg`: para pôr um jogo na nuvem, basta soltá-lo na pasta do
console. O lançador cria as que faltam a cada início (um sistema novo do RetroBat ganha a pasta sozinho) e nunca
apaga nenhuma. Ele nunca cria a pasta `retroboat`: uma pasta que não aparece pode ser o Drive ainda carregando ou
outra conta montada primeiro, e criar ali faria uma segunda pasta vazia no Drive.

As pastas de console ficam só no Drive. Na pasta de jogos deste PC, o lançador não cria nenhuma: a de um console surge
com o primeiro jogo dele (o download ou o marcador de um jogo da nuvem). Nenhuma pasta local é apagada.

**Biblioteca da nuvem (RB-021).** A pasta `retroboat` tem uma identidade, `retroboat\biblioteca.xml`, criada uma vez e
nunca reescrita, e o PC guarda o id em `[Cloud] Library`. Com o id, o lançador, o `retroboat-launch` e o `retroboat-cli
--retrobat` só usam a pasta que tem esse id, em qualquer letra e em qualquer conta. Se ela não aparece, ficam sem
nuvem, como com o Drive fechado, e nunca usam a pasta `retroboat` de outra conta. Sem id escolhido e com a pasta
`retroboat` em exatamente uma conta, o lançador adota essa biblioteca sozinho: lê o `biblioteca.xml` ou cria um (só
esse arquivo; nada mais muda no Drive). Com a pasta em duas contas ou em nenhuma, não escreve nada e a escolha fica
para o setup (desenho em `design/rb021-setup-e-configuracoes.md`). Numa biblioteca identificada, a pasta `jogos` também
é criada se faltar.

**BIOS na nuvem (RB-013).** `retroboat\bios` no Drive espelha `<RetroBat>\bios`, de qualquer console. Tudo o que está
lá vem para o PC. Do PC sobe toda BIOS conhecida que está na pasta certa; uma solta no Drive vai para a pasta que o
emulador usa. "Conhecida" é a lista do próprio RetroBat (`system\modules\rb_gui\bios_local.json`) mais os nomes de BIOS
de PS1 que faltam nela; os arquivos livres que já vêm com o RetroBat não sobem. Arquivo zerado nunca é copiado. Só a
BIOS do PS2 tem o conteúdo conferido e status no ES.

Antes de abrir o RetroBat, o lançador também sincroniza com a nuvem: BIOS, os logins do ES
(ScreenScraper, IGDB e RetroAchievements, em `retroboat\contas`; um PC sem login recebe o da
nuvem), as configurações (opções do ES, controles e emuladores, em `retroboat\configuracoes`), a biblioteca (jogos da nuvem como marcadores) e o conteúdo dos jogos. A mídia que o scraper do ES grava na pasta de jogos
vai para `.emulationstation\media\<sistema>`. Nome, descrição, capas e demais metadados que um PC
tem e os outros não vão para `retroboat\midia\<sistema>` no Drive. Nos outros PCs, eles preenchem
só os campos vazios. Imagens sempre vêm; vídeos e manuais são opcionais:

```ini
[Cloud]
; Vazio = detectar o Google Drive para computador.
Path=
; Id da biblioteca da nuvem deste PC (retroboat\biblioteca.xml). Vazio = escolher na próxima abertura.
Library=
; 1 = baixar também os vídeos dos jogos.
Videos=0
; 1 = baixar também manuais e revistas (PDF).
Manuals=0
; 1 = abrir o Google Drive sem perguntar quando ele estiver fechado.
AutoOpen=0

[Media]
; 1 = capas, descrição, gênero, data e demais informações automáticas para jogos sem elas.
Automatic=1

[Bios]
; 1 = o Retroboat já desligou o aviso de BIOS do ES neste PC (só acontece uma vez).
EsCheckHandled=1
```

Como na Steam, a página de um jogo mostra o conteúdo dele antes do download. Para todo jogo da
biblioteca, o lançador preenche o que estiver vazio no gamelist: nome sem as etiquetas do arquivo,
descrição, gênero, data, desenvolvedora, editora e jogadores. As fontes não pedem conta nem chave:
a base do libretro (pelo nome Redump; no PS2, descrição em inglês e o resto) e o Wikidata com o
resumo da página do jogo na Wikipédia em português. O scraper do ES fica por último: nome, gênero e
desenvolvedora vêm primeiro do Wikidata; editora, data e jogadores, do libretro; o que nenhuma fonte tem
continua como estava. Cada jogo é consultado no máximo uma vez por mês, e o resultado vai para o
Drive, então os outros PCs não precisam buscar. Consultas em `retroboat\cache\informacoes`.

As capas automáticas não precisam de conta: o lançador procura o nome do arquivo (padrão
No-Intro/Redump) nos thumbnails do libretro, guarda a lista de nomes de cada sistema por uma semana
em `retroboat\cache\libretro` e envia as imagens para o Drive. Descrição e vídeo vêm do scraper do
ES, que precisa de uma conta gratuita no ScreenScraper (menu Procurar mídias de jogo, aba Contas).

### Mídias em segundo plano (RB-041)

A abertura e o ATUALIZAR LISTAS DE JOGOS só esperam o essencial: os textos e as imagens que aparecem na lista de jogos
de qualquer tema (capa, tela do jogo, logo e fan art), baixados vários ao mesmo tempo (um por vez de cada servidor; do
Drive, até 6 cópias juntas). O resto vai para a fila `<RetroBat>\retroboat\estado\midias-em-segundo-plano.xml` (só neste
PC): vídeo, manual e as imagens da página do jogo (caixa traseira, disco, tela de título, mapa, moldura). Vale para os
jogos novos (fontes da internet) e para um PC sem nenhuma mídia (cópia do Drive na primeira abertura). A divisão fica em
`Retroboat.Core/Media/Background/MediaPriority.cs`.

O `Retroboat.exe --media` baixa a fila sem janela, com prioridade baixa e sem aviso: espera enquanto há uma
sincronização, um jogo baixando ou um jogo aberto; o vídeo vai direto para o disco e continua de onde parou. A cada
poucos segundos, o que chegou entra no gamelist e, com o ES aberto e livre, aparece nele na hora (POST `/addgames` só
com os campos novos; o que o ES tem na memória, como partidas e favoritos, fica). Um campo que a pessoa mudou nesse
meio-tempo (PROCURAR MÍDIAS) vale mais. Sem internet, tenta de novo em 1, 5, 15 e 60 minutos e depois em 4 horas, e
desiste na sexta vez. Ele abre depois de cada sincronização e, se sobrou fila (o PC desligou no meio), pelo script
`scripts\start\retroboat-midias-nowait.bat` quando o ES abre. O que veio da internet vai para o Drive quando o ES fecha
(ou na próxima sincronização). Log em `retroboat\logs\midias.log`.

## Emuladores (RB-033)

O Retroboat instala e atualiza os emuladores, sempre na **última versão** do repositório do RetroBat
(`https://www.retrobat.ovh/repo/win64/stable/emulators/`). Eles vão para `emulators\<nome>` e os cores para
`emulators\retroarch\cores`, como o RetroBat faria. Desenho completo: `retroboat/design/rb033-emuladores.md`.

- **Junto com o jogo:** o download de um jogo da nuvem traz o emulador que ele vai usar, se falta ou tem versão nova. A ordem
  da escolha é a do ES: o jogo, depois o sistema, depois o padrão. A barra soma os dois. Se o emulador falhar, o jogo fica
  instalado assim mesmo.
- **Ao abrir um jogo** (`retroboat-launch`):
  - se o emulador falta, aparece a tela "Este jogo precisa do X", com "Baixar e jogar" ou "Agora não", e o andamento com B
    para cancelar;
  - uma versão nova é instalada sem perguntar;
  - sem internet, o jogo abre com o que está instalado.
- **Conferência do arquivo:** tamanho igual ao anunciado, `7za t` (CRC de cada arquivo), extração numa pasta temporária e
  programa válido do Windows (MZ/PE). Só então os arquivos vão para o lugar, sem trocar os que o RetroBat nunca sobrescreve
  (`system\upgrade.xml`).
- **Compatibilidade** (`Emulators/EmulatorCompatibility.cs`, arquivo próprio): antes e depois de cada jogo, o app confere o
  que uma versão nova pode ter mudado. Cada problema tem um código:
  - `EXECUTAVEL_MUDOU`, `EXECUTAVEL_INVALIDO` e `RETROARCH_AUSENTE`: o programa;
  - `REPOSITORIO_MUDOU`: o repositório;
  - `SAVE_FORA_DA_NUVEM`: um save gravado fora de `saves\`, como a VMU do Flycast em `bios\dc`;
  - `PASTA_DE_SAVES_MUDOU`: a pasta de memory cards do PCSX2;
  - `CONFIGURACAO_SUMIU`: as configurações sincronizadas.

  Tudo vai para `retroboat\logs\emuladores.log`, em bloco, com esperado e achado. O usuário vê uma tela com o problema em
  português, uma vez por versão; o que impede o jogo aparece sempre, com "Voltar" e "Abrir mesmo assim".
- **Estado:** em `retroboat\estado\`:
  - `emuladores-instalados.xml`: o que o Retroboat instalou, com a versão anunciada e a data do pacote no servidor;
  - `emuladores-servidor.xml`: o cache das consultas, por 6 h, com 10 min de espera depois de uma falha de conexão;
  - `emuladores-avisos.xml`: os avisos já mostrados.
- **Catálogo:** `Emulators/EmulatorCatalog.xml` diz qual pacote cada emulador do ES abre e que executável o emulatorLauncher
  procura. Se uma versão nova renomear o executável, corrija o `exe` ali.

## Configurações na nuvem

As configurações dos emuladores e dos jogos vão para `retroboat\configuracoes` no Drive e voltam em todo PC:

- **opções do ES** (`es_settings.cfg`): globais, por sistema e por jogo, que o emulatorLauncher aplica no emulador a cada
  jogo, e as preferências do ES;
- **controles** configurados no ES (`es_input.cfg`);
- **PCSX2**: o `PCSX2.ini` chave a chave, os ajustes por jogo (`gamesettings`) e os perfis de controle (`inputprofiles`);
- **os outros emuladores**, pela regra genérica: todo emulador já baixado em `emulators\` (RetroArch, Dolphin,
  DuckStation, Flycast…). `.ini`, `.cfg`, `.conf`, `.opt` e `.rmp` vão chave a chave; `.json`, `.xml`, `.yml` e `.toml`
  vão inteiros, só sem caminho e sem login. Pastas de BIOS, firmware, chaves, NAND, saves, cache, shaders, jogos
  instalados e o que vem com o emulador ficam de fora.

Fica em cada PC o que depende da máquina: tela, monitor, áudio, placa de vídeo, pastas, janelas, o tema escolhido, o controle
de cada jogador e as senhas (as contas têm sincronização própria). Um PC novo recebe tudo da nuvem; se a mesma opção mudou em
dois PCs, vale a mudança mais nova.

- **Ao abrir o `Retroboat.exe`:** tudo, antes do ES abrir.
- **Antes do jogo:** o emulador recebe o que mudou em outro PC e envia o que mudou aqui. O que mudou nos menus do ES vai para a
  nuvem durante o jogo.
- **Depois do jogo:** o que mudou nos menus do emulador vai para a nuvem.
- **Quando o ES fecha:** o ES recebe o que outro PC mudou, e isso vale na próxima abertura. Com o ES aberto, os arquivos dele
  não são gravados, porque ele os regrava.

Cada PC grava só o próprio `<PC>.xml` na nuvem. O estado da última sincronização fica em
`<RetroBat>\retroboat\configuracoes-sync`, e uma cópia de cada arquivo, antes de ser trocado, em
`<RetroBat>\retroboat\historico\configuracoes` (as 10 últimas). Um arquivo apagado ou um emulador reinstalado recebe as
configurações da nuvem de volta, sem apagar nada nos outros PCs. Emuladores novos entram sozinhos pela
regra genérica (`Config\AutoEmulatorConfig.cs`); um emulador com armadilha própria ganha regra em `EmulatorConfigRules`.
Por isso todos os sistemas abrem pelo `retroboat-launch`.

## Downloads em segundo plano

Apertar BAIXAR não prende mais a tela. O `retroboat-launch` põe o jogo na fila e volta para o ES em instantes, com código
de saída 1, para o ES não contar como partida nem mostrar erro. A pessoa continua navegando, abre outros jogos ou põe mais
jogos na fila.

- **Fila:** `retroboat\estado\downloads.xml`, um jogo por vez, na ordem em que entraram. Sobrevive ao fechamento do ES.
- **Serviço:** `retroboat-launch --downloads`, sem janela própria, um só por vez. Sobe quando a fila tem jogo: no BAIXAR, ao
  abrir qualquer jogo e na abertura do `Retroboat.exe`. Fecha quando a fila esvazia. Log em `retroboat\logs\downloads.log`.
- **Cancelar:** apertar de novo em um jogo da fila abre a tela com "Continuar baixando" e "Cancelar o download". Num jogo em
  arquivo solto, o que já foi copiado fica para retomar; um ZIP recomeça (o deflate não permite continuar no meio).
- **Sem o Drive:** o jogo fica em AGUARDANDO e continua sozinho quando o Drive voltar, inclusive se ele cair no meio.
- **ES fechou:** o serviço para em até 15 s e o jogo fica na fila; continua na próxima abertura.
- **Com jogo aberto:** o download continua, com prioridade baixa, e a região do cabeçalho fica escondida. O PRONTO espera a volta ao ES.
- **Quando termina:** o jogo vira JOGAR na hora, sem recarregar o ES nem mexer na posição do usuário (`POST /addgames`).
  Se não deu certo, o motivo aparece como notificação no ES.

O tema mostra BAIXANDO porque existe `retroboat\estado\baixando\<arquivo do jogo>`. O andamento fica na
[região do cabeçalho](../design/design-system-steam.md#46-região-de-ícones-do-cabeçalho-download), uma janela do Retroboat
por cima do ES, sem foco e sem clique, porque o tema do ES não recebe dados ao vivo. Exige o ES em tela cheia sem borda, que é
como o RetroBat abre (`FullscreenBorderless=1`).

Com o nosso ES (RB-010), ele mesmo atende o A num jogo da nuvem, sem abrir o jogo, e chama `retroboat-launch --baixar "<jogo>"`
ou `--cancelar "<jogo>"` (0 = feito, 1 = nada mudou). O serviço percebe esse ES pelo `GET /retroboat/download` e manda o
andamento por `POST /retroboat/download`, no máximo 4 vezes por segundo e com um sinal a cada 2 s; a região do cabeçalho, o
status ao vivo e a atualização das listas ficam com o ES, sem a janela e sem o `POST /addgames`.

A fila inteira pode ser pausada (RB-028): `paused="1"` no `estado\downloads.xml`, por `retroboat-launch --pausar` e `--retomar`
(menu do jogo no nosso ES) ou pela pergunta ao apertar de novo num jogo da fila (ES oficial). O jogo atual para onde está, sem
sair da fila, e o cabeçalho mostra PAUSADO; ao retomar, a cópia continua do `.retroboat-part`. A pausa continua depois de fechar
o ES e acaba quando a fila esvazia.

Um arquivo que um navegador ainda está baixando direto na pasta do console no Drive (`.opdownload`, `.crdownload`, `.part` e
outros, ou o nome final ao lado de um deles) não é jogo (RB-027): entra quando o download termina.

## Desinstalar e verificar pelo ES

Com o nosso ES, o menu do jogo (segurar A) de um jogo da nuvem troca o JOGAR e o EXCLUIR JOGO por entradas do Retroboat. O ES
chama o `retroboat-launch` e mostra o que ele responde (`estado\resposta-<pedido>.txt`).

| No menu | Quando | O que faz |
|---|---|---|
| BAIXAR · tamanho | o jogo não está no PC | o mesmo que o A: entra na fila |
| PAUSAR DOWNLOADS ou RETOMAR DOWNLOADS · jogos na fila | na fila | `--pausar` ou `--retomar`: a fila inteira (RB-028) |
| CANCELAR DOWNLOAD · andamento | na fila | `--cancelar`, sem perguntar de novo |
| INDISPONÍVEL · motivo | corrompido na nuvem | explica o motivo |
| VERIFICAR INTEGRIDADE | o jogo está no PC | `--verificar`: compara com a nuvem, com a porcentagem na tela (`estado\verificando`) |
| DESINSTALAR · espaço ocupado | o jogo está no PC (interface completa) | pergunta e roda `--desinstalar` |

- **`--desinstalar "<jogo>"`:** confere no Drive (perguntando se ele estiver fechado) que a nuvem tem uma cópia boa, apaga a deste
  PC e põe o marcador: o jogo vira BAIXAR, com a mídia. Os saves ficam. 0 = desinstalado; 1 = ficou (sem o Drive, jogo fora da
  nuvem ou zerado lá).
- **`--verificar "<jogo>"`:** disco de ZIP, CRC-32 da cópia inteira contra o do ZIP; outro arquivo, tamanho e 32 blocos de 256 KB
  contra a nuvem; jogo em pasta, lista de arquivos, tamanhos e blocos do maior. Sem o Drive, só confere se a cópia não está zerada.
  0 = confere; 3 = danificada (o ES oferece REPARAR); 1 = não deu para verificar ou reparar.
- **`--reparar "<jogo>"`:** desinstala e põe na fila de downloads.

## Status nos temas

O status do Retroboat (JOGAR/BAIXAR, ícone de nuvem no cabeçalho) é um `retroboat\status.xml` dentro
da pasta do tema mais uma linha de include no `theme.xml` dele. O instalador de temas do ES apaga a
pasta do tema e baixa o oficial ao instalar, reinstalar ou atualizar, e o status some junto. Por
isso o app leva os `status.xml` dos temas suportados (Carbon e PlayStation-X, recursos do
`Retroboat.Core.dll`, em `src\Retroboat.Core\Themes\Overlays`) e os põe de volta:

- **A cada abertura do `Retroboat.exe`**, antes do ES. Lê só o `theme.xml` e o `status.xml` de cada
  tema suportado (alguns milissegundos) e grava apenas o que falta ou mudou. As imagens, vídeos e
  músicas do tema não são tocados.
- **Ao escolher um tema no menu do ES**: o lançador grava
  `.emulationstation\scripts\theme-changed\retroboat-temas.bat`, que roda `retroboat-cli themes apply`.
  O ES roda esse evento antes de recarregar o tema.

Um tema reinstalado pelo ES com ele já escolhido fica sem o status até a próxima abertura pelo
`Retroboat.exe`. Se a versão instalada do PlayStation-X não for a da qual as posições foram copiadas
(hoje a 43), o status entra mesmo assim e o log avisa para conferir o layout.

Os `status.xml` são editados e testados nos clones dos temas (`..\es-theme-carbon`,
`..\es-theme-PlayStation-X`) e copiados para cá. O teste `Bundled_overlays_match_the_theme_clones`
falha se a cópia do app ficar diferente da do clone.

## Saves de todos os sistemas

A pasta de saves de cada sistema (`<RetroBat>\saves\<sistema>`, com as subpastas de cada emulador) vai para
`retroboat\saves\<sistema>` no Drive, do mesmo jeito que os memory cards abaixo: antes do jogo puxa, durante
envia cada gravação, ao fechar envia o resto e libera a trava, e o `Retroboat.exe` envia ao abrir o que ficou de uma
partida sem o Drive. Vale para qualquer emulador: saves do jogo (`.srm`), memory cards do DuckStation e do Beetle PSX,
saves do GameCube no Dolphin, VMUs do Dreamcast, NVRAM e EEPROM do arcade e save states.

- **Fica no PC:** pastas de BIOS, firmware, chaves, jogos instalados, capas, capturas e caches; arquivos de mais de
  256 MB.
- **O que veio com o RetroBat** não sobe: só entra o que mudou depois que a sincronização começou neste PC, ou o que
  já está na nuvem. Num PC novo, o arquivo de fábrica dá lugar ao da nuvem sem perguntar.
- **Na nuvem** ficam as 10 versões mais novas de cada arquivo; cópias locais antes de trocar, em
  `<RetroBat>\retroboat\historico` (as 5 últimas).
- **Ainda fora:** PS3, Switch, Wii U, Xbox, Xbox 360, Vita, PS4 e 3DS, que guardam jogos instalados, firmware ou chaves
  junto com os saves.
- Com o Drive fechado, só pergunta se abre o Drive quando o sistema já tem saves sincronizados neste PC (ou é PS2).
- **ESTADO DA NUVEM na página do jogo (RB-032):** a cada sincronização (ao abrir o `Retroboat.exe`, antes do jogo, a cada save
  enviado durante o jogo, ao fechar o jogo e depois de um jogo com o Drive fechado), o Retroboat grava o estado dos saves de
  cada jogo em `retroboatestadosaves-jogos.xml`, e o nosso ES mostra ao lado do JOGAR: Atualizada (com a data da versão na
  nuvem), Não sincronizada, Conflito, Chegando de outro PC ou Nenhum save ainda. Um save é do jogo quando o nome do arquivo
  começa com o nome do arquivo do jogo (`Jogo.srm`, `Jogo_1.mcd`, `Jogo.state1`) ou uma pasta tem esse nome; o resto (o
  `Mcd001.ps2`, a VMU do Dreamcast) é do console e conta para os jogos dele que já foram jogados. Só olha este PC e o
  `saves-sync.xml`: não lê o Drive. O "na nuvem" é a pasta do Google Drive para computador; o envio dela para o Google é
  do próprio Drive.

## Memory cards

Os memory cards do PS2 são do emulador: o PCSX2 cria, escolhe e grava os cartões em
`<RetroBat>\saves\ps2\pcsx2\memcards`, e o emulatorLauncher escolhe o arquivo de cada slot pela opção do ES.
O Retroboat sincroniza todo `*.ps2` dessa pasta com `retroboat\saves\ps2\pcsx2\memcards` no Drive:

- **antes do jogo**, o `retroboat-launch` puxa o cartão mais novo. Se outro PC estiver jogando com os
  cartões, ou se o mesmo cartão mudou aqui e na nuvem, ele pergunta antes, numa tela feita para controle;
- **durante o jogo**, cada gravação vai para a nuvem uns 3 s depois que o emulador para de gravar;
- **ao fechar**, envia o que faltar e libera a trava;
- **ao abrir o `Retroboat.exe`**, envia o que ficou de uma partida sem o Drive.

Cada gravação é uma versão nova no Drive (`<cartão>\<data>-<PC>.gz`); nada é sobrescrito. Antes de
trocar ou apagar um cartão local, uma cópia vai para `<RetroBat>\retroboat\historico`. O estado da
última sincronização fica em `<RetroBat>\retroboat\saves-sync.xml`. Cartões em pasta (opção `folder`)
ainda não são sincronizados. Log em `<RetroBat>\retroboat\logs\launch.log`.

Feito para aguentar um RetroBat portátil num pen drive arrancado no meio do jogo:

- um cartão só vai para a nuvem (ou substitui o local) se estiver inteiro: cabeçalho, tamanho e
  diretório raiz de memory card de PS2;
- um cartão danificado neste PC volta da nuvem antes do jogo, com aviso; uma versão danificada na
  nuvem é pulada;
- se a pasta some durante o jogo, nada é apagado na nuvem;
- as gravações locais vão primeiro para um temporário forçado no disco.

Para recuperar à mão:

```
retroboat-cli saves verify --retrobat <pasta>                       confere cartões e cópias, sem mudar nada
retroboat-cli saves history Mcd001.ps2 --retrobat <pasta>           todas as cópias, e se cada uma está boa
retroboat-cli saves restore Mcd001.ps2 [<id>] --retrobat <pasta>    volta uma cópia (padrão: a boa mais nova da nuvem)
```
