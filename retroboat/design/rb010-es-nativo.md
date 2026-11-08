# RB-010: nuvem nativa no EmulationStation

Decisão de 07/11/2026: as chaves de API deixam de impedir o uso do nosso ES (o scraper é a última fonte de informação
dos jogos). O RB-010 leva para dentro do ES o BAIXAR, a região do cabeçalho que gira e o status ao vivo do jogo. A
fila e o download continuam no serviço C# (`retroboat-launch --downloads`, RB-020): o ES só mostra e pede.

Fork: `..\retroboat-emulationstation` (Batocera ES, `-DRETROBAT=ON`). Mapa do código feito em 07/11/2026; as linhas
citadas são dessa data.

## 1. Regras

- Lógica nova só em arquivos nossos: `es-app/src/retroboat/`. Arquivos do upstream ganham ganchos de uma linha, com o
  comentário `// RETROBOAT RB-010`. Exceções pequenas e listadas em 6.
- Textos em português, como literais nos nossos arquivos (os `.po` mudam toda semana). Os nossos `.cpp` compilam com
  `/utf-8` (definido em `retroboat.cmake`, só para eles).
- O ES oficial continua funcionando com o Retroboat: tudo o que é nativo é detectado (seção 4), e o serviço C# mantém a
  janela por cima do ES (`HeaderBar`) quando o ES não é o nosso.
- Nada de rede, nada de credencial, nenhum push.

## 2. Estado que o ES conhece

Raiz do RetroBat = pasta-mãe de `Paths::getHomePath()` (o RetroBat abre o ES com `--home <RetroBat>\emulationstation`).

| Fonte | Quem grava | O ES usa para |
|---|---|---|
| `<raiz>\retroboat\biblioteca-nuvem.xml` | C# (sincronização, fim de download) | saber quais arquivos são jogos da nuvem e quais estão corrompidos |
| `<raiz>\retroboat\estado\downloads.xml` | C# (fila) | a fila na abertura do ES e depois de cada pedido |
| `POST /retroboat/download` | serviço C# | andamento ao vivo, avisos de fim, fila atual |

`biblioteca-nuvem.xml` (UTF-8 com BOM; gravação não atômica: se não abrir, manter a última leitura boa):

```xml
<cloudLibrary>
  <game system="ps2" cloud="Jogo.zip" local="Jogo.iso" size="..." modified="..." state="Cloud|Installed|Corrupt" problem="..."/>
</cloudLibrary>
```

`downloads.xml`: `<downloads><game system="ps2" cloud="Jogo.zip" local="Jogo.iso" title="..." added="..."/></downloads>`,
na ordem da fila (o primeiro é o que está baixando). Arquivo ausente = fila vazia.

**Chave de um jogo** = `minúsculas(<pasta do sistema>/<nome do arquivo>)`, onde a pasta do sistema é o nome da pasta-mãe do
arquivo do jogo (`system` no XML). Funciona sem saber a pasta de jogos. Em coleções, usar `getSourceFileData()`.

**Estado de um jogo** (`CloudState`):

| Estado | Regra |
|---|---|
| `None` | não está no índice, ou está como `Installed`, ou o arquivo tem mais de 0 bytes |
| `Corrupt` | `state="Corrupt"` e arquivo com 0 bytes |
| `Downloading` | na fila, em primeiro, e o serviço disse que é ele |
| `Queued` | na fila, mas não é o atual |
| `Cloud` | no índice, não instalado, arquivo com 0 bytes, fora da fila |

O tamanho do arquivo só é consultado para quem está no índice (é um `stat` sem cache). Guardar o resultado e refazer
quando o estado muda.

Recarregar o índice e a fila: na abertura, a cada 2 s se a data do arquivo mudou (no `tick` da interface, seção 5.4), e
logo depois de um aviso de fim ou de um pedido ao C#.

## 3. Protocolo

### 3.1 Serviço → ES: `POST /retroboat/download`

Só de `127.0.0.1` (o servidor pode estar aberto à rede com `PublicWebAccess`). Corpo JSON em UTF-8:

```json
{
  "phase": "preparing | downloading | waiting | finished | failed | cancelled | idle",
  "system": "ps2",
  "local": "Jogo.iso",
  "title": "Shadow of the Colossus",
  "done": 1234, "total": 4567,
  "speed": 1234.5,
  "remaining": 62,
  "message": "motivo, só em failed",
  "queue": [ { "system": "ps2", "local": "Jogo.iso" } ]
}
```

