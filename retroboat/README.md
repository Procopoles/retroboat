# retroboat/

Tudo o que pertence ao fork, separado do código do RetroBat oficial para nunca gerar conflito de merge.

- [ROADMAP.md](ROADMAP.md): etapas e marcos (etapa 1 cloud-first, etapa 2 redesign).
- [FORK.md](FORK.md): estratégia, mapa dos repositórios, como classificar o impacto de cada mudança e a rotina de sincronização.
- [PATCHES.md](PATCHES.md): registro de todas as mudanças do fork em relação ao upstream.
- [HOTSPOTS.md](HOTSPOTS.md): quais arquivos o upstream mais altera, ou seja, onde é caro mexer.
- [CHANGELOG.md](CHANGELOG.md): o que mudou em cada versão do Retroboat.
- [design/](design/): desenho das features maiores antes de implementar ([cloud-first](design/cloud-first.md), [biblioteca e nuvem](design/biblioteca-e-nuvem.md)).
- [app/](app/README.md): código do Retroboat (CLI, lançador, biblioteca na nuvem). C#/.NET Framework 4.8.
- `tools/upstream-check.sh`: relatório antes de cada merge do upstream (só faz fetch, não altera nada).
- `tools/hotspots.sh`: recalcula a tabela de arquivos quentes.

Os scripts rodam no Git Bash, que já vem com o Git for Windows, pré-requisito do build:

```bash
bash retroboat/tools/upstream-check.sh
```
