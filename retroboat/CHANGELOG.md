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
  - Informações automáticas, como na Steam: todo jogo da biblioteca, baixado ou não, ganha nome sem
    as etiquetas do arquivo, descrição (em português quando há página na Wikipédia), gênero, data,
    desenvolvedora, editora e jogadores, sem conta (base do libretro, Wikidata e Wikipédia). A linha
    genérica "Na nuvem · ..." saiu da descrição; o status fica no botão do tema. O scraper do ES passa a ser a última fonte
    (ele errou, por exemplo, o gênero do Shadow of the Colossus).
- RB-005 (contas): os logins do ES (ScreenScraper, IGDB, RetroAchievements) vão para o Drive
  (`retroboat\contas`). Um PC sem login recebe o da nuvem ao abrir; uma troca de login chega aos
  outros PCs.
- RB-005 (configurações): as opções do ES (globais, por sistema e por jogo), os controles e as
  configurações do PCSX2 (o `PCSX2.ini`, os ajustes por jogo e os perfis de controle) vão para o
  Drive (`retroboat\configuracoes`). Um PC novo recebe tudo; uma mudança num PC chega aos outros, e
  o que depende da máquina (tela, áudio, pastas, janelas, senhas) fica em cada PC.
- RB-009 (saves de todos os sistemas): a pasta `saves` de cada sistema vai para o Drive pela mesma
  base dos memory cards do PS2: saves do jogo, memory cards, VMUs, NVRAM e save states de qualquer
  emulador (PS1, GameCube, Dreamcast e os outros), com trava, conflito e histórico. O que veio com o
  RetroBat não sobe; ficam as 10 versões mais novas de cada arquivo. PS3, Switch, Wii U, Xbox, Vita,
  PS4 e 3DS ainda não entram.
- RB-005 (configurações de todos os emuladores): os outros emuladores entram por uma regra genérica,
  sem lista: RetroArch (PS1 e Dreamcast), Dolphin (GameCube), DuckStation, Flycast e qualquer outro
  que o RetroBat baixar. Vão as opções, os ajustes por jogo e os remaps; ficam no PC caminhos,
  monitor, janelas, dispositivos, drivers e logins.
- RB-014: o tema padrão passa a ser o nosso fork do Carbon (`build.ini: theme_path`), no design da
  Steam (Big Picture): botão JOGAR ou BAIXAR com o status na seção do jogo, ícone de status nas
  visões em grade e ícone global de nuvem no cabeçalho.
  - Com o Drive fechado, um jogo da nuvem mostra "NA NUVEM · DRIVE FECHADO" e "Abre o Google Drive e
    baixa ao jogar" (RB-019). Não é tratado como corrompido.
- RB-017: o tema PlayStation-X ganha o mesmo status: o "Iniciar" do jogo em foco vira JOGAR, BAIXAR
  ou INDISPONÍVEL (visões ps4Style e single), barra de ação na visão detalhada, ícone de status na
  faixa de ícones de todas as visões em grade e ícone global de nuvem no cabeçalho.
  - O `Retroboat.exe` põe o status de volta no Carbon e no PlayStation-X a cada abertura, e também
    quando o tema é escolhido no menu do ES. Excluir, reinstalar ou atualizar o tema pelo ES não
    perde mais os ajustes. Comandos `retroboat-cli themes status | apply`.
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
  - Um cartão novo que o PCSX2 criou em branco não é mais chamado de danificado: fica fora da nuvem até
    o primeiro save, e num PC novo recebe o cartão da nuvem.
- Design system do Retroboat (`design/design-system-steam.md`), com base no Steam Big Picture e
  nos tokens do próprio cliente Steam.

- RB-018: o aviso falso de falta de BIOS ao abrir um jogo some: o lançador desliga a checagem do ES uma
  vez por PC (ela só aceita uma BIOS de PS2 específica). A BIOS segue conferida pelo conteúdo na
  notificação do início; quem quiser o aviso de volta religa no menu do ES.

- RB-019: o Retroboat cuida do Google Drive sozinho.
  - Com o Drive fechado, ele pergunta se abre: ao iniciar, ao apertar BAIXAR e ao abrir um jogo com memory
    card. "Sempre abrir, sem perguntar" não pergunta mais; "Agora não" não insiste no memory card até o
    próximo início. Se o Drive abrir por outro caminho, a pergunta some.
  - O ícone de nuvem desligada ganha um X, nos dois temas.
  - Com o ES aberto, a nuvem acompanha o Drive: quando ele conecta, sincroniza e o ES recarrega com os jogos
    novos; quando desconecta, o ES recarrega com o X e avisa.
  - BAIXAR continua ativo com o Drive fechado, e jogos da nuvem de sistemas que não são PS2 passam a chegar
    à pergunta, em vez de abrir o emulador com um arquivo vazio.

- RB-020: downloads em segundo plano, como na Steam.
  - BAIXAR não prende mais a tela: o jogo entra numa fila e o ES volta em instantes. Dá para navegar, abrir outros jogos
    e pôr mais jogos na fila. A fila continua na próxima abertura se o ES fechar.
  - O download aparece no topo: a região dos ícones do cabeçalho gira, mostra status, nome, barra e tempo restante e volta
    aos ícones, em qualquer tema. Não tira o foco e some com um jogo aberto.
  - O jogo mostra BAIXANDO nos dois temas e vira JOGAR ao vivo quando termina, sem recarregar o ES.
  - Sem o Google Drive, o download espera e continua sozinho quando ele voltar, inclusive se cair no meio.
  - Apertar de novo em um jogo da fila oferece cancelar. A tela cheia de download saiu.

