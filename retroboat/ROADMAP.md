# Roadmap do Retroboat

Duas etapas, nesta ordem. Cada entrega tem ID no [registro de patches](PATCHES.md) e é
classificada pelos critérios do [FORK.md](FORK.md) antes de começar.

## Etapa 1: cloud-first

Jogos, saves, memory cards e configurações na nuvem, no modelo do Steam: a nuvem é a fonte da
verdade e tudo roda localmente. O transporte é o app Google Drive para computador (`G:\`), sem API
e sem login próprio. Desenho completo em [design/cloud-first.md](design/cloud-first.md).

| Marco | Entrega | Patches | Status |
|---|---|---|---|
| M0 | Fundações: build, provedor de pasta sobre o Google Drive para computador, verificação de integridade | RB-006 | base pronta; falta empacotar no build |
| M1 | Biblioteca isolada: pasta de jogos própria, metadados fora dela | RB-003 | base pronta; falta teste com o ES real e empacotar |
| M2 | Biblioteca na nuvem: instalar, fila, desinstalar, verificar | RB-004 | não iniciado |
| M3 | Saves e memory cards na nuvem, com conflito e histórico | RB-009 | não iniciado |
| M4 | Perfil e configurações na nuvem, PC novo em poucos passos | RB-005 | não iniciado |
| M5 | Interface nativa de nuvem no ES (fork e build do ES) | RB-010, RB-011 | não iniciado |

M0 e M1 podem andar em paralelo. Código em [app/](app/README.md).

### Estado em 24/10/2026

- **Pronto e testado** (42 testes automáticos, mais testes manuais no `G:\` e numa instalação simulada):
  - CLI `retroboat-cli` com `where`, `ls`, `verify`, `install` e `uninstall` sobre a pasta do Drive;
  - instalação retomando depois de o processo ser derrubado (testado no Drive real, a cerca de 6 a 9 MB/s);
  - verificação rápida, que apontou os 5 ISOs zerados do Drive em cerca de 11 s por jogo;
  - biblioteca isolada (overlay, `roms=`, gamelists locais) e lançador `Retroboat.exe`.
- **Validado com o ES real** (release `continuous-master` de 22/10/2026 + emulatorLauncher `continuous`):
  - o ES listou os jogos da pasta nova e ignorou os da pasta padrão;
  - o sistema `gw` (pasta `gameandwatch`) funcionou;
  - o gamelist movido foi usado, com nome e favorito preservados;
  - um favorito alterado pela API foi gravado, ao sair, em `.emulationstation\gamelists\snes`;
  - nenhum arquivo além dos jogos foi criado ou alterado na pasta de jogos.
- **Falta no M0/M1:**
  - empacotar o `Retroboat.exe` e o CLI no build do RetroBat;
  - atalho e instalador apontando para o `Retroboat.exe`;
  - mídia do scraper fora da pasta de jogos (1c).

## Etapa 2: redesign visual e de UX

Começa depois da etapa 1. Aproveita o fork e o build do ES montados no M5. A primeira escolha de
desenho será onde cada mudança visual mora:

- **tema novo:** C0, sem conflito com o upstream;
- **mudança de comportamento no ES:** C1 a C3, no fork do ES.
