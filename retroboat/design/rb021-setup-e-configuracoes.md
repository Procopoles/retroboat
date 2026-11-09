# RB-021: setup inicial e configurações da nuvem

Desenho de 08/11/2026. Pedido do usuário: uma tela de setup quando falta a configuração da nuvem e uma tela de
configurações do Drive num menu, só com o principal. As quatro recomendações da seção 12 foram aceitas em 09/11/2026, e
os quatro passos da seção 11 foram feitos no mesmo dia; o que ficou diferente do desenho está na seção 13.

## 1. Problema

- **Ninguém cria a biblioteca.** Desde `7524b5d` o Retroboat não cria mais a pasta `retroboat` no Drive sozinho: uma
  pasta que não aparece pode ser o Drive ainda carregando ou outra conta montada primeiro, e criar ali fazia uma
  segunda pasta vazia. Quem tem o Drive e ainda não tem a pasta vê "Google Drive não encontrado" e precisaria criá-la
  à mão. Isso vai contra o princípio de o app fazer sozinho.
- **Conta errada.** `CloudLocator.Locate` pega a primeira unidade do Google Drive que tem `retroboat` e, se nenhuma tem,
  a primeira. Com duas contas, ele decide pela ordem das letras. Não há como escolher, fora o `[Cloud] Path` do
  `retroboat.ini`, que é para testes.
- **Primeiro uso fora do padrão.** O `LibraryFolderForm` só pergunta a pasta de jogos: janela comum do Windows, sem
  controle e fora do design da Steam.
- **Configurações só no arquivo.** Vídeos, manuais, capas automáticas e "abrir o Drive sozinho" só mudam editando o
  `retroboat.ini`.

## 2. Regras

1. **O Drive é a fonte de tudo.** Nada é criado no Drive sem confirmação, exceto dentro de uma biblioteca já
   identificada (seção 3). Criar é sempre `CreateDirectory` ou `FileMode.CreateNew`; nada é sobrescrito, apagado,
   movido ou renomeado. A tabela da seção 9 é a lista completa.
2. **Pergunta pouco.** O setup só aparece quando o app não consegue decidir sozinho, e a escolha fica lembrada.
3. **Design da Steam e controle.** Tela cheia, A escolhe, B volta, cima/baixo navega, como a `ChoiceForm` do
   `retroboat-launch`. Mouse e teclado também funcionam.
4. **Sem depender do interior do Google Drive para computador.** As contas ficam em bancos SQLite em
   `%LOCALAPPDATA%\Google\DriveFS\<id>` (formato interno, muda sem aviso). O registro `PerAccountPreferences` só tem o id
   numérico da conta, sem o e-mail. O tamanho da unidade `G:` é o do disco local (930 GB neste PC), não a cota da
   conta. Ou seja: o app não tem como mostrar "conta fulano@gmail.com" de forma confiável, e a identificação usa um
   arquivo nosso (seção 3).
5. **O ES oficial continua funcionando.** O setup é do `Retroboat.exe`; o menu NUVEM só existe no nosso ES. Com o oficial,
   as configurações ficam no `Retroboat.exe --setup`.

## 3. Identidade da biblioteca

Arquivo `retroboat\biblioteca.xml` no Drive, criado uma vez e nunca reescrito:

```xml
<biblioteca id="9b2f6c1e-…" criada="2026-11-08T21:00:00Z" pc="PC-SALA" versao="1" />
```

- No PC: `[Cloud] Library=<id>` no `retroboat.ini`.
- **Com `Library`:** o `Locate` procura em todas as unidades do Google Drive um `retroboat\biblioteca*.xml` com aquele id
  (o `*` cobre a cópia que o Drive renomeia num conflito). Achou: é ela, em qualquer letra. Não achou: o app fica sem
  nuvem, como com o Drive fechado, e **nunca** cai para outra conta.
- **Sem `Library`:** quem decide é a tabela da seção 4.
- **Com `[Cloud] Path`:** nada muda (testes e pastas fora do Drive).
- Gravação com `FileMode.CreateNew`: se o arquivo já existir, falha, e o app lê o que está lá.
- Uma `retroboat` sem `biblioteca.xml` é uma biblioteca de antes do RB-021: o app adota (seção 4) e cria o arquivo.
- Todos os lugares que chamam `CloudLocator.Locate` (lançador, `retroboat-launch`, CLI, `CloudWatcher`) passam a
  encontrar a biblioteca certa sem mudar, porque a regra fica dentro do `Locate`. Ler um arquivo de poucos bytes por unidade cabe no ciclo de 5 s do `CloudWatcher`.
