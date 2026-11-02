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
  `retroboat.ini`). Jogos sem imagem ganham capa, tela e título automáticos (thumbnails do
  libretro, sem conta), que vão para o Drive na mesma hora (`[Media] Automatic`).
- RB-005 (contas): os logins do ES (ScreenScraper, IGDB, RetroAchievements) vão para o Drive
  (`retroboat\contas`). Um PC sem login recebe o da nuvem ao abrir; uma troca de login chega aos
  outros PCs.
- RB-014: o tema padrão passa a ser o nosso fork do Carbon (`build.ini: theme_path`), no design da
  Steam (Big Picture): botão JOGAR ou BAIXAR com o status na seção do jogo, ícone de status nas
  visões em grade e ícone global de nuvem no cabeçalho.
  - Com o Drive fechado, um jogo da nuvem mostra BAIXAR desativado e "Abra o Google Drive para
    baixar", em vez de parecer pronto para baixar. Não é tratado como corrompido.
- RB-017: o tema PlayStation-X ganha o mesmo status: o "Iniciar" do jogo em foco vira JOGAR, BAIXAR
  ou INDISPONÍVEL (visões ps4Style e single), barra de ação na visão detalhada, ícone de status na
  faixa de ícones de todas as visões em grade e ícone global de nuvem no cabeçalho. Fica num clone
  local do tema (`..\es-theme-PlayStation-X`); "Atualizar" o tema pelo ES apaga os ajustes.
- RB-009 (memory cards do PS2): todo cartão que o PCSX2 usa vai para a nuvem
  (`retroboat\saves\ps2\pcsx2\memcards`). O cartão mais novo é puxado antes do jogo, cada
  gravação é enviada segundos depois e o resto ao fechar. Um cartão mudado em dois PCs abre uma tela
  para escolher qual manter, e a outra versão fica guardada. Um cartão em uso em outro PC mostra um
  aviso. Cada gravação fica no histórico do Drive e, antes de substituir um cartão, uma cópia fica
  em `retroboat\historico`. Comandos `retroboat-cli saves status | sync | history`.
  - Feito para um pen drive arrancado no meio do jogo: um cartão cortado ou danificado nunca vai para a
    nuvem e volta da nuvem antes do próximo jogo, com aviso na tela.
  - Uma versão danificada na nuvem é pulada.
  - A pasta sumindo no meio do jogo não apaga nada na nuvem.
  - `saves verify` confere tudo e `saves restore` volta qualquer cópia guardada.
- Design system do Retroboat (`design/design-system-steam.md`), com base no Steam Big Picture e
  nos tokens do próprio cliente Steam.

### Nos forks (ainda não usados pelo build)

- RB-011: build Windows do EmulationStation no fork `retroboat-emulationstation`, com o mesmo
  pacote do RetroBat oficial, publicado no release `continuous-master`. Falta cadastrar as
  credenciais de API antes de trocar o ES no `build.ini`. RB-016: correção para compilar com
  o Visual Studio 2026.
