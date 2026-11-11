# RB-033 — Emuladores: sempre a última versão, junto com o jogo

Pedido de 11/11/2026. A regra mudou: os emuladores ficam sempre na última versão. Antes, o plano era uma versão fixa e
testada. Se uma versão nova mudar algo de que o Retroboat depende (nome do executável, pasta de saves, arquivo de
configuração), o app só precisa perceber, registrar de forma clara no log e avisar o usuário daquele problema; a correção
vem depois. O código dessa verificação fica num arquivo próprio.

Entram três peças: o download junto com o jogo, o aviso em português no estilo da Steam e a conferência do arquivo baixado.

## 1. De onde vem cada emulador

É o mesmo repositório que o RetroBat usa no primeiro jogo. Os endereços foram achados consultando o servidor, porque não
aparecem no emulatorLauncher:

| O quê | Endereço |
|---|---|
| Emulador (programa) | `https://www.retrobat.ovh/repo/win64/stable/emulators/<nome>.7z` |
| Versão de cada emulador | `.../emulators/versions.xml` (`<system name="flycast" version="2.7.0.1"/>`) |
| Core do RetroArch | `.../emulators/cores/<core>_libretro.dll.zip` |

O destino também é o mesmo do RetroBat: `<RetroBat>\emulators\<nome>`, e os cores em `emulators\retroarch\cores`. O
emulatorLauncher só regrava as configurações a partir dali, e o sync de saves depende das pastas que ele define.

**Instalado** quer dizer que o programa existe. O RetroBat já vem com uma pasta para cada emulador, com modelos de
configuração e sem nenhum `.exe`: neste build, só o PCSX2 e o RetroArch têm executável. Para um core, instalado é o
`<core>_libretro.dll` existir.

## 2. Sempre a última versão

- **Instalar:** baixa o que o repositório tem agora.
- **Atualizar:** antes de cada jogo, a versão do `versions.xml` é comparada com a do executável. A do executável é a
  `FileVersion`; sem ela, a data do arquivo, no formato que o RetroBat usa (`0.aa.MM.dd`). Se o repositório tiver uma
  versão mais nova, o emulador é atualizado antes do jogo, sem perguntar, na mesma tela de andamento.
- **Sem internet ou com falha:** o jogo abre com a versão instalada, e o log registra o motivo.
- **Cores:** quase nenhum tem versão no repositório. A atualização compara a data do pacote no servidor (`Last-Modified`)
  com a do pacote que o Retroboat instalou. Um core que o RetroBat instalou antes desta mudança é adotado como está.
- **Consultas ao servidor:** ficam guardadas por 6 horas em `retroboat\estado\emuladores-servidor.xml`. Abrir um jogo não
  espera a internet a cada vez. Depois de uma falha de conexão, as consultas esperam 10 minutos antes de tentar de novo; o
  "Tentar de novo" do aviso não espera.
- **Configurações do usuário:** numa atualização, os arquivos que o RetroBat nunca sobrescreve (`system\upgrade.xml`,
  `<neveroverwrite>`) ficam como estão.

## 3. Conferência do arquivo

Cada passo que falha para a instalação, e nada é trocado na pasta do emulador:

1. **Tamanho:** os bytes recebidos são exatamente o `Content-Length` do servidor.
2. **Pacote íntegro:** o `7za t` (`system\tools\7za.exe`) confere o CRC de cada arquivo do `.7z` ou do `.zip`.
3. **Extração:** é feita numa pasta temporária (`emulators\.retroboat-temp`), nunca direto na pasta do emulador.
4. **Programa esperado:** existe no pacote e é um executável válido do Windows (cabeçalhos MZ e PE).

Só então os arquivos vão para o lugar. Um download pela metade continua de onde parou (`.retroboat-part`).

## 4. Junto com o jogo

Quando um jogo entra na fila de downloads, o Retroboat descobre qual emulador ele vai usar, pela mesma ordem do ES:

1. a escolha feita para o jogo (gamelist `<emulator>`/`<core>`);
2. a escolha feita para o sistema (`<sistema>.emulator`/`.core` no `es_settings.cfg`);
3. o padrão do sistema (`default="true"`, do overlay do Retroboat ou do `es_systems.cfg`).

Se esse emulador falta ou tem versão nova, ele baixa depois do jogo, como parte do mesmo download: a barra soma os dois
desde o começo, e o jogo só fica PRONTO quando os dois terminam. Se o emulador falhar (sem internet, por exemplo), o jogo
fica instalado assim mesmo, e o emulador vem pelo aviso da seção 5 quando o jogo for aberto.

## 5. Aviso quando falta (estilo Steam, em português)

Ao abrir um jogo cujo emulador não está no PC, aparece a tela cheia do Retroboat, a mesma das outras perguntas, feita
para controle:

> **Este jogo precisa do Flycast**
> O Flycast ainda não está neste PC. São 7,3 MB, do repositório do RetroBat; o jogo abre assim que terminar.
> [Baixar e jogar] [Agora não]