- Com a biblioteca identificada, o `CloudFolders` pode criar `jogos` se ele faltar: o mesmo diretório que mostrou o
  `biblioteca.xml` está carregado.

## 4. Quando o setup aparece

| Situação | O que acontece |
|---|---|
| Primeiro uso neste PC (sem `RomsPath`) | Setup completo (passos 1 a 3). A biblioteca encontrada já vem marcada. |
| Sem `Library` e exatamente uma biblioteca em uma conta | **Adota sozinho**, sem tela: cria o `biblioteca.xml` se faltar e avisa no ES "Biblioteca da nuvem: Meu Drive (G:)". É o caso deste PC hoje. |
| Sem `Library` e bibliotecas em duas ou mais contas | Setup, passo 1 (escolher). |
| Sem `Library`, Drive aberto e nenhuma biblioteca | Setup, passo 1, com "Criar uma biblioteca nova" (regras em 5.1). |
| Sem `Library` e Drive fechado | A pergunta de abrir o Drive (RB-019); depois volta a esta tabela. "Agora não" abre sem nuvem e sem setup. |
| `Library` lembrada e não encontrada, com o Drive aberto | **Não abre o setup sozinho.** Abre sem nuvem, com o aviso "Biblioteca da nuvem não encontrada no Google Drive", e o menu NUVEM oferece procurar de novo ou escolher outra. Drive carregando, conta desconectada e biblioteca apagada parecem iguais daqui. |
| `Retroboat.exe --setup` ou pedido do menu NUVEM | Setup completo, com as escolhas atuais marcadas. |
| `[Cloud] Path` configurado | Nada muda. |

## 5. Telas do setup

Tela cheia no padrão da `ChoiceForm`: título pequeno "RETROBOAT · CONFIGURAÇÃO", passo ("1 de 3"), título grande, texto,
lista de opções com foco azul `#1A9FFF` e a linha de ajuda "A escolher · B voltar". Cores, tipografia e espaçamento do
[design system](design-system-steam.md). O `Retroboat.Launcher` já usa o projeto `Retroboat.Launch` (a `CloudAppPrompt`
vem de lá), então reaproveita `Theme`, `GamepadButtons` e `ChoiceForm` sem cópia. O `LibraryFolderForm` sai.

### 5.1 Passo 1: biblioteca na nuvem

Uma linha por unidade do Google Drive:

- `Meu Drive (G:)` · Biblioteca encontrada · 13 jogos de PS2
- `Meu Drive (H:)` · Sem biblioteca

O que distingue as contas é o conteúdo (jogos e sistemas), porque a letra sozinha não diz qual conta é (regra 4).

Opções: "Usar esta biblioteca" para cada uma encontrada, "Criar uma biblioteca nova em G:" quando há conta sem
biblioteca, "Procurar de novo" e "Continuar sem nuvem (só este PC)".

**Criar uma biblioteca nova:**

- Só aparece com o Drive aberto (processo `GoogleDriveFS` rodando, "Meu Drive" listável) e depois de uma segunda busca,
  5 s após a primeira, que também não achou nada em nenhuma conta.
- Texto: "Não achei uma biblioteca do Retroboat neste Google Drive. Se você já usa o Retroboat em outro PC com esta conta,
  espere o Drive terminar de carregar e escolha Procurar de novo."
- Ao confirmar, o app busca de novo. Se a `retroboat` apareceu, adota em vez de criar. Senão, cria `retroboat`, o
  `biblioteca.xml` e `jogos` com as pastas de console. `bios`, `saves`, `midia`, `configuracoes` e `contas` continuam
  sendo criadas pelas sincronizações quando têm o que guardar, como hoje.

### 5.2 Passo 2: pasta de jogos deste PC

Opções que funcionam no controle:

- "Pasta padrão do RetroBat (C:\RetroBat\roms) · 120 GB livres"
- "D:\Jogos · 800 GB livres": uma por disco fixo, do maior espaço livre para o menor, no máximo 3
- "Manter a atual (C:\Stuffs\Jogos)", quando o setup roda de novo
- "Escolher outra pasta… (mouse e teclado)", que abre o seletor do Windows