- `queue` é a fila inteira depois do evento, na ordem; o primeiro é o atual. `remaining` em segundos, `-1` se não há estimativa.
- O serviço manda: cada progresso (no máximo 4 por segundo), cada mudança de fase, um sinal a cada 2 s enquanto
  estiver aberto (com o último estado), `finished`, `failed` ou `cancelled` no fim de cada jogo, e `idle` com
  `queue: []` quando a fila esvazia e ele vai fechar.
- `finished` e `failed` viram avisos na região do cabeçalho (seção 5.3). `cancelled` não tem aviso.
- O handler roda numa thread do servidor: guarda o estado sob mutex, aumenta um contador de versão e chama
  `PowerSaver::pushRefreshEvent()`. **Nunca** `postToUiThread` por progresso: ele zera o tempo de inatividade, e a
  proteção de tela nunca entraria durante um download.

### 3.2 `GET /retroboat/download`

Responde `200` com o estado que o ES tem (o mesmo formato). O serviço C# usa isso para saber que o ES é o nosso: o ES
oficial responde 404.

### 3.3 ES → serviço: linha de comando

O ES roda `"<raiz>\retroboat\retroboat-launch.exe" --baixar "<caminho do jogo>"` ou `--cancelar "<caminho do jogo>"`.
Se não existir, tentar `<raiz>\retroboat-launch.exe`. O caminho é o `getPath()` do jogo (barras normais servem).

- `--baixar`: se o Google Drive estiver fechado, o C# mostra a pergunta dele (tela cheia); põe o jogo na fila e inicia o
  serviço. Código 0 = está na fila; 1 = não entrou (o usuário disse "Agora não", o jogo não é da nuvem, erro).
- `--cancelar`: tira o jogo da fila. 0 = tirou; 1 = não estava.
- Sem `estado\nuvem-conectada` (Drive fechado), `--baixar` pode abrir a pergunta. Nesse caso o ES espera o processo
  sem processar a entrada (modo bloqueante do `ProcessStartInfo`, com `window`, que descarta os eventos do SDL durante
  a espera: o A apertado na pergunta não pode chegar ao ES). Com o Drive conectado, o processo roda numa thread e o ES
  continua respondendo.
- O processo é criado pelo nosso código (`CreateProcessW`, sem expandir variáveis de ambiente, fechando os handles),
  com janela normal (`SW_SHOWNORMAL`: o `SW_HIDE` esconderia a pergunta do C#).
- Ao terminar, o ES relê `downloads.xml` (a verdade da fila) e atualiza a tela.

### 3.4 Sinal para o tema: `estado\es-nativo`

O nosso ES cria `<raiz>\retroboat\estado\es-nativo` ao abrir, antes de carregar o tema, e apaga ao sair. O
`Retroboat.exe` apaga o arquivo antes de abrir o RetroBat. O tema usa `if='exists(...)'` para mostrar o que só o nosso
ES sabe fazer (seção 7). É o mesmo padrão do `nuvem-conectada`.

## 4. ES oficial ou o nosso

- O serviço C# pergunta `GET /retroboat/download` ao começar cada sessão. `200`: modo nativo (manda o estado por POST,
  não abre a `HeaderBar`, não usa `/addgames`). Outro código: modo atual (RB-020).
- No modo nativo, o `retroboat-launch` que fica na frente do emulatorLauncher continua no `<command>`, mas não deveria
  ver um marcador: o ES intercepta antes. Se vir, faz o que faz hoje.

## 5. O que o ES mostra

### 5.1 BAIXAR (A num jogo da nuvem)

Ganchos (seção 6) em `ISimpleGameListView::launchSelectedGame` (antes do seletor de save states e do som de launch),
no começo de `ViewController::launch` (rede de segurança para opções do jogo, `/launch`, netplay) e no lançamento pela
proteção de tela (`SystemScreenSaver`, que chama `FileData::launchGame` direto). Todos chamam
`RetroboatCloud::onActivate(window, game)`, que devolve `true` quando cuidou do jogo:

| Estado | O que acontece |
|---|---|
| `Cloud` | pede `--baixar`; o jogo passa para `Queued` na hora (otimista) e volta ao que o `downloads.xml` disser quando o processo terminar |
| `Downloading` ou `Queued` | caixa de pergunta (5.5) |
| `Corrupt` | caixa com o motivo: "Este jogo está corrompido na nuvem: <problem>. Substitua o arquivo no Drive para poder jogar." |
| `None` | devolve `false`: o jogo abre como sempre |

Nada da abertura de jogo acontece num jogo da nuvem: sem animação, sem tela de "carregando", sem parar a música, sem
eventos de script, sem contar partida.

