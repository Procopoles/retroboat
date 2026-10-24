# Retroboat (aplicativo)

Código do que o Retroboat acrescenta ao RetroBat. Fica fora de `system/` e não altera nenhum arquivo
do upstream (camada C0). Desenho em [../design/cloud-first.md](../design/cloud-first.md).

| Projeto | Saída | O que é |
|---|---|---|
| `src/Retroboat.Core` | `Retroboat.Core.dll` | Biblioteca na nuvem (pasta do Google Drive para computador), instalação com progresso e retomada, verificação de integridade, biblioteca isolada (RB-003) |
| `src/Retroboat.Cli` | `retroboat-cli.exe` | Linha de comando para testar e operar a biblioteca |
| `src/Retroboat.Launcher` | `Retroboat.exe` | Lançador: prepara a biblioteca e abre o `retrobat.exe` |
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

## CLI

```
retroboat-cli cloud where                                  onde está a pasta do Retroboat no Drive
retroboat-cli cloud ls [sistema]                           jogos na nuvem (só metadados, nada é baixado)
retroboat-cli cloud verify [sistema | sistema/jogo]        verificação rápida por amostragem (--full: SHA-1 e CRC-32)
retroboat-cli cloud install sistema/jogo --to <pasta>      copia para a pasta de jogos local, com progresso e retomada
retroboat-cli cloud uninstall sistema/jogo --to <pasta>    apaga a cópia local; o jogo continua na nuvem
retroboat-cli library apply --retrobat <pasta> [--roms <pasta>] [--dry-run]
```

Códigos de saída: 0 ok, 1 erro, 2 uso incorreto, 3 jogo corrompido encontrado, 130 cancelado.

## Lançador

`Retroboat.exe` fica na pasta do RetroBat (ou em `<RetroBat>\retroboat\`). No primeiro uso, pergunta
onde ficam os jogos e grava em `<RetroBat>\retroboat\retroboat.ini`. A cada início, regera o overlay
de sistemas, a chave `roms=` e os gamelists locais, e então abre o `retrobat.exe` repassando os
argumentos. Se a preparação falhar, o RetroBat abre mesmo assim com a configuração anterior.

Argumentos próprios: `--setup` (escolher a pasta de novo), `--no-launch` (só preparar) e
`--retrobat <pasta>`. Log em `<RetroBat>\retroboat\logs\launcher.log`.
