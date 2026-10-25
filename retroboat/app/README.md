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
```

Códigos de saída: 0 ok, 1 erro, 2 uso incorreto, 3 jogo corrompido encontrado, 130 cancelado.

## Lançador

`Retroboat.exe` fica na pasta do RetroBat (ou em `<RetroBat>\retroboat\`). No primeiro uso, pergunta
onde ficam os jogos e grava em `<RetroBat>\retroboat\retroboat.ini`. A cada início, regera o overlay
de sistemas, a chave `roms=` e os gamelists locais, e então abre o `retrobat.exe` repassando os
argumentos. Se a preparação falhar, o RetroBat abre mesmo assim com a configuração anterior.

Argumentos próprios: `--setup` (escolher a pasta de novo), `--no-launch` (só preparar) e
`--retrobat <pasta>`. Log em `<RetroBat>\retroboat\logs\launcher.log`.

Antes de abrir o RetroBat, o lançador também sincroniza com a nuvem: BIOS, a biblioteca (jogos da
nuvem como marcadores) e o conteúdo dos jogos. A mídia que o scraper do ES grava na pasta de jogos
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
