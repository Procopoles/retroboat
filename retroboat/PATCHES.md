# Registro de patches do Retroboat

Toda mudança do fork em relação ao upstream tem uma linha aqui. Critérios em [FORK.md](FORK.md#3-classificação-de-cada-mudança).

Status: `candidato` (identificado, não feito) · `em andamento` · `ativo` (em `main`) ·
`enviado` (PR aberto no upstream) · `absorvido` (o upstream corrigiu; o nosso foi removido) · `descartado`.

| ID | Mudança | Repo | Camada | Calor | Risco de merge | Upstreamável | Status |
|---|---|---|---|---|---|---|---|
| RB-001 | Pasta `retroboat/` com guia, registro e scripts de sincronização | retrobat | C0 | — | nenhum | não | em andamento |
| RB-002 | ES ignora todos os overlays seguintes quando um `es_systems_*.cfg` é inválido | ES (Batocera) | C1 | média | baixo | sim | candidato |
| RB-003 | Biblioteca isolada: pasta de jogos escolhida pelo usuário, gamelists e mídia fora dela | retrobat (+ componente novo) | C0 | — | nenhum (semântico: baixo) | não | em andamento |
| RB-004 | Biblioteca na nuvem: instalar, fila, desinstalar, verificar (integração C0 com o ES) | retroboat/app | C0 | — | nenhum (semântico: médio) | não | em andamento |
| RB-005 | Perfil e configurações na nuvem: chaves portáveis, controles, metadados, estatísticas | retroboat-cloud | C0 | — | nenhum (semântico: médio) | não | candidato |
| RB-006 | Base da nuvem: provedor de pasta (Google Drive para computador, NAS), verificação de integridade, lançador, `retroboat-launch` | retroboat/app | C0 | — | nenhum | não | em andamento |
| RB-007 | ExoDOS cria atalhos em `retrobat\roms` ignorando a chave `roms=` | emulatorlauncher | C1 | média | baixo | sim | candidato |
| RB-008 | Opção para o `retrobat.exe` esperar um `AppLauncher` terminar antes de abrir o ES | retrobat-executable | C1 | média | baixo | sim | candidato |
| RB-009 | Saves e memory cards na nuvem: sync por jogo, conflito, offline, snapshots | retroboat-cloud | C0 | — | nenhum (semântico: médio) | não | candidato |
| RB-010 | Interface nativa de nuvem no ES: status, instalar/desinstalar, tela de downloads | ES (Batocera) | C2 | ALTA/média | alto | talvez (só se genérico) | candidato |
| RB-011 | Build Windows do ES no nosso fork (GitHub Actions) | ES (Batocera) | C0 | — | nenhum | não | candidato |
| RB-012 | Sistema Ghostship cadastrado com o nome "starship" (nome duplicado no `es_systems.cfg`) | retrobat | C1 | ALTA | médio | sim | candidato |
| RB-013 | BIOS na nuvem: organizar, sincronizar e mostrar status (lançador + notificação no ES) | retroboat/app | C0 | — | nenhum (semântico: baixo) | não | em andamento |

---

## RB-001 — Infraestrutura do fork

- **Arquivos:** `retroboat/**` (todos novos).
- **Por quê:** dar ao fork um lugar próprio para documentação e ferramentas, sem tocar em nada do upstream.
- **Risco semântico:** nenhum. O RetroBuild só copia `system/` e os `*.txt` da raiz, então a pasta não entra na distribuição.
- **Configuração local (não versionada, refazer em cada clone):**

  ```bash
  git remote add upstream https://github.com/RetroBat-Official/retrobat.git
  git remote set-url --push upstream DISABLED-read-only
  git config rerere.enabled true
  git config rerere.autoupdate true
  git config merge.conflictstyle zdiff3
  ```

## RB-002 — Um overlay inválido desliga os outros (candidato)

- **Onde:** `es-app/src/SystemData.cpp`, `SystemData::loadAdditionnalConfig`, upstream `batocera-linux/batocera-emulationstation`.
- **Problema:** quando um `es_systems_*.cfg` não é XML válido, ou não tem `<systemList>`, a função dá
  `return` em vez de `continue`. Todos os overlays que viriam depois, nas duas pastas, são ignorados
  sem aviso. A função equivalente de `es_features_*.cfg` (`CustomFeatures.cpp`) já usa `continue`.
- **Por que importa para nós:** a regra 1 do FORK.md coloca as nossas mudanças de sistema num
  overlay. Um arquivo `es_systems_*.cfg` quebrado do usuário, lido antes do nosso, desligaria em
  silêncio todas as nossas correções.
- **Correção:** trocar os dois `return;` por `continue;` (2 linhas).
- **Caminho recomendado:** PR direto no Batocera. Como não precisa de build próprio do ES, é só
  esperar o merge e o espelho do RetroBat. Não precisa virar patch do fork.

## RB-003 a RB-006, RB-009 a RB-011 — Biblioteca isolada e cloud-first

Desenho em [design/cloud-first.md](design/cloud-first.md) (nuvem) e
[design/biblioteca-e-nuvem.md](design/biblioteca-e-nuvem.md) (biblioteca isolada e pontos de
extensão). O cloud-first trocou o rclone por um motor de sincronização próprio, que usa como
transporte o app Google Drive para computador (sem API e sem login nosso). Resumo da classificação:

- **RB-003 (biblioteca isolada):** C0. Usa overlay de `es_systems` gerado a cada início, a chave
  `roms=` do emulatorLauncher e a pasta alternativa de gamelists que o ES já suporta. Risco
  semântico baixo: como o overlay é regenerado, sistemas novos do upstream entram sozinhos.
  Validado em 24/10/2026 com o ES real: jogos lidos da pasta nova, gamelist local usado e regravado
  ao sair, nada escrito na pasta de jogos.
- **RB-004 (biblioteca na nuvem):** C0 na fase 1 (arquivos marcadores, coleção "☁ Na nuvem",
  notificações pela API HTTP do ES, download com progresso no `retroboat-launch`). A fase 2 (selo e
  "Baixar" nativos no ES) é C2 no ES e só deve começar depois do build próprio do ES.
- **RB-005 (perfil na nuvem):** C0. Risco semântico médio: chaves novas do `es_settings.cfg` que
  dependem da máquina precisam entrar na lista de chaves locais, senão viajam de um PC para o outro.
- **RB-006 (base):** C0. Componente novo em repositório próprio (`retroboat-cloud`); o build
  baixa os releases dele.
- **RB-009 (saves e memory cards):** C0. Risco semântico médio: se o upstream mudar onde um
  emulador grava os saves, a regra correspondente no catálogo de saves fica errada sem conflito de
  merge. O `upstream-check.sh` deve alertar quando um gerador mudar caminhos de `saves`.
  Memory cards do PS2 funcionam como no console: cartões com nome, escolhidos por slot e por jogo,
  colocados nos arquivos fixos `Mcd001.ps2`/`Mcd002.ps2` que o emulatorLauncher configura no PCSX2.
  Isso depende do bloco de memory cards de `Pcsx2.Generator.cs` (`SetupConfigurationQT`), que
  entra na lista de vigilância.
- **RB-010 (ES nativo):** C2 em arquivos quentes do ES (`FileData.cpp`, `GuiGameOptions.cpp`,
  bindings de tema). Só começa depois do RB-011. Lê a API local do `retroboat-cloud`, então a
  lógica de nuvem continua fora do ES.
- **RB-011 (build do ES):** C0. Arquivo de workflow novo no nosso fork do ES. Também é pré-requisito
  do redesign (etapa 2).

**Implementação (24/10/2026):** o código fica em [app/](app/README.md), dentro deste repositório,
e não num repositório separado como o cloud-first propunha. A pasta não é tocada pelo upstream nem
empacotada pelo RetroBuild, então o risco de merge continua zero, e o build e a documentação ficam
num lugar só. Se um dia precisar de releases independentes, `git subtree split` extrai a pasta com
o histórico.

## RB-012 — Ghostship cadastrado como "starship" (candidato)

- **Onde:** `system/templates/emulationstation/es_systems.cfg` (calor ALTA), commit upstream `0c78f16` ("Add ghostship", 27/03/2026).
- **Problema:** o sistema da pasta `ghostship` (fullname "Ghostship") tem `<name>starship</name>`,
  igual ao sistema Starship. O ES não junta nomes repetidos: os dois sistemas dividem o mesmo
  gamelist local, tema e opções, e o `es_features.cfg` já tem as opções em `emulator name="ghostship"`.
- **Efeito no Retroboat:** um overlay só consegue alterar o primeiro sistema com aquele nome. A
  RB-003 detecta, avisa e deixa o Starship na pasta padrão.
- **Correção:** trocar o `<name>` para `ghostship` (1 linha). **Caminho recomendado:** PR no
  upstream. Editar aqui seria C1 no arquivo mais quente do repositório.

## RB-004 — Biblioteca na nuvem (fase 1)

- **Onde:** `retroboat/app` (`Cloud\CloudLibrarySync`, `CloudIndex`, `OnDemandInstaller`,
  `ZipCatalog`, projeto `Retroboat.Launch`). No RetroBat, só o overlay gerado
  `es_systems_retroboat.cfg`, que agora também troca o `<command>` dos sistemas com jogos na nuvem.
- **Como funciona:** marcador vazio com o nome final do jogo + descrição no gamelist local.
  `retroboat-launch` na frente do emulatorLauncher baixa o jogo quando o `-rom` é um marcador.
- **Risco semântico médio:**
  - Depende do formato do `<command>` do `es_systems.cfg`: o comando original é envolvido como está,
    e o `-rom` é procurado nos argumentos.
  - Depende de o ES aceitar arquivo vazio como jogo, o que foi verificado no ES de 22/10/2026.
  - Se o upstream passar a validar o tamanho das ROMs, os marcadores somem da lista sem conflito de merge.
  - Os três pontos entram na lista de vigilância.

## RB-013 — BIOS na nuvem

- **Onde:** `retroboat/app` (`Bios\`, `Cloud\StartupSync`, `Es\EsApi`, lançador). Nada no upstream.
- **Desenho:** seção 4b do [cloud-first](design/cloud-first.md).
- **Risco semântico baixo:** o catálogo de BIOS copia a lista de nomes e a ordem de busca do
  `Pcsx2.Generator.cs` (`SetupConfigurationQT`). Se o upstream mudar essa lista ou a pasta
  `bios\pcsx2\bios`, o status fica errado sem conflito de merge. Esse trecho entra na lista de
  vigilância.

## RB-007 — ExoDOS ignora a pasta de jogos configurada (candidato)

- **Onde:** `emulatorLauncher/Generators/ExoDOS.Generator.cs:483` (6 alterações/ano), `RetroBat-Official/emulatorlauncher`.
- **Problema:** `CreateShortcuts` monta o caminho como `GetFullPath("retrobat") + "roms"` em vez de
  `GetFullPath("roms")`, então ignora `roms=` no `emulatorLauncher.cfg`. Com a RB-003, os atalhos
  do ExoDOS iriam para a pasta errada.
- **Correção:** uma linha. **Caminho recomendado:** PR no upstream.

## RB-008 — `AppLauncher` com espera (candidato)

- **Onde:** `RetroBat/RetroBat/EmulationStationLauncher.cs` (5 alterações/ano), `RetroBat-Official/retrobat-executable`.
- **Problema:** os apps do `AppLauncher` são abertos em paralelo, sem esperar. Não dá para rodar
  algo que precise terminar antes do ES abrir, como baixar o perfil da nuvem.
- **Correção:** um sufixo `-wait`, análogo ao `-nowindow` que já existe. **Caminho recomendado:**
  PR no upstream. Se for aceito, o `Retroboat.exe` deixa de ser necessário como lançador.

---

## Modelo para novas entradas

```markdown
## RB-NNN — Título curto

- **Onde:** repositório e arquivos (com o calor de cada um, de HOTSPOTS.md).
- **Problema:** o que o usuário vê / como reproduzir.
- **Correção:** o que muda, em uma ou duas frases.
- **Classificação:** camada C?, calor ?, risco de merge ?. Justifique se for C2 ou C3.
- **Risco semântico:** o que pode quebrar em silêncio num merge futuro (ex.: tags que o overlay sobrescreve).
- **Upstreamável:** sim/não; link do PR quando enviado.
- **Como testar:** passos curtos.
```
