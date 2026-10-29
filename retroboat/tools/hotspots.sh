#!/usr/bin/env bash
# Mapa de "arquivos quentes": quantas vezes cada arquivo mudou no upstream num
# período. Quanto mais quente o arquivo, maior a chance de uma edição nossa
# nele colidir com o próximo merge.
#
# Uso: bash retroboat/tools/hotspots.sh [-C repo] [-r ref] [-s desde] [-n top] [-x regex-para-excluir]
#   -C  repositório a analisar (padrão: .)
#   -r  ref do upstream (padrão: upstream/main)
#   -s  janela de tempo (padrão: "12 months ago")
#   -n  quantos arquivos listar (padrão: 40)
#   -x  regex de caminhos a ignorar (ex.: '^locale/')
#
# Saída: tabela Markdown, pronta para colar em retroboat/HOTSPOTS.md.
set -eu

repo=.
ref=upstream/main
since="12 months ago"
top=40
exclude='^$'

while getopts "C:r:s:n:x:" opt; do
	case $opt in
		C) repo=$OPTARG ;;
		r) ref=$OPTARG ;;
		s) since=$OPTARG ;;
		n) top=$OPTARG ;;
		x) exclude=$OPTARG ;;
		*) exit 2 ;;
	esac
done

total=$(git -C "$repo" rev-list --count --no-merges --since="$since" "$ref")
if [ "$total" -eq 0 ]; then
	echo "Nenhum commit em $ref desde \"$since\"." >&2
	exit 1
fi

echo "_$total commits (sem merges) em \`$ref\` desde \"$since\". Calor: ALTA >= 15 alterações, média 4-14, baixa <= 3._"
echo
echo "| Alterações | % dos commits | Calor | Arquivo |"
echo "|---:|---:|---|---|"
git -C "$repo" log "$ref" --no-merges --since="$since" --name-only --format= \
	| grep -v '^$' | grep -Ev "$exclude" | sort | uniq -c | sort -rn | head -n "$top" \
	| sed -E 's/^ *([0-9]+) /\1\t/' \
	| awk -F'\t' -v t="$total" '{
		heat = $1 >= 15 ? "ALTA" : ($1 >= 4 ? "média" : "baixa")
		printf "| %d | %.0f%% | %s | `%s` |\n", $1, 100 * $1 / t, heat, $2
	}'