### 5.2 Mudança de estado na lista

Quando a fila muda (entrou, saiu, terminou, falhou) ou o índice muda: `Utils::FileSystem::FileSystemCache::reset()` (o
`exists()` do tema tem cache e o ES não passa mais pelo `launchGame`, que fazia isso) e
`ViewController::get()->onFileChanged(jogo, FILE_METADATA_CHANGED)` para cada jogo que mudou. Isso refaz a lista e
mantém o cursor; é o que o `/addgames` fazia no RB-020 e funcionou. Só nas transições, nunca por progresso.

### 5.3 Região do cabeçalho (o mesmo desenho da seção 4.6 do design system)

- O gancho em `ViewController::render`, logo depois da visão e antes da barra de ajuda e do fade
  (ViewController.cpp:1032-1034), chama `Retroboat::Cloud::onFrame(window, transform)`. Assim menus, caixas de mensagem,
  avisos e a proteção de tela ficam por cima. `onFrame` roda em toda visão (inclusive na de sistemas, onde não há
  `ISimpleGameListView`): traz o estado do HTTP para a thread da interface, recarrega os arquivos quando mudam, agenda as
  transições da 5.2 com `postToUiThread` (não se mexe em visões no meio do desenho) e chama `CloudHeaderRegion::render`.
- `CloudHeaderRegion` (arquivos `CloudHeaderRegion.h/.cpp`) não conhece a fila: recebe `setLive` e `addNotice` e desenha.
  A API está na seção 10.
- Retângulo por tema (mesmos números do `HeaderRegion.cs`), com o tema de `Settings::getString("ThemeSet")`:
  altura 0,07 da tela; direita em 0,985; largura base 0,30 (0,33 no PlayStation-X) × (16/9) / proporção da tela, no
  máximo 0,62; centro vertical 0,04 (PlayStation-X: 0,066; 0,036 se a proporção for menor que 1,55).
- Ciclo: barra 10 s, ícones 6 s, rotação 450 ms com `(1 - cos(t·π)) / 2`. Aviso (`finished`/`failed`) 6 s e sem esperar
  a vez. Depois de um `failed` voltar aos ícones, `window->displayNotificationMessage(título + ": " + motivo)`.
- A face de ícones é o próprio cabeçalho: na rotação, a cada quadro, copiar o retângulo do quadro atual para uma textura
  (`glCopyTexImage2D(GL_TEXTURE_2D, 0, GL_RGB, ...)`, GL 1.1, existe nos dois renderers do Windows), desenhar a barra no
  lugar, copiar de novo para uma segunda textura, e então desenhar o fundo `neutral-2` e as duas faces do cubo em
  faixas horizontais (uma tira de triângulos por face, uma faixa a cada 2 a 4 px, cor por vértice para escurecer até
  50% de lado). As texturas existem só durante a rotação: criar no começo e destruir no fim (o contexto pode ser refeito).
- Conversão do retângulo para pixels da janela por `Renderer::getViewport()` e `getWindowHeight()` (Y invertido).
  Não girar com a tela rotacionada (`getScreenRotate() != 0`): só troca de face.
- `PowerSaver::pause()` só durante a rotação, sempre com o `resume()` correspondente.
- Com o scraper, a instalação de conteúdo ou o hasher rodando (`ThreadedScraper::isRunning()`,
  `ContentInstaller::isRunning()`, `ThreadedHasher::isRunning()`), a região não abre: o aviso deles fica no mesmo canto.
  Um aviso de fim espera.
- Barra: fundo `neutral-4` (`#171D25`), borda 1 px `neutral-8`, raio 4 px; ícone 30 px; linha 1 com a legenda (16 px,
  na cor do estado), o nome (20 px, `#DCDEDF`) e a porcentagem à direita (19 px, `#F1F2F3`); linha 2 com a barra de 6 px
  em trilho `#2D323D`; linha 3 com os detalhes (16 px, `#8B929A`) e "mais N na fila" à direita. Tamanhos para 1080
  linhas, escalar pela altura. Fonte padrão do ES; ícones da FontAwesome que o ES já embute.
- Legendas: PREPARANDO, BAIXANDO (`#1A9FFF`), AGUARDANDO (`#FFC82C`), PRONTO (`#59BF40`), NÃO BAIXOU (`#E5484D`).
  Detalhes: "Lendo o jogo na nuvem"; "1,9 GB de 4,3 GB · 12 MB/s · restam 3:10"; "Continua sozinho quando o Google Drive
  voltar"; "Pronto para jogar neste PC"; "Veja o motivo na notificação". Números no formato brasileiro (vírgula decimal).