Trocar a pasta não move os jogos baixados nesta versão: eles ficam na pasta antiga. Na nova, os jogos da nuvem voltam a
aparecer como "Na nuvem"; os que só existem neste PC ficam só na pasta antiga. O texto do passo avisa isso e mostra
quanto ocupam. Mover junto fica para depois (seção 10).

### 5.3 Passo 3: opções principais

A liga ou desliga; "Concluir" grava o `retroboat.ini` e segue o início normal.

| Opção | Chave | Padrão |
|---|---|---|
| Baixar os vídeos dos jogos (ocupa mais espaço) | `[Cloud] Videos` | desligado |
| Baixar os manuais (PDF) | `[Cloud] Manuals` | desligado |
| Capas e informações automáticas | `[Media] Automatic` | ligado |
| Abrir o Google Drive sozinho quando precisar | `[Cloud] AutoOpen` | desligado |

Tudo isso continua por PC (o `retroboat.ini` não vai para a nuvem): espaço em disco e hábito com o Drive mudam de um
PC para outro.

## 6. Menu NUVEM no nosso ES

**Entrada.** "NUVEM" no menu principal, só no modo completo da interface, logo depois do item de configurações de jogos
(`GAME SETTINGS` no código), com o mesmo ícone de nuvem do cabeçalho e da lista de jogos (seção 13). Uma linha em
`GuiMenu.cpp` com `// RETROBOAT RB-021`; a tela fica em
`es-app/src/retroboat/RetroboatSettingsGui.cpp` e `.h` (nossos), com textos em português como literais.

**Tela** (`GuiSettings`):

| Linha | Tipo | De onde vem |
|---|---|---|
| GOOGLE DRIVE: Conectado / Fechado / Biblioteca não encontrada | só leitura | `estado\nuvem-conectada` e o resultado da busca |
| BIBLIOTECA: Meu Drive (G:) · 13 jogos | só leitura | `retroboat.ini` e `biblioteca-nuvem.xml` |
| PASTA DE JOGOS DESTE PC: C:\Stuffs\Jogos | só leitura | `retroboat.ini` |
| BAIXAR VÍDEOS · BAIXAR MANUAIS · CAPAS E INFORMAÇÕES AUTOMÁTICAS · ABRIR O GOOGLE DRIVE SOZINHO | chaves | `retroboat.ini` |
| TROCAR BIBLIOTECA OU PASTA DE JOGOS… | ação | seção 6.2 |
| PROCURAR A BIBLIOTECA DE NOVO | ação, só com a biblioteca não encontrada | reabre a tela (`retroboat-cli settings get` de novo) |

**Gravação.** O ES lê o `retroboat.ini`, mas só grava pelo `retroboat-cli settings set <chave> <valor>`, ao sair da tela
(`addSaveFunc`) e só com as chaves que mudaram. O processo é criado como no RB-010 (seção 3.3: `CreateProcessW`, sem
expandir variáveis). Assim o `RetroboatSettings` continua sendo o único que escreve o arquivo.

### 6.1 Mudança ao vivo

- **Abrir o Drive sozinho:** vale na hora; cada `retroboat-launch` lê o `retroboat.ini` ao abrir.
- **Vídeos, manuais e capas automáticas:** o `Retroboat.exe` em segundo plano (`CloudWatcher`) percebe a data nova do
  `retroboat.ini` e roda a parte de mídia da sincronização quando o ES estiver parado, na mesma regra de quando o Drive
  conecta (sem jogo aberto nem scraper rodando). Depois recarrega o ES e avisa. Sem o `Retroboat.exe` em segundo plano,
  vale na próxima abertura.

### 6.2 Trocar biblioteca ou pasta de jogos

Isso refaz a biblioteca inteira, então não dá para fazer com o ES aberto:

1. O ES confirma: "O ES vai fechar e o Retroboat abre a configuração. Nenhum jogo é apagado."
2. Roda `retroboat-cli settings request-setup`, que cria `estado\pedido-setup`, e fecha o ES.
3. O `Retroboat.exe` em segundo plano vê o ES fechar com o pedido, mostra o setup e abre o RetroBat de novo.
4. Sem o `Retroboat.exe` em segundo plano (RetroBat aberto direto), o pedido espera a próxima abertura do `Retroboat.exe`.

## 7. Mudanças no app (C#)