- RB-004: a pasta de jogos do Drive (`retroboat\jogos`) ganha as mesmas pastas de console do `roms` do RetroBat
  (`psx`, `n64`, `gc`...), uma por sistema, a cada início. Para pôr um jogo na nuvem basta soltá-lo na pasta do
  console, sem criar pasta. Sistema novo do RetroBat ganha a pasta sozinho; nenhuma é apagada, nenhum arquivo é
  tocado. Só cria dentro de um `retroboat\jogos` que já existe, para nunca fazer uma segunda pasta vazia no Drive.
- RB-003: as pastas de console ficam só no Drive. A pasta de jogos do PC deixa de ganhar uma pasta por sistema (eram
  mais de 200 vazias): a pasta de um console surge com o primeiro jogo dele (download ou marcador de um jogo da nuvem).
  Nenhuma pasta local é apagada.
- RB-013 (todos os consoles): a BIOS na nuvem deixa de ser só do PS2. Uma BIOS de qualquer console posta num PC sobe
  para o Drive e chega aos outros; uma solta no Drive vai para a pasta que o emulador usa (ex.: `dc_boot.bin` em
  `bios\dc`). A lista vem do próprio RetroBat (uns 100 sistemas), mais os nomes de BIOS de PS1 que faltam nela. Os
  arquivos livres que já vêm com o RetroBat não sobem. A conferência do conteúdo continua só no PS2.
- RB-004: "Atualizar listas de jogos" no menu do ES faz a mesma sincronização da abertura antes de reler as listas:
  um jogo posto no Drive com o ES aberto aparece sem reabrir o Retroboat, com capa e informações, e um jogo tirado do
  Drive sai. Funciona também com o ES oficial; no nosso, a tela mostra "Sincronizando com a nuvem..." enquanto isso.
- RB-022: o PS1 abre no SwanStation por padrão, no lugar do Beetle PSX HW do RetroBat. Ele aceita qualquer BIOS original
  de PS1 (a SCPH1001 inclusive), e o Beetle só aceita a scph5501.bin. Como os outros emuladores, o RetroBat baixa o
  SwanStation no primeiro jogo, e o menu do ES ainda escolhe outro por sistema ou por jogo.

- RB-021 (passo 1): a biblioteca da nuvem ganha identidade (`retroboat\biblioteca.xml`, criado uma vez e nunca
  reescrito) e cada PC guarda o id em `[Cloud] Library`. O Retroboat só usa a pasta com esse id, em qualquer letra e
  em qualquer conta do Google Drive. Se ela não aparece, fica sem nuvem, em vez de usar a pasta `retroboat` de outra
  conta. Com a pasta em uma conta só, o PC adota a biblioteca sozinho, sem perguntar. Com a pasta em duas contas ou em
  nenhuma, nada é escrito e a escolha fica para o setup (passo 2).
- RB-021 (passos 2 a 4): setup em tela cheia, com controle, no lugar da janela da pasta de jogos: biblioteca da nuvem
  (usar, criar com confirmação depois de uma segunda busca, procurar de novo ou seguir sem nuvem), pasta de jogos deste
  PC (atual, padrão do RetroBat ou os discos com mais espaço) e as opções principais. No nosso ES, o menu NUVEM mostra o
  estado do Drive e da biblioteca e muda vídeos, manuais, capas automáticas e "abrir o Drive sozinho" na hora;
  "Trocar biblioteca ou pasta de jogos" fecha o ES e o Retroboat abre o setup. Novo `retroboat-cli settings`.

### Nos forks (ainda não usados pelo build)

- RB-011: build Windows do EmulationStation no fork `retroboat-emulationstation`, com o mesmo
  pacote do RetroBat oficial, publicado no release `continuous-master`. Falta cadastrar as
  credenciais de API antes de trocar o ES no `build.ini`. RB-016: correção para compilar com
  o Visual Studio 2026.
- RB-004 (fase 2): o menu do jogo no nosso ES desinstala, verifica e repara, como na Steam. DESINSTALAR mostra o espaço
  ocupado, confere antes que a nuvem tem uma cópia boa e devolve o jogo a BAIXAR, com a mídia e os saves. VERIFICAR INTEGRIDADE
  compara a cópia com a nuvem sem baixar o jogo (CRC-32 nos discos de ZIP, blocos nos outros) e oferece REPARAR quando acha dano.
  Um jogo que não está no PC tem BAIXAR ou CANCELAR DOWNLOAD no menu.
- RB-010: a nuvem dentro do nosso ES.
  - BAIXAR não passa mais pela abertura de jogo: sem animação, sem reiniciar a música. Apertar de novo num jogo da fila
    pergunta se cancela; num jogo corrompido, explica o motivo.
  - A região do cabeçalho que gira passa a ser desenhada pelo próprio ES, abaixo dos menus, e o relógio e a bateria do ES
    continuam à vista.
  - O status do jogo mostra o andamento ao vivo ("45% · restam 3:10"), a barra de ajuda diz BAIXAR, BAIXANDO ou
    INDISPONÍVEL, e os jogos que não estão no PC aparecem esmaecidos, com ícone de nuvem.
  - Com o ES oficial, o Retroboat continua como no RB-020, sozinho.