O download mostra andamento, velocidade e tempo restante, e B cancela. Uma falha diz o motivo e oferece tentar de novo.
Como o Retroboat instala antes, a janela em inglês do RetroBat ("The emulator is not installed") não aparece mais.

## 6. Verificação de compatibilidade (arquivo próprio)

Fica em `Retroboat.Core/Emulators/EmulatorCompatibility.cs`. Cada problema tem um código, uma frase para o usuário e o
detalhe técnico (o que era esperado e o que foi achado). O registro vai para `retroboat\logs\emuladores.log`, em bloco,
com o código entre colchetes, para dar para buscar.

| Quando | Código | O que confere |
|---|---|---|
| Depois de instalar ou atualizar, e antes de cada jogo | `EXECUTAVEL_MUDOU` | O executável que o emulatorLauncher procura existe; o log lista os `.exe` que vieram |
| | `EXECUTAVEL_INVALIDO` | O executável é um programa válido do Windows |
| | `RETROARCH_AUSENTE` | Um core precisa do `retroarch.exe` |
| | `REPOSITORIO_MUDOU` | O `versions.xml` ainda existe e tem o formato conhecido |
| Depois do jogo | `SAVE_FORA_DA_NUVEM` | O emulador gravou um save fora de `saves\` (na pasta dele ou em `bios\`), que não vai para a nuvem |
| | `PASTA_DE_SAVES_MUDOU` | O PCSX2 ainda usa a pasta de memory cards que o Retroboat sincroniza |
| | `CONFIGURACAO_SUMIU` | Os arquivos de configuração que o Retroboat sincroniza continuam onde ele procura |

**Aviso ao usuário:** a mesma tela cheia, com o problema em português e o caminho do log, e uma só opção, "Entendi". Cada
problema aparece uma vez por versão do emulador (`retroboat\estado\emuladores-avisos.xml`). O log registra todas as
vezes. Um problema que impede o jogo de abrir (executável sumiu) aparece antes do jogo, com "Voltar" e "Abrir mesmo
assim".

## 7. Menu EMULADORES (RB-023)

O estado "Instalado" de um emulador separado passa a exigir um `.exe` na pasta dele; antes, bastava a pasta ter algum
arquivo, o que deixava o Flycast separado como instalado sem estar. O texto de quem falta muda para "Não instalado: baixa
junto com o primeiro jogo".

## 8. Onde fica no código

| Peça | Arquivo |
|---|---|
| O que é um pacote, instalado ou não | `Retroboat.Core/Emulators/EmulatorPackage.cs` |
| Emulador do ES → pacote do repositório (nome, pasta, executável) | `Emulators/EmulatorCatalog.cs` + `EmulatorCatalog.xml` |
| Qual emulador um jogo usa | `Emulators/EmulatorChoice.cs` |
| Repositório do RetroBat (versões, tamanho, download) | `Emulators/RetroBatRepository.cs` |
| Versões (formatos do repositório e do executável) | `Emulators/EmulatorVersions.cs` |
| Instalado? Tem atualização? | `Emulators/PackageStatus.cs` |
| Cache das respostas do servidor | `Emulators/RepositoryCache.cs` |
| Conferência do arquivo | `Emulators/PackageCheck.cs` (+ `SevenZip.cs`) |
| Instalar e atualizar | `Emulators/EmulatorInstaller.cs` |
| Registro do que o Retroboat instalou | `Emulators/InstalledEmulators.cs` |
| Junto com o jogo | `Emulators/GameEmulatorDownload.cs` |
| Verificação de compatibilidade | `Emulators/EmulatorCompatibility.cs` |
| Log do emulador | `Emulators/EmulatorLog.cs` |
| Aviso e andamento (telas) | `Retroboat.Launch/EmulatorFlow.cs`, `Retroboat.Launch/ProgressForm.cs` |
| Linha de comando (manutenção) | `Retroboat.Cli/EmulatorCommands.cs`: `retroboat-cli emulators status|install|check` |

## 9. Testado

- **Repositório de verdade, numa cópia isolada do RetroBat:**
  - o core do Flycast (5,4 MB) e o Flycast separado 2.7.0.1 (7,3 MB) foram baixados, conferidos e instalados;
  - com o registro em 2.6, o 2.7.0.1 foi atualizado sozinho;
  - com o `flycast.exe` renomeado, apareceu `EXECUTAVEL_MUDOU`, com o executável que veio no lugar.
- **Testes automáticos** (`EmulatorTests`, 25 testes):
  - o pacote corrompido, o curto e o sem programa são recusados;
  - a configuração do usuário é mantida numa atualização;
  - o core instalado pelo RetroBat é adotado;
  - sem internet, o emulador instalado conta como em dia;
  - a barra soma jogo e emulador, e o jogo fica instalado se o emulador falhar;
  - cada código de compatibilidade é achado;
  - o aviso aparece uma vez por versão.
- **Telas** (desenhadas fora da tela): a pergunta, o andamento e o aviso.
- **Ainda não testado:** um jogo de verdade aberto pelo ES com o emulador faltando (a pergunta sobre o ES) e o download
  junto com um jogo da nuvem no build.
