# Changelog do Retroboat

Mudanças do fork em relação ao RetroBat oficial. O changelog do upstream continua em `/CHANGELOG.md` e não é editado aqui.

## Não lançado

- Base: RetroBat 8.4.0 (upstream `875e1f5`, 23/10/2026).
- RB-001: estrutura de gestão do fork (`retroboat/`).
- RB-006 (base): `retroboat-cli` lê a biblioteca na pasta do Google Drive para computador, instala
  jogos com progresso e retomada, e verifica integridade por amostragem (detecta arquivos zerados).
- RB-003 (base): biblioteca isolada (pasta de jogos própria, gamelists em
  `.emulationstation\gamelists`) e lançador `Retroboat.exe`.
- RB-013: BIOS na nuvem (`retroboat\bios` no Drive). O lançador organiza, sincroniza e valida as
  BIOS a cada início e mostra o resultado como notificação no EmulationStation.
- `retroboat/tools/package-app.sh`: coloca o Retroboat dentro de um build do RetroBuild.
- RB-004 (fase 1): os jogos do Drive aparecem no EmulationStation antes de serem baixados e são
  baixados com tela de progresso na primeira vez que se joga (`retroboat-launch.exe`). Um ZIP com um
  disco dentro é extraído uma vez, com conferência de CRC; um jogo corrompido na nuvem é avisado.
- RB-015: capas, imagens, nome e descrição dos jogos vêm da nuvem e aparecem antes do download,
  como no Steam (`retroboat\midia` no Drive). A mídia do scraper sai da pasta de jogos e vai para
  `.emulationstation\media`. Vídeos e manuais são opcionais (`[Cloud] Videos` e `Manuals` no
  `retroboat.ini`).
- RB-014: o tema padrão passa a ser o nosso fork do Carbon (`build.ini: theme_path`), que mostra
  o status do jogo no cabeçalho de todas as visões: "Na nuvem · BAIXAR" ou "Instalado · JOGAR".

### Nos forks (ainda não usados pelo build)

- RB-011: build Windows do EmulationStation no fork `retroboat-emulationstation`, com o mesmo
  pacote do RetroBat oficial, publicado no release `continuous-master`. Falta cadastrar as
  credenciais de API antes de trocar o ES no `build.ini`. RB-016: correção para compilar com
  o Visual Studio 2026.
