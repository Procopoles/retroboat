# Retroboat (aplicativo)

Código do que o Retroboat acrescenta ao RetroBat. Fica fora de `system/` e não altera nenhum arquivo
do upstream (camada C0). Desenho em [../design/cloud-first.md](../design/cloud-first.md).

| Projeto | Saída | O que é |
|---|---|---|
| `src/Retroboat.Core` | `Retroboat.Core.dll` | Biblioteca na nuvem (pasta do Google Drive para computador), instalação com progresso e retomada, verificação de integridade, biblioteca isolada (RB-003) |
| `src/Retroboat.Cli` | `retroboat-cli.exe` | Linha de comando para testar e operar a biblioteca |
| `src/Retroboat.Launcher` | `Retroboat.exe` | Lançador: prepara a biblioteca, sincroniza BIOS e jogos da nuvem e abre o `retrobat.exe` |
| `src/Retroboat.Launch` | `retroboat-launch.exe` | Fica na frente do emulatorLauncher; baixa o jogo da nuvem com tela de progresso antes de abrir |
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
   para `build\retroboat\`.
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
retroboat-cli themes status --retrobat <pasta>            temas suportados com ou sem o status do Retroboat
retroboat-cli themes apply --retrobat <pasta>             põe o status de volta nos temas (Carbon, PlayStation-X)
```

`--pc NOME` nos comandos `saves` faz a pasta valer como outro PC, para testar dois PCs numa máquina só.
`saves lock` e `saves unlock` existem só para testar a trava.

Códigos de saída: 0 ok, 1 erro, 2 uso incorreto, 3 jogo corrompido encontrado, 130 cancelado.

## Lançador

`Retroboat.exe` fica na pasta do RetroBat (ou em `<RetroBat>\retroboat\`). No primeiro uso, pergunta
onde ficam os jogos e grava em `<RetroBat>\retroboat\retroboat.ini`. A cada início, regera o overlay
de sistemas, a chave `roms=` e os gamelists locais, e então abre o `retrobat.exe` repassando os
argumentos. Se a preparação falhar, o RetroBat abre mesmo assim com a configuração anterior.

Argumentos próprios: `--setup` (escolher a pasta de novo), `--no-launch` (só preparar) e
`--retrobat <pasta>`. Log em `<RetroBat>\retroboat\logs\launcher.log`.

Antes de abrir o RetroBat, o lançador também sincroniza com a nuvem: BIOS, os logins do ES
(ScreenScraper, IGDB e RetroAchievements, em `retroboat\contas`; um PC sem login recebe o da
nuvem), a biblioteca (jogos da nuvem como marcadores) e o conteúdo dos jogos. A mídia que o scraper do ES grava na pasta de jogos
vai para `.emulationstation\media\<sistema>`. Nome, descrição, capas e demais metadados que um PC
tem e os outros não vão para `retroboat\midia\<sistema>` no Drive. Nos outros PCs, eles preenchem
só os campos vazios. Imagens sempre vêm; vídeos e manuais são opcionais:

```ini
[Cloud]
; Vazio = detectar o Google Drive para computador.
Path=
; 1 = baixar também os vídeos dos jogos.
Videos=0
; 1 = baixar também manuais e revistas (PDF).
Manuals=0

[Media]
; 1 = capa, tela e título automáticos (thumbnails do libretro) para jogos sem imagem.
Automatic=1
```

As capas automáticas não precisam de conta: o lançador procura o nome do arquivo (padrão
No-Intro/Redump) nos thumbnails do libretro, guarda a lista de nomes de cada sistema por uma semana
em `retroboat\cache\libretro` e envia as imagens para o Drive. Descrição e vídeo vêm do scraper do
ES, que precisa de uma conta gratuita no ScreenScraper (menu Procurar mídias de jogo, aba Contas).

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