### 5.4 Status ao vivo do jogo selecionado

- Novas propriedades de `{game:...}` (gancho em `FileData::getProperty`):

| Propriedade | Tipo | Valor |
|---|---|---|
| `cloudState` | texto | `""`, `cloud`, `queued`, `downloading`, `corrupt` |
| `inCloud` | bool | o jogo não está neste PC (`Cloud`, `Queued`, `Downloading`, `Corrupt`) |
| `downloading` | bool | `Queued` ou `Downloading` |
| `downloadPercent` | int | 0 a 100 |
| `downloadProgress` | texto | "45% · restam 3:10", "Preparando", "Na fila", "Aguardando o Google Drive"; fora disso, um espaço (texto vazio vira "Unknown" no ES) |

- `tick` (gancho em `ISimpleGameListView::update`, que roda a cada quadro só na visão do topo): se a versão do estado
  mudou e o jogo do cursor é o que está baixando, reavaliar só os extras cujas expressões citam essas propriedades
  (`BindingManager::updateBindings(comp, cursor, false)`), sem `updateInfoPanel` (que reinicia storyboards e dispara
  o evento `game-selected`). Também aplica as transições da 5.2 e recarrega índice e fila quando os arquivos mudam.

### 5.5 Apertar de novo num jogo da fila

`GuiMsgBox` com o texto "<título>\n\n<andamento>\n\nCancelar o download?" e os botões `_("NO")` (primeiro, com o foco, e
o que o B aciona) e `_("YES")`. NÃO mantém; SIM roda `--cancelar`.

### 5.6 Lista, ícone e barra de ajuda

- Listas de texto: terceira cor para jogos da nuvem, a cor primária com 45% da opacidade (também a cor de seleção e a
  dos parênteses). `TextListComponent` passa de 2 para 3 cores.
- Ícone antes do nome (`GameNameFormatter`), da FontAwesome embutida: nuvem U+F0C2 para `Cloud`, seta U+F019 para
  `Queued`/`Downloading`, alerta U+F071 para `Corrupt`.
- Grade (padrão do PlayStation-X): bloco com opacidade `0xA0` quando `inCloud` (`ImageGridComponent::loadTile`, pela
  propriedade, sem depender do nosso código).
- Barra de ajuda: o A vira "BAIXAR" (`Cloud`), "BAIXANDO" (`Queued`/`Downloading`) ou "INDISPONÍVEL" (`Corrupt`).
  Atualiza quando o estado do jogo do cursor muda.

## 6. Ganchos no upstream

| Arquivo | Gancho |
|---|---|
| `es-app/CMakeLists.txt` | `include(${CMAKE_CURRENT_SOURCE_DIR}/src/retroboat/retroboat.cmake OPTIONAL)` antes do `add_executable` |
| `es-app/src/views/gamelist/ISimpleGameListView.cpp` | `onActivate` em `launchSelectedGame`; `tick` em `update`; ajuda em `getHelpPrompts` |
| `es-app/src/views/ViewController.cpp` | `onActivate` no começo de `launch`; `CloudHeaderRegion::render` depois da visão |
| `es-app/src/SystemScreenSaver.cpp` | `onActivate` antes de `launchGame` |
| `es-app/src/FileData.cpp` | `getProperty` pergunta ao nosso código os nomes que não conhece |
| `es-app/src/views/gamelist/BasicGameListView.cpp` | cor da linha pelo estado |
| `es-app/src/views/gamelist/GameNameFormatter.cpp` | ícone antes do nome |
| `es-app/src/services/HttpServerThread.cpp` | registro das nossas rotas antes da primeira rota |
| `es-core/src/components/TextListComponent.h` | terceira cor (cerca de 5 linhas) |
| `es-core/src/components/ImageGridComponent.h` | opacidade do bloco por `inCloud` (1 linha) |
| início do ES (o primeiro ponto antes de carregar o tema) | inicializar o nosso estado e criar `estado\es-nativo` |

## 7. Tema