| Onde | O quê |
|---|---|
| `Core\Cloud\LibraryIdentity` (novo) | Lê e cria o `biblioteca.xml` (`CreateNew`). |
| `Core\Cloud\CloudLocator` | `Locate` com o id da biblioteca; `FindLibraries()` lista cada unidade (letra, "Meu Drive", tem `retroboat`, id, jogos por sistema). |
| `Core\Settings\RetroboatSettings` | `[Cloud] Library`. |
| `Core\Cloud\CloudFolders` | Com a biblioteca identificada, cria `jogos` se faltar. |
| `Launcher\SetupFlow` e `SetupForm` (novos) | A tabela da seção 4 e as telas da seção 5. Sai o `LibraryFolderForm`. |
| `Launcher\CloudWatcher` | Data do `retroboat.ini` (6.1) e `pedido-setup` ao fechar o ES (6.2). |
| `Cli` | `settings get`, `settings set`, `settings find` e `settings request-setup`. |

## 8. Mudanças no ES (fork)

- Novos: `es-app/src/retroboat/RetroboatSettingsGui.cpp` e `.h`, na lista do `retroboat.cmake` (compila com `/utf-8`).
- Upstream: uma linha em `es-app/src/guis/GuiMenu.cpp`, que é um arquivo que muda muito; o gancho de uma linha segue a
  regra do RB-010.
- Rebuild do fork e cópia do `emulationstation.exe` para `build\emulationstation`.

## 9. O que pode ser escrito no Drive

Esta é a lista completa. Qualquer outra escrita no Drive fica fora do RB-021.

| O quê | Quando | Como |
|---|---|---|
| `retroboat`, `jogos` e as pastas de console | Só em "Criar uma biblioteca nova", confirmado e depois da nova busca | `CreateDirectory` |
| `biblioteca.xml` | Ao criar ou adotar uma biblioteca | Temporário `.biblioteca-<guid>.tmp` (`FileMode.CreateNew`) movido para o nome final; o mover falha se ele já existe. Nunca reescrito |
| Apagar o próprio `.biblioteca-<guid>.tmp` | Só quando o mover falhou porque outro PC criou o `biblioteca.xml` no mesmo instante | `File.Delete` do temporário que acabou de criar |
| `jogos` e pastas de console que faltam | A cada início, numa biblioteca identificada | `CreateDirectory`, só o que falta |

**Prova antes de cada commit:**

- o teste de armadilhas da validação de 08/11/2026 (hash SHA-256 e data de todos os arquivos antes e depois, um arquivo
  com nome de console, pasta em outra caixa, pasta oculta, pasta de sistema que saiu);
- os casos novos: duas contas, Drive carregando (busca que só acha na segunda vez), `biblioteca.xml` já existente,
  conflito de `biblioteca.xml`;
- um teste no `G:` real, numa pasta de teste e só com o OK do usuário.

## 10. Riscos aceitos e pendências

- **Dois PCs criando ao mesmo tempo.** Só acontece se os dois confirmarem "Criar uma biblioteca nova" antes de o Drive
  sincronizar. A segunda busca e o texto do passo 1 reduzem isso. Falta ver no Drive real como o Drive para computador
  mostra duas pastas com o mesmo nome.
- **Dois PCs adotando no mesmo minuto** podem gravar dois `biblioteca.xml` com ids diferentes. O `Locate` aceita
  `biblioteca*.xml`, então cada PC continua achando o seu. Unificar os ids fica para depois.
- **Contas sem e-mail na tela.** O passo 1 distingue as contas pelo conteúdo. Ler o e-mail do DriveFS fica fora (regra 4).
- **Trocar a pasta de jogos não move os jogos baixados.** Próximo passo natural: "Mover os jogos baixados (N GB)".
- **ES oficial:** sem menu NUVEM; as configurações ficam no `Retroboat.exe --setup`.

## 11. Ordem de implementação

Cada passo vira um commit próprio, testado numa cópia isolada do RetroBat (o ES com `--home` apontando para a cópia,
pilotado pela API da porta 1234) e depois com o usuário no Drive real.

1. **Identidade da biblioteca**, `Locate` com o id e adoção sozinha. Resolve o risco da conta errada sem nenhuma tela
   nova. **Feito em 09/11/2026** (`LibraryIdentity`, `LibraryChoice`, `CloudLocator.Locate(settings)`), com 10 testes
   novos, entre eles o de armadilhas com hash. A identidade nova é gravada num arquivo temporário oculto
   (`.biblioteca-<guid>.tmp`) e ganha o nome final com um mover que falha se o nome já existe. Se outro PC criou no mesmo
   instante, o temporário é apagado e vale o que está lá. É o único arquivo que o Retroboat apaga no Drive, e só o
   temporário que ele mesmo acabou de criar. Falta o teste no `G:` real: a primeira abertura cria
   `G:\Meu Drive\retroboat\biblioteca.xml` e nada mais.
