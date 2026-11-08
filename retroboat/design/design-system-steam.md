# Design system do Retroboat (base: Steam Big Picture)

Toda interface do Retroboat (tema, mudanças no ES, janelas do lançador) segue o design do app da
Steam para desktop, com o **modo Big Picture** como referência principal: é uma interface para TV e
controle, como o EmulationStation. Decisão de 25/10/2026.

## 1. Fontes

| Fonte | O que dá | Confiança |
|---|---|---|
| CSS do próprio cliente Steam (`steamui\css\sp.css`, cliente de 10/10/2026, versão 1791918634) | variáveis de cor, raio e fonte; gradientes e regras dos botões JOGAR e INSTALAR; foco | alta: é o que o cliente desenha |
| [webdesignhot: Steam design.md](https://www.webdesignhot.com/design.md/steam/) e [shadcn.io: Steam](https://www.shadcn.io/design/steam/raw) | tokens da loja (azul-marinho `#1b2838`, links `#66c0f4`, tipografia da loja) | média: reconstrução de terceiros, e é a **loja**, não o app |
| [designmd.co/d/steam](https://www.designmd.co/d/steam) | indicado pelo usuário | não abriu (HTTP 429); revisar depois |

Onde as fontes divergem, vale o CSS do cliente. Os valores de cor da loja (`#1b2838` etc.) **não**
são usados no Retroboat: o Big Picture usa a escala de cinzas neutra abaixo.

## 2. Princípios

1. **Interface de sala (10 pés).** Lida a 2–3 m de distância, com controle: textos e alvos maiores
   que no desktop, um foco sempre visível, nada que dependa de mouse.
2. **Cada coisa no seu lugar.**
   - **Cabeçalho:** só estado global (conexão com a nuvem, bateria, hora). Nada de um jogo específico.
   - **Seção do jogo:** tudo o que é do jogo selecionado: status (na nuvem, instalado), ação
     principal (JOGAR, BAIXAR), informações.
   - **Barra de ajuda:** botões do controle.
3. **Uma ação principal por jogo**, com cor que diz o que acontece: verde = jogar agora, azul =
   baixar/instalar, cinza = indisponível.
4. **Cor é rara.** A interface é cinza-escura; verde e azul aparecem só nas ações e nos estados.
   O azul `#1A9FFF` também marca o foco.
5. **Texto claro, mas não branco puro** no corpo (`#DCDEDF`); branco só em título e no que tem foco.
6. **Cantos quase retos** (2 px) em botões e cartões. Pílula só em avatar.
7. **Sem enfeite:** nada de sombra em cartão, brilho, vidro ou gradiente de fundo. Movimento curto
   (~200 ms, ease-out), sem quique.

## 3. Tokens

### 3.1 Cores neutras (fundo e superfícies)

Escala `--color-greyneutral-*` do cliente.

| Token | Hex | Uso |
|---|---|---|
| `neutral-1` | `#060A0E` | fundo mais profundo, atrás de mídia |
| `neutral-2` | `#0E141B` | fundo da tela |
| `neutral-3` | `#141A21` | faixas, barras |
| `neutral-4` | `#171D25` | painéis |
| `neutral-6` | `#23262E` | cartões, barra de ação do jogo |
| `neutral-7` | `#2D323D` | cartão com foco, botão secundário |
| `neutral-8` | `#3D4450` | bordas, divisores |
| `neutral-10` | `#67707B` | ícones apagados, texto desativado |
| `neutral-11` | `#8B929A` | texto secundário, legendas |
| `neutral-12` | `#B8BCBF` | texto de apoio |

### 3.2 Texto

| Token | Hex | Uso |
|---|---|---|
| `text-title` | `#F1F2F3` | títulos |
| `text-body` | `#DCDEDF` | corpo, rótulos de botão |
| `text-secondary` | `#8B929A` | legendas em maiúsculas ("NA NUVEM", "INSTALADO") |
| `text-note` | `#636B74` | notas |
| `text-on-action` | `#FFFFFF` | rótulo do botão principal com foco |

### 3.3 Acento e estados

| Token | Hex | Uso |
|---|---|---|
| `accent` / `focus` | `#1A9FFF` | foco (contorno de 2 px ou sombra interna), progresso, ícone "baixando" |
| `focus-light` | `#47BFFF` | borda de botão com foco |
| `ready` | `#59BF40` | estado "instalado / pronto" |
| `danger` | `#E5484D` | erro, corrompido |
| `warning` | `#FFC82C` | aviso |
| `disabled-bg` | `#2E3136` | fundo de botão desativado (texto a 50%) |

### 3.4 Gradientes das ações (do cliente)

| Ação | CSS do cliente | Faixa visível no botão* |
|---|---|---|
| **JOGAR** | `linear-gradient(to right, #70d61d 0%, #01a75b 60%)` | `#50C82F` → `#18B14E` (com foco: `#70D61D` → `#39BF3C`) |
| **INSTALAR / BAIXAR** | `linear-gradient(to right, #47bfff 0%, #1a44c2 60%)` | `#3A9BED` → `#235DCE` (com foco: `#47BFFF` → `#3082E0`) |
| **INDISPONÍVEL** | fundo `#2E3136`, texto branco a 50% | — |

\* O cliente desenha o gradiente com `background-size: 330%` e `background-position: 25%`, então o
botão mostra só uma faixa dele. Fora do navegador (tema do ES), use a faixa visível como
gradiente horizontal de duas cores.

### 3.5 Tipografia

- **Família:** Motiva Sans (fonte da Steam). Ela é proprietária e não pode ser distribuída com o
  Retroboat. Até o tema da etapa 2, o tema usa a fonte do Carbon (Cabin), que tem proporções
  parecidas; a troca por uma fonte livre equivalente fica para a etapa 2.
- **Pesos:** 300, 400, 500, 700.
- **Escala do Big Picture**, em px numa tela de 1080 linhas, e o valor de `fontSize` no tema do ES
  (`px / 1080`):

| Papel | px @1080p | ES `fontSize` | Peso | Caixa |
|---|---|---|---|---|
| título do jogo | 40 | 0.037 | 700 | normal |
| título de seção | 26 | 0.024 | 500 | normal |
| rótulo do botão principal | 26 | 0.024 | 700 | MAIÚSCULAS |
| corpo | 22 | 0.020 | 400 | normal |
| legenda (rótulo de informação) | 18 | 0.017 | 500 | MAIÚSCULAS, espaçamento 0,5 px |
| nota | 16 | 0.015 | 400 | normal |

O cliente desktop usa 12–16 px; o Big Picture escala tudo para a distância da TV, por isso os
valores acima são maiores.

### 3.6 Forma, espaço e foco

- **Raio:** `sm` 2 px (botões, campos, cartões), `md` 4 px, `lg` 8 px (painéis grandes), `full` só
  em avatar. No ES: `roundCorners` com valor ≥ 1 é em pixels; abaixo de 1 é fração do maior lado.
  Use pixels (3 px numa tela de 1080, que é o 2 px do cliente na escala da TV).
- **Espaço:** base de 4 px; passos 4, 8, 12, 16, 24, 32, 48. Margem da tela no Big Picture: ~48 px
  (0.025 da largura em 16:9).
- **Foco:** contorno `#1A9FFF` de 2 px, ou sombra interna de 4–6 px no item de grade com foco.
- **Movimento:** 200 ms, ease-out.

## 4. Componentes

### 4.1 Botão de ação do jogo

| Estado do jogo | Ícone (FontAwesome) | Rótulo | Fundo |
|---|---|---|---|
| Instalado | ▶ `` | JOGAR | gradiente JOGAR |
| Na nuvem | ⬇ `` | BAIXAR | gradiente INSTALAR |
| Na nuvem, Drive fechado | ⬇ `` | BAIXAR | gradiente INSTALAR |
| Baixando ou na fila | ⬇ `` | BAIXANDO | `neutral-7`, rótulo `accent` |
| Corrompido na nuvem | ⚠ `` | INDISPONÍVEL | `disabled-bg`, texto a 50% |

"Drive fechado" não é defeito: BAIXAR continua valendo, porque o Retroboat oferece abrir o Drive ao
apertar. Por isso o botão fica ativo, a legenda usa `warning` ("NA NUVEM · DRIVE FECHADO") e o valor
diz o que vai acontecer ("Abre o Google Drive e baixa"); vermelho fica só para corrompido.
O selo da faixa (4.3) fica `neutral-10`.

BAIXANDO é cinza porque não é uma ação nova: o jogo já está baixando. Apertar de novo abre a tela de cancelar, com
"Continuar baixando" como opção inicial. O tema só mostra que está baixando; o andamento fica na região do cabeçalho (4.6).

Altura de 48 px @1080p (0.044), largura mínima de 200 px (0.105 da largura em 16:9), raio 3 px,
rótulo em maiúsculas, peso 700, `text-on-action`.

### 4.2 Barra de ação do jogo

Fica na seção do jogo, logo abaixo da mídia principal (como a barra de JOGAR do Big Picture abaixo
da arte do jogo). Fundo `neutral-6` a ~85%, raio 4 px.

```
[ ▶ JOGAR ]   INSTALADO                    (legenda, text-secondary)
              Pronto para jogar neste PC   (valor, text-body)

[ ⬇ BAIXAR ]  NA NUVEM
              Baixa em segundo plano

[ ⬇ BAIXANDO ] BAIXANDO                      (legenda, accent)
              O andamento aparece no topo da tela
```

### 4.3 Selo de status na faixa de informações

Nas visões em grade, que não têm seção de detalhes, o status vira um ícone na faixa de informações
do jogo (ao lado de nota, jogadores, manual): nuvem com seta `#1A9FFF` (na nuvem), check
`#59BF40` (instalado), alerta `#E5484D` (corrompido).

### 4.4 Ícone global de nuvem (cabeçalho)

Canto superior direito, junto da bateria e da hora, como os ícones de sistema do Big Picture.

| Estado | Ícone | Cor |
|---|---|---|
| Nuvem conectada | nuvem `` | `text-body` |
| Sem nuvem (Drive fechado) | nuvem com um X por cima | nuvem `neutral-11`, X `neutral-2` |

Só a cor não bastava: de longe, as duas nuvens pareciam iguais. O X é um glifo próprio (`&#xF00D;`),
centrado sobre a nuvem, na cor do fundo, como um recorte.

### 4.5 Legenda + valor

Padrão de informação do Big Picture: legenda curta em maiúsculas (`text-secondary`, 18 px, peso 500)
sobre o valor (`text-body`, 22 px). Usado na barra de ação e em informações do jogo.

### 4.6 Região de ícones do cabeçalho (download)

O canto do cabeçalho com os ícones globais é uma região que gira: as duas faces são lados de um cubo, no eixo
horizontal e dentro da própria região. A face de ícones é o cabeçalho de verdade do tema, então funciona em
qualquer tema. Um download é estado global (a pessoa pode estar em outro jogo), por isso fica aqui, e não na
seção do jogo; o status do jogo (BAIXANDO, em 4.1) continua na seção dele.

```
[ ★ 2026  ☁  15:24 ]  →  gira  →  [ ⬇ BAIXANDO  Nome do jogo                   45% ]
                                  [   ▓▓▓▓▓▓▓▓▓░░░░░░░░░░░                         ]
                                  [   1,9 GB de 4,3 GB · 12 MB/s · restam 3:10     ]
```

| Status | Legenda | Cor | Ícone | Linha de baixo |
|---|---|---|---|---|
| Lendo o jogo na nuvem | PREPARANDO | `accent` | seta | Lendo o jogo na nuvem |
| Baixando | BAIXANDO | `accent` | seta | quanto, velocidade e tempo restante |
| Sem o Google Drive | AGUARDANDO | `warning` | ! | Continua sozinho quando o Google Drive voltar |
| Terminou | PRONTO | `ready` | check | Pronto para jogar neste PC |
| Não deu certo | NÃO BAIXOU | `danger` | ! | Veja o motivo na notificação |

- **Tempos:** barra por 10 s, ícones por 6 s. PRONTO e NÃO BAIXOU ficam 6 s e não esperam a vez, nem com os ícones à mostra.
- **Movimento:** rotação de 450 ms, com aceleração e pouso, sem quique. É a exceção aos 200 ms em ease-out: são duas faces
  passando pela região e, com ease-out, quase todo o giro acaba no primeiro décimo de segundo e não aparece.
- **Forma:** fundo `neutral-4`, borda de 1 px `neutral-8`, raio 4 px, barra de 6 px em trilho `neutral-7`. Tamanhos a
  1080 linhas: 76 px de altura, legenda 16 px, nome 20 px, porcentagem 19 px e detalhes 16 px. O texto é menor que a escala
  do Big Picture (3.5) porque a faixa é estreita e foi pedida discreta.
- **Quando aparece:** só com o ES em primeiro plano. Com jogo aberto, ela some; com o ES ocupado (scraper), espera, porque o
  aviso dele fica no mesmo canto. Nunca pega o foco nem o clique: controle e teclado continuam indo para o ES.
- **Posição:** tabela por tema em `HeaderRegion` (Core). No PlayStation-X ela começa logo depois do logo do tema, para não
  cortar o nome dele ao meio; em tema desconhecido, canto superior direito.

## 5. Aplicação nos temas

### Carbon do Retroboat (RB-014)

| Elemento | Onde | Arquivo |
|---|---|---|
| Barra de ação (botão + status) | visão detalhada, abaixo da mídia | `retroboat/status.xml` do fork do Carbon |
| Selo de status | faixa de informações das visões em grade e carrossel | idem |
| Ícone global de nuvem | cabeçalho, canto superior direito | idem |

### PlayStation-X do Retroboat (RB-017)

| Elemento | Onde | Arquivo |
|---|---|---|
| Botão + legenda e valor | ps4Style (visão padrão) e single: no lugar do "Iniciar" do jogo em foco | `retroboat/status.xml` do clone do PlayStation-X |
| Barra de ação | detailed, abaixo da mídia | idem |
| Selo de status | fim da faixa de ícones (`contenedor-iconos`) de todas as visões em grade | idem |
| Ícone global de nuvem | cabeçalho, entre "★ 2026" e o relógio (na linha do "v.43" em 4:3, 5:4 e 3:2) | idem |

No PlayStation-X a fonte é a SST, do próprio tema, e o alerta de corrompido usa o glifo `F06A`: o
tema troca o `F071` por uma imagem própria, que ignora a cor.

O status do jogo vem do tamanho do arquivo (`filesize({game:path}) == 0` é um marcador da
nuvem), e não da descrição: o scraper do ES troca a descrição e apagava a linha de status.
"Corrompido" ainda vem da descrição, porque só o lançador sabe.

**Regras do ES aprendidas na implementação:**

- `<visible>` só é avaliado como expressão quando tem uma variável `{tipo:propriedade}`. Sem ela, o
  ES lê o texto como true/false. Estado global (sem jogo) vai no atributo `if="..."` do elemento,
  avaliado ao carregar o tema.
- Dentro de `if`, `${themePath}` é variável, não texto: o caminho se monta com concatenação,
  `exists(${themePath} + "/...")`.
- Variáveis do tema são coladas como texto. Toda variável com expressão vai entre parênteses, senão
  `!${variavel}` muda de sentido pela precedência dos operadores.
- O estado da nuvem chega ao tema por um arquivo: o lançador cria
  `<RetroBat>\retroboat\estado\nuvem-conectada` quando acha o Drive e apaga quando não acha.
- `exists()` guarda o resultado em cache até o ES recarregar ou voltar de um jogo, e `if=` só é avaliado ao
  carregar o tema. Para o tema mudar com o ES aberto, o lançador chama `GET /reloadgames`.
- Para só o status de um jogo mudar, sem recarregar nem mexer na posição do usuário: `POST /addgames/<sistema>` com o
  nó da pasta raiz (`<gameList><folder><path>.</path></folder></gameList>`) limpa o cache e redesenha a lista. O gamelist é
  regravado com o que o ES tem, sem perda. Um nó de jogo apagaria as datas do scraper e os campos que o ES não conhece.
- O tema não recebe dados ao vivo: uma expressão só é avaliada quando o jogo selecionado muda. Por isso a região do
  cabeçalho é desenhada pelo Retroboat, numa janela por cima do ES, e não pelo tema.
- Uma visão personalizada (`customView ... inherits="grid"`) copia os elementos da base quando é
  lida. Um `<view name="grid">` lido depois ainda chega a ela, porque o ES repassa a definição às
  visões que herdam; um `<customView name="a, b">` (com vírgula e sem `inherits`) altera só as
  visões listadas que já existem, sem repassar.
- Filhos de um `stackpanel` redefinido são acrescentados no fim, não mesclados: para mudar um filho
  conforme o estado, use `if="..."` no próprio filho.
- Um tema pode trocar glifos do FontAwesome por imagens (`resources/fontoverrides/<código>.png`);
  esses glifos perdem a cor do texto.

## 6. Pendências

- Fonte livre no lugar da Motiva Sans (etapa 2).
- Nome esmaecido para jogos da nuvem na lista e "BAIXAR" na barra de ajuda: dependem do ES (RB-010).
- Tema próprio estilo Big Picture (etapa 2). Até lá, os elementos do Retroboat seguem estes tokens
  dentro do Carbon e do PlayStation-X.