- `rbDownloading` continua por `exists(.../estado/baixando/<arquivo>)`: vale nos dois ES (o C# grava esses arquivos).
- Só com o nosso ES (`if='exists(... + "/../../../../retroboat/estado/es-nativo")'`), o valor do estado BAIXANDO passa a
  ser `{game:downloadProgress}` no lugar de "O andamento aparece no topo da tela". Essa definição vem depois da do
  "Drive fechado", para valer sobre ela.

## 8. Lado C#

- `retroboat-launch --baixar <rom>` e `--cancelar <rom>` (3.3). A pergunta do Drive é a mesma do BAIXAR de hoje.
- `DownloadHost`: modo nativo (seção 4) com um repórter que manda o POST (até 4/s, sinal a cada 2 s, `idle` no fim);
  sem `HeaderBar` e sem `/addgames` nesse modo.
- `EsApi`: `SupportsNativeDownloads()` e `PostDownloadStatus(...)`. JSON montado no Core e testado.
- `Retroboat.exe` apaga `estado\es-nativo` antes de abrir o RetroBat.

## 9. Riscos aceitos

- Dois ES na mesma máquina disputam a porta 1234.
- O ES compilado por nós usa o MSVC 14.50; o oficial, o 14.29. Um PC com o Visual C++ Redistributable antigo pode falhar.
- Sem as chaves: sem ScreenScraper, TheGamesDB e HfsDB, e sem o menu do RetroAchievements, até as chaves existirem.
- O `es-update` do RetroBat pode trocar o executável pelo oficial.

## 10. API entre o núcleo e a região

`es-app/src/retroboat/CloudHeaderRegion.h` (exatamente esta interface; só a thread da interface chama):

```cpp
#pragma once
#include <string>
#include "math/Transform4x4f.h"

class Window;

namespace Retroboat
{
	enum class DownloadPhase { None, Preparing, Downloading, Waiting, Finished, Failed };

	struct DownloadView
	{
		DownloadPhase phase = DownloadPhase::None;
		std::string title;
		long long done = 0;
		long long total = 0;
		double speed = 0;     // bytes/s
		int remaining = -1;   // segundos; -1 = sem estimativa
		int waiting = 0;      // jogos na fila depois deste
		std::string message;  // motivo, em Failed
	};

	class CloudHeaderRegion
	{
	public:
		// O download em andamento. phase None = nada baixando.
		static void setLive(const DownloadView& view);

		// Um jogo terminou (Finished) ou falhou (Failed): mostrado por 6 s, sem esperar a vez.
		static void addNotice(const DownloadView& notice);

		// Chamado a cada quadro pelo núcleo, depois da visão e antes dos menus.
		static void render(Window* window, const Transform4x4f& parentTrans);

		// Nada na tela e nada para mostrar.
		static bool isIdle();
	};
}
```

Arquivos do núcleo: `es-app/src/retroboat/RetroboatCloud.h/.cpp` (estado, índice, fila, processo, propriedades,
`onActivate`, `onFrame`, `tick`, ajuda, cor, ícone) e `RetroboatHttp.h/.cpp` (rotas). `retroboat.cmake` lista todos.

## 11. Como ficou (08/11/2026)

O código seguiu este desenho, com estas diferenças:

- **Sem `queue` no JSON e sem `cancelled`.** A fila é sempre o `downloads.xml`: o ES relê o arquivo a cada 2 s, logo depois de
  `finished`, `failed` ou `idle` e quando um `--baixar` ou `--cancelar` termina. O serviço manda `idle` ao cancelar.
- **`Downloading` é o primeiro da fila,** sem esperar o serviço confirmar. Sem andamento, o valor do jogo diz "Preparando".
- **Um segundo A enquanto o `--baixar` roda não faz nada,** em vez de oferecer cancelar um jogo que ainda não entrou na fila.
- **Início no `ViewController::init`,** não no `main.cpp` (arquivo quente): ele roda antes de carregar sistemas e temas. O
  `es-nativo` é apagado por um `atexit`.
- **Rotas em `RetroboatHttp.h`, só cabeçalho,** incluído pelo `HttpServerThread.cpp`: o `httplib.h` do ES tem uma função fora de
  linha (`base64_encode`) e não pode entrar em um segundo `.cpp`.
- **Processo próprio nos dois modos** (`CreateProcessW` numa thread, ou esperando e descartando os eventos do SDL quando o Drive
  está fechado), em vez do `ProcessStartInfo` do ES, que chama o SDL fora da thread principal e expande variáveis de ambiente.
- **A região termina antes do relógio e da bateria do ES** (0,94 no PlayStation-X, onde o relógio fica em 0,91; 0,95 nos
  demais, onde fica a bateria). Os dois são do ES, desenhados depois das visões, e ficariam por cima da região. Na janela do
  RB-020, a foto do cabeçalho os incluía.
- **A região segura as fontes** (`shared_ptr<Font>` em cada texto montado): sem isso o ES descarta a fonte e a textura dos
  glifos, e o texto vira quadrados pretos.
