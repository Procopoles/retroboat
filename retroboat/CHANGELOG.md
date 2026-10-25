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
