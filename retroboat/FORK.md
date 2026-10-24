# Retroboat: como manter o fork perto do RetroBat

Objetivo: corrigir e melhorar o RetroBat e o EmulationStation **sem perder a capacidade de puxar
as atualizações oficiais**. Toda mudança é classificada antes de ser feita (seção 3) e registrada
em [PATCHES.md](PATCHES.md). Os números de "calor" vêm de [HOTSPOTS.md](HOTSPOTS.md).

Tudo o que é do fork mora em `retroboat/` na raiz. O upstream nunca cria essa pasta e o RetroBuild
não a empacota, então ela nunca gera conflito nem vai parar na distribuição.

---

## 1. Mapa: o RetroBat são vários repositórios

| Peça | Repositório upstream | Linguagem | Como entra no build | Commits/ano |
|---|---|---|---|---:|
| Configs, templates, mapeamentos de controle, menus | `RetroBat-Official/retrobat` (**este**) | XML/INI/YAML | pasta `system/` copiada pelo RetroBuild | 324 |
| `emulatorLauncher.exe` (gera config de cada emulador e dos controles), `es_features.cfg`, `es-update.exe` | `RetroBat-Official/emulatorlauncher` | C# | baixa `batocera-ports-x64.zip` (`build.ini: emulatorlauncher_url`) | 657 |
| EmulationStation (a interface) | `batocera-linux/batocera-emulationstation` | C++ | baixa `EmulationStation-Win64.zip` (`build.ini: emulationstation_url`) | 396 |
| `retrobat.exe` (inicialização, splash, foco da janela) | `RetroBat-Official/retrobat-executable` | C# | vem dentro de `retrobat_binaries.7z` do retrobat.org | 34 |
| `RetroBuild.exe` e `InstallerHost.exe` | `RetroBat-Official/retrobat-build` | C# | binários versionados na raiz deste repo | — |
| Tema, BIOS, bezels, tattoos | `es-theme-carbon`, `retrobat-bios`, `retrobat-bezels`, `retrobat-tattoos` | — | submódulos/downloads | — |

O que foi verificado em 24/10/2026 e muda a estratégia:

- **`RetroBat-Official/emulationstation` é só um espelho** do ES do Batocera: zero commits próprios,
  sincronizado por bot. O código-fonte de verdade do ES é `batocera-linux/batocera-emulationstation`,
  que já tem o suporte a Windows (`#ifdef WIN32`). Um fork nosso do ES deve partir do Batocera.
- **O build do ES para Windows não é público.** O release `continuous-master` não tem workflow
  visível. O `CMakeLists.txt` baixa sozinho as dependências de
  `batocera-emulationstation-win32-dependencies`, então montar um GitHub Actions nosso é viável,
  mas é pré-requisito para qualquer correção no ES.
- **O ES já tem pontos de extensão para configs**: ele carrega `es_systems_*.cfg` e
  `es_features_*.cfg` e mescla por cima do arquivo principal (`SystemData::loadAdditionnalConfig`,
  `CustomFeatures::loadAdditionnalFeatures`). Isso permite mudar sistemas e opções sem editar os
  arquivos mais quentes do upstream.
- **O atualizador embutido aponta para o RetroBat oficial**: `es-update.exe` lê
  `HKCU\SOFTWARE\RetroBat\InstallRootUrl`. Quem usar o nosso build e clicar em "Atualizar" recebe
  o RetroBat oficial por cima e perde as nossas correções (ver seção 6).

## 2. Onde cada tipo de correção mora

| Sintoma | Onde corrigir | Como distribuir |
|---|---|---|
| Sistema, extensão, emulador ou core listado errado/faltando | `es_systems` (este repo) | overlay `es_systems_retroboat.cfg` |
| Opção faltando ou errada no menu de opções avançadas | `es_features.cfg` (emulatorlauncher) | overlay `es_features_retroboat.cfg` |
| Mapeamento de botões / atalhos de teclado | `es_padtokey.cfg`, `system/resources/inputmapping/` (este repo) | edição pontual (arquivos quentes: mínimo possível) |
| Config default ruim de um emulador | `system/templates/<emulador>/` (este repo) | edição pontual; costuma ser arquivo frio |
| Emulador abre com config/controle errado | gerador C# (emulatorlauncher) | fork do emulatorlauncher + nosso `batocera-ports-x64.zip` |
| Bug/lentidão/menu da interface | ES (Batocera) | fork do ES + nosso build Windows |
| Inicialização, splash, foco, tela preta ao abrir | `retrobat.exe` (retrobat-executable) | fork + binário substituto no build |
| Build/instalador | retrobat-build | fork, com executável de **outro nome** (binário sempre conflita) |

## 3. Classificação de cada mudança

Antes de implementar, cada mudança recebe três notas. Elas vão para a linha da mudança em
PATCHES.md e para a descrição do PR.

**Camada** (o quanto toca código do upstream):

| Camada | O que é | Exemplo |
|---|---|---|
| **C0 — aditiva** | só arquivos novos, em caminho nosso ou num ponto de extensão | `es_systems_retroboat.cfg`, script novo, classe C# nova |
| **C1 — cirúrgica** | até ~10 linhas num arquivo do upstream, num bloco só | trocar uma URL do `build.ini`, gancho de uma linha chamando código nosso |
| **C2 — modificação** | lógica alterada em várias regiões ou dezenas de linhas | mudar o comportamento de um gerador de config |
| **C3 — estrutural** | refatorar, mover/renomear, mudar formato, trocar pipeline, rebranding | renomear RetroBat → Retroboat em todo lugar |

**Calor** do arquivo tocado, tirado de HOTSPOTS.md: baixa (até 3 alterações/ano), média (4 a 14),
ALTA (15 ou mais).