2. **Setup no `Retroboat.exe`** (passos 1 a 3), no lugar do `LibraryFolderForm`.
3. **`retroboat-cli settings` e menu NUVEM** no nosso ES.
4. **Mudança ao vivo** (6.1) e **pedido de setup** pelo ES (6.2).

## 12. Decisões do usuário

| # | Pergunta | Recomendação |
|---|---|---|
| 1 | Com uma biblioteca só, adotar sozinho ou confirmar uma vez? | Adotar sozinho: é a biblioteca que já está em uso, e adotar não cria nada além do `biblioteca.xml`. |
| 2 | Onde fica a entrada NUVEM? | Menu principal, logo depois do item de configurações de jogos. |
| 3 | Trocar a pasta de jogos sem mover os baixados, nesta versão? | Sim, com o aviso de quanto fica na pasta antiga. |
| 4 | O `biblioteca.xml` fica visível na pasta `retroboat` do Drive. Tudo bem? | Sim. No Drive não existe arquivo oculto de verdade, e um nome com ponto continuaria aparecendo no site. |

## 13. Como ficou (09/11/2026)

Commits no app: `cb52949` (passo 1), `13058d2` (passo 2), `f5029ef` (passo 3, CLI), `ebe6bb4` (passo 4). No fork do ES:
`b20a2827c` (menu NUVEM) e `70a76616d` (ícone de nuvem na entrada). 269 testes.

Diferenças do desenho:

- **Sem `settings find`.** "Procurar a biblioteca de novo" só reabre a tela, que roda `settings get` outra vez; a busca é a
  mesma.
- **"Continuar sem nuvem por agora"** não fica lembrado: com o Drive aberto e sem biblioteca, o setup volta na próxima
  abertura, como o "Agora não" da pergunta do Drive (RB-019).
- **Avisos técnicos da biblioteca** (sistemas com o mesmo nome no `es_systems.cfg` e outros) numa caixa do Windows só no
  primeiro uso; com `--setup` ou pelo menu NUVEM, vão só para o log.
- **A pergunta do Drive vem antes do setup**, para o passo 1 já ver as contas.
- **Pasta de jogos:** as opções mostram "Pasta atual · 120 GB livres · C:\...", com o caminho por último, e um caminho longo
  termina em reticências. No menu NUVEM, caminhos longos viram `C:\…\pasta\sub`.
- **Ícone da entrada NUVEM:** o mesmo de nuvem do cabeçalho e da lista de jogos (U+F0C2 do FontAwesome), não o
  `iconNetwork`, que é de rede. Os temas só dão ícones às entradas do próprio ES, então a nossa monta a linha: o glyph na
  caixa dos ícones do tema (1,25 letra, quadrada), com a cor do texto e a de seleção. Num tema sem ícones no menu, a
  entrada fica sem ícone, como as outras. Conferido no PlayStation-X e no Carbon.
- **Leitura pelo ES:** o `settings get` leva 50 ms (250 ms quando conta os jogos no `G:`); a tela abre sem espera visível.
- **Pedido de setup:** o `Retroboat.exe` em segundo plano olha a cada segundo se o ES fechou com `estado\pedido-setup`, abre
  outro `Retroboat.exe` com `--setup` e sai; o novo mostra o setup e reabre o RetroBat.

Testado numa cópia isolada:

- **Setup:** os três passos, capturados fora da área visível; com o `G:` real só lido; o pedido do menu abre o setup completo
  na abertura seguinte e é consumido.
- **Menu NUVEM:** entrada no menu principal; estado, biblioteca e pasta; a chave de vídeos gravada pelo `settings set` ao
  sair da tela; confirmação de troca; pedido criado e ES fechado.

No build do usuário (09/11/2026): o primeiro início adotou a biblioteca do `G:` sozinho (`biblioteca.xml` criado e
`Library=` no `retroboat.ini`); manuais e "abrir o Drive" ligados no menu NUVEM chegaram ao `Retroboat.exe` em segundo
plano, que buscou a mídia com o ES aberto.

Ainda não testado:

- o ciclo com o `Retroboat.exe` em segundo plano (ES fecha pelo menu, setup, RetroBat reaberto);
- o controle.
