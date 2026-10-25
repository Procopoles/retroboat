# Desenho: conteúdo dos jogos na nuvem e primeiros ajustes de UI estilo Steam

Pedido de 25/10/2026:

- a capa, as imagens e a descrição dos jogos da nuvem aparecem no Retroboat antes do download, como no Steam;
- os detalhes do jogo mostram o status com um ícone (baixar ou jogar);
- no modo lista, o nome dos jogos não baixados aparece um pouco mais escuro;
- tudo compatível com os temas principais, ou num tema Steam nosso, se for mais fácil.

Investigado no código do ES (`batocera-emulationstation@master`) e no tema Carbon do build.

## 1. Conteúdo (metadados e mídia) na nuvem — RB-015

**Onde fica.** `retroboat\midia\<sistema>\` no Drive:

- metadados por jogo (nome, descrição, data, gênero, nota...), com o nome do arquivo na nuvem como chave;
- `imagens\`, `videos\` e `manuais\`.

**Concorrência.** Cada PC grava o próprio arquivo de metadados, e a leitura junta todos (seção 2 do
cloud-first). A mídia não muda depois de baixada, então basta enviar o que falta.

**Como chega em cada PC.**

- Ao abrir, o lançador baixa os metadados e as imagens de todos os jogos da biblioteca, que são
  pequenos. Os vídeos são opcionais, porque são grandes.
- Tudo vai para `.emulationstation\media\<sistema>\`, fora da pasta de jogos. Isso fecha o item 1c da
  biblioteca isolada.
- O gamelist local passa a apontar para essas mídias com `~/.emulationstation/media/...`, que é
  relativo à pasta do ES e mantém o RetroBat portátil.

**Como o conteúdo é produzido.**

1. **Scraper do próprio ES, nos marcadores.** Para um arquivo vazio, o ES manda ao ScreenScraper só o
   nome do arquivo (`romnom`) e `romtaille=0`, sem hash (`ScreenScraper.cpp`, linhas 255–302). Com nomes
   no padrão Redump/No-Intro, como "Clock Tower 3 (Europe) (En,Fr,De,Es,It).iso", a busca por nome
   costuma achar o jogo. **A validar** com um scrape real.
2. Depois do scrape, o ES grava a mídia dentro da pasta de jogos (`Scraper::getSaveAsPath`). Na
   abertura seguinte, o lançador move essa mídia para `.emulationstation\media`, reescreve os caminhos e
   envia tudo para a nuvem. Os outros PCs recebem sem precisar fazer scrape.
3. Futuro: um scraper próprio que usa o CRC do disco lido do ZIP no Drive, o que identifica o jogo sem
   baixá-lo. Precisa de credencial de desenvolvedor do ScreenScraper.

## 2. Interface: o que o tema já consegue e o que precisa do ES

**O tema já consegue, sem mudar o ES** (`THEMES_BINDINGS.md`): elementos de tema aceitam expressões
avaliadas para o jogo selecionado.

- `filesize({game:path}) == 0` identifica um jogo não baixado (o marcador está vazio).
- `startswith({game:desc}, "Corrompido na nuvem")` identifica um jogo corrompido.
- `<visible>`, `<opacity>`, `<saturation>`, `<text>` e `<path>` aceitam essas expressões. Dá para
  mostrar "NA NUVEM · BAIXAR" ou "INSTALADO · JOGAR", trocar o ícone e deixar a capa
  dessaturada nos detalhes, como o Steam faz.

**O tema não consegue:**

| Desejo | Por que não dá só com tema | Onde muda no ES |
|---|---|---|
| Nome esmaecido na lista para jogos não baixados | A lista só tem duas cores por linha: `primaryColor` para jogos e `secondaryColor` para pastas (`BasicGameListView.cpp:76`). As expressões valem só para o jogo selecionado, não para cada linha | Terceira cor na lista. Sem definição no tema, usa a cor primária com menos opacidade, então funciona em **qualquer** tema |
| Ícone de nuvem ao lado do nome | Os ícones da lista (favorito, manual, save, troféus) são desenhados pelo ES em `GameNameFormatter.cpp`, com glifos da fonte FontAwesome que o ES já embute | Mais um ícone, com o mesmo mecanismo |
| "BAIXAR" no lugar de "JOGAR" na barra de ajuda | A barra de ajuda é do ES, não do tema | Texto da ajuda conforme o estado do jogo |
| Status sem truque de tamanho de arquivo | `filesize == 0` funciona, mas é uma convenção nossa | Nova variável `{game:cloud}` / `{game:installed}` para os temas |

## 3. Compatibilidade com os temas

| Abordagem | Funciona em | Custo | Risco de merge |
|---|---|---|---|
| **ES nativo**: lista esmaecida, ícone de nuvem, "BAIXAR" na ajuda, variável `{game:cloud}` | todos os temas, sem mudar nenhum | fork do ES + build Windows (RB-011); mudanças pequenas em 3 a 4 arquivos C++ | C1/C2 em arquivos de calor baixo ou médio (`GameNameFormatter.cpp`, `BasicGameListView.cpp`, `TextListComponent.h`) |
| **Elementos no tema**: status e ícone nos detalhes | só no tema que recebe os elementos, porque cada tema tem um layout de detalhes | XML por tema e por visão | depende do tema |
| **Tema Retroboat**, derivado do Carbon | no nosso tema | fork do tema Carbon | nenhum para o RetroBat; vira a base do tema Steam da etapa 2 |

**Por que não dá para "estender" o Carbon sem editá-lo:** o ES não tem um mecanismo para outro
arquivo acrescentar elementos a um tema pronto. Um tema nosso que incluísse o Carbon de outra pasta
quebraria as imagens, porque o Carbon usa `${themePath}` 95 vezes, e isso aponta para a pasta do tema
que está ativo.

## 4. Decisão recomendada

1. **Agora, sem mudar o upstream (C0):** conteúdo na nuvem (seção 1) e mídia fora da pasta de jogos.
   Não depende de nada além do que já temos.
2. **Fork do ES com build Windows no GitHub Actions (RB-011)**, depois as mudanças nativas da seção 2
   (RB-010): lista esmaecida, ícone de nuvem, "BAIXAR" na ajuda e `{game:cloud}`. Isso cumpre
   "compatível com os temas principais" no que é comum a todos: a lista e a ajuda.
3. **Tema Retroboat (RB-014), um fork do Carbon:** nos detalhes, status com ícone Baixar/Jogar e
   capa dessaturada para jogos da nuvem, usando as expressões que já funcionam hoje. Ele vira a base
   do tema Steam da etapa 2. Os outros temas recebem as melhorias nativas da lista; o status nos
   detalhes fica no nosso tema, e as expressões ficam documentadas para quem quiser adotar.

**O que depende de você:**

- **Autorizar a criação de dois forks no GitHub** da conta Procopoles: `batocera-emulationstation`
  (base do ES) e `es-theme-carbon` (base do tema). Autorizado em 25/10/2026.
- **Concluir o login no GitHub CLI**, para eu criar os forks, o workflow de build e os PRs. Os forks
  também podem ser criados pelo site.

## 5. Estado em 25/10/2026

**Conteúdo na nuvem (RB-015): pronto.** Mídia fora da pasta de jogos, metadados e mídia
compartilhados entre PCs pelo Drive, vídeos e manuais opcionais. Validado com o ES real.

**Fork do ES (RB-011): pronto localmente, em `..\retroboat-emulationstation`.**

- Nome do fork: `retroboat-emulationstation`, para deixar claro de qual peça se trata. O
  `upstream` é o Batocera.
- Workflow `.github/workflows/retroboat-windows.yml` e script `retroboat/build-windows.ps1`:
  compilam e publicam `EmulationStation-Win64.zip` no release `continuous-master`, no formato que
  o RetroBuild baixa.
- Compilado aqui: o pacote tem os mesmos arquivos do oficial e o executável abre o RetroBat.
- O Visual Studio 2026 precisou de uma linha a mais no ES (RB-016, enviável ao Batocera).
- **Achado: o ES oficial tem credenciais de API compiladas.** Sem as nossas, o build não tem os
  scrapers ScreenScraper, TheGamesDB e HfsDB, nem o menu do RetroAchievements. Só IGDB e ArcadeDB
  continuam. Por isso o `build.ini` continua com o ES oficial até termos as credenciais. A mais
  importante é a do ScreenScraper, que é pedida no fórum deles e é gratuita.

**Fork do Carbon (RB-014): primeira versão, em `..\es-theme-carbon`.**

- O status fica no cabeçalho, à direita: "☁ Na nuvem" + botão azul "BAIXAR", "✓ Instalado" + botão
  verde "JOGAR", ou "Corrompido na nuvem" em vermelho.
- O cabeçalho é o mesmo em todas as visões de jogos do Carbon, então um arquivo
  (`retroboat/status.xml`) cobre Icons, Tiles, Boxes, Content, detailed e gamecarousel. O
  `theme.xml` ganha uma linha.
- O status vem da primeira linha da descrição. Quando o RB-010 criar `{game:cloud}`, o tema troca
  para essa variável.
- Testado com o nosso ES nas visões Icons (a padrão do PS2) e detailed.

**Próximos passos:**

1. ~~Criar os dois forks no GitHub, enviar os commits locais e rodar o workflow do ES.~~ Feito em
   25/10/2026: [Procopoles/retroboat-emulationstation](https://github.com/Procopoles/retroboat-emulationstation)
   (o build passou e publicou o release `continuous-master`) e
   [Procopoles/es-theme-carbon](https://github.com/Procopoles/es-theme-carbon).
2. Conseguir as credenciais de API e cadastrar como *secrets* do fork do ES.
3. Trocar no `build.ini` `emulationstation_url` (fork do ES). O `theme_path` (fork do Carbon) já
   foi trocado em 25/10/2026.
4. RB-010 no fork do ES: lista esmaecida, ícone de nuvem, "BAIXAR" na ajuda e `{game:cloud}`.