**Risco de merge** = camada × calor:

| | calor baixa | calor média | calor ALTA |
|---|---|---|---|
| C0 | nenhum | nenhum | nenhum (mas veja risco semântico) |
| C1 | baixo | baixo | médio |
| C2 | médio | alto | muito alto: só com justificativa forte |
| C3 | alto | muito alto | evitar; discutir antes |

Além do risco textual (o git mostra o conflito), existe o **risco semântico**: o merge passa limpo
mas o resultado está errado. O caso típico é o overlay: se `es_systems_retroboat.cfg` substitui o
bloco `<emulators>` de um sistema e o upstream adiciona um core novo a esse sistema, o nosso overlay
esconde o core novo em silêncio. Por isso todo overlay entra em PATCHES.md com a lista exata do que
sobrescreve, e é revisado a cada sincronização.

**Upstreamável?** Se a correção serve para qualquer usuário do RetroBat, a meta é mandar PR para o
upstream. Patch aceito upstream é patch que some no próximo merge. O fork deve carregar no longo
prazo só o que é realmente nosso.

## 4. Regras para manter a divergência baixa

1. **Overlay antes de edição.** Sistemas vão em `es_systems_retroboat.cfg`, opções de menu em
   `es_features_retroboat.cfg`. O overlay troca a tag inteira (ex.: o `<emulators>` todo), então
   coloque nele só os sistemas e tags que realmente mudam.
2. **Nunca reformatar arquivo do upstream.** Nada de reordenar, reindentar, trocar aspas ou finais
   de linha. O repositório guarda LF e o Windows faz checkout em CRLF (`core.autocrlf=true`, já
   configurado no sistema); um editor que grave o arquivo inteiro com outro final de linha
   transforma o arquivo todo em conflito.
3. **Código nosso em arquivo nosso.** Em C# e C++, a lógica nova vai numa classe ou arquivo novo
   (C0) e o arquivo do upstream recebe só a chamada (C1). Quando o formato aceita comentário, a
   linha alterada leva `RETROBOAT RB-NNN` para ser achada na hora do conflito.
4. **Uma mudança = um branch = um ID.** Branch `rb/NNN-descricao`; cada commit leva o trailer
   `Retroboat-Patch: RB-NNN` (é isso que permite ao `upstream-check.sh` dizer qual patch é afetado).
   Uma mudança isolada também é fácil de reverter quando o upstream corrigir do jeito dele.
5. **Não editar `CHANGELOG.md` nem `README.md` do upstream.** O nosso histórico fica em
   `retroboat/CHANGELOG.md`.
6. **Binários nunca são alterados no lugar.** `RetroBuild.exe`, `InstallerHost.exe`,
   `mountsquashfs.exe` etc. não fazem merge: qualquer atualização upstream é conflito garantido.
   Se precisarmos de outra versão, ela entra com outro nome.

## 5. Fluxo de trabalho

Branches:

- `main` (no `origin`): o produto, que é o upstream mais os nossos patches.
- `upstream/main`: o RetroBat oficial, só leitura (o push para esse remote está desativado).
- `rb/NNN-descricao`: uma mudança, que entra em `main` por PR.
- `sync/upstream-AAAA-MM-DD`: cada sincronização com o upstream, que entra em `main` por PR.

Usamos **merge, não rebase**: `main` é público, e reescrever histórico publicado complica a vida de
quem clonou. O `git rerere` está ligado neste clone, então um conflito resolvido uma vez é
resolvido sozinho nas próximas.

Rotina de sincronização (sugestão: a cada 1 ou 2 semanas, e sempre que sair uma versão do RetroBat;
merges pequenos e frequentes são bem mais baratos que um grande a cada trimestre):

```bash
bash retroboat/tools/upstream-check.sh
```

1. Leia o relatório: commits novos, arquivos onde o fork e o upstream mexeram, simulação do merge.
2. `git switch -c sync/upstream-AAAA-MM-DD main` e depois `git merge upstream/main`.
3. Resolva os conflitos. Para cada patch afetado, confira se o upstream já corrigiu o mesmo problema;
   se sim, reverta o nosso e marque como `absorvido` em PATCHES.md.
4. Revise os overlays (risco semântico) contra o que mudou em `es_systems.cfg` / `es_features.cfg`.
5. Rode o RetroBuild (opção 1) e um teste rápido: o ES abre, um jogo de cada patch afetado abre.
6. PR para `main`.

Para os forks do ES e do emulatorlauncher a rotina é a mesma, com `-C` apontando para o clone:
`bash retroboat/tools/upstream-check.sh -C ../emulatorlauncher -b master`.

## 6. Decisões em aberto

Estas decisões são suas e afetam o fork inteiro:

1. **Canal de atualização.** Hoje, "Atualizar" dentro do ES instala o RetroBat oficial por cima do
   nosso. Opções: (a) aceitar e documentar que a atualização é manual; (b) ter um canal próprio,
   o que exige forkar o `es-update` (emulatorlauncher) e o instalador. A chave de registro
   `HKCU\SOFTWARE\RetroBat` é compartilhada com o RetroBat oficial, então instalar os dois lado a
   lado também conflita.
2. **Build próprio do ES.** Qualquer correção no ES exige montar o build Windows (GitHub Actions no
   nosso fork do ES). É um investimento único, sem risco de merge (arquivo de workflow novo).
3. **Identidade.** Trocar o nome em todo lugar é C3 em todos os repositórios. O recomendado é marca
   só em pontos nossos (nome do instalador, splash, `about.info`), mantendo nomes internos, chaves
   e caminhos como estão.
4. **Nome do branch local.** O clone local está em `master` e o GitHub tem `main`; o fluxo acima
   assume `main`.
