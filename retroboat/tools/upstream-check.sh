#!/usr/bin/env bash
# Relatório pré-merge: o que chegou no upstream desde o último merge e onde isso
# vai encostar nas mudanças do fork. Só faz `git fetch`; não altera branch,
# índice nem working tree.
#
# Uso: bash retroboat/tools/upstream-check.sh [-C repo] [-u remote] [-b branch]
#   -C  repositório (padrão: .) — serve também para os forks do ES e do emulatorlauncher
#   -u  remote do upstream (padrão: upstream)
#   -b  branch do upstream (padrão: main)
#
# Os commits do fork devem levar o trailer "Retroboat-Patch: RB-NNN" para que o
# relatório consiga dizer qual patch é afetado por cada arquivo em risco.
set -eu

repo=.
remote=upstream
branch=main

while getopts "C:u:b:" opt; do
	case $opt in
		C) repo=$OPTARG ;;
		u) remote=$OPTARG ;;
		b) branch=$OPTARG ;;
		*) exit 2 ;;
	esac
done

g() { git -C "$repo" "$@"; }

# Lista os IDs RB-NNN dos commits do fork que tocaram um arquivo.
patches_for() {
	g log --no-merges --format='%(trailers:key=Retroboat-Patch,valueonly,separator=%x2C)' "$up..HEAD" -- "$1" \
		| tr ',' '\n' | sed 's/^ *//; /^$/d' | sort -u | paste -sd, -
}

g fetch -q "$remote"
up="$remote/$branch"
base=$(g merge-base HEAD "$up")

incoming=$(g rev-list --count "$base..$up")
ours=$(g rev-list --count --no-merges "$up..HEAD")

echo "== $up"
echo "   último upstream integrado: $(g log -1 --format='%h %ad %s' --date=short "$base")"
echo "   commits novos no upstream: $incoming"
echo "   commits próprios do fork:  $ours"

our_files=$(g diff --name-only "$base" HEAD)

echo
echo "== Superfície do fork (arquivos do upstream que divergem) e calor de cada um nos últimos 12 meses"
if [ -z "$our_files" ]; then
	echo "   nenhuma — o fork é idêntico ao upstream"
else
	# Arquivos que só existem no fork não colidem com o upstream: aparecem resumidos por pasta.
	own_files=""
	upstream_touched=0
	while IFS= read -r f; do
		if g cat-file -e "$base:$f" 2>/dev/null; then
			upstream_touched=1
			n=$(g rev-list --count --no-merges --since="12 months ago" "$up" -- "$f")
			echo "   [$n alterações/ano] $f  (patches: $(patches_for "$f"))"
		else
			own_files="$own_files$f"$'\n'
		fi
	done <<< "$our_files"

	[ "$upstream_touched" -eq 0 ] && echo "   nenhum arquivo do upstream alterado"

	if [ -n "$own_files" ]; then
		printf '%s' "$own_files" | sed 's#/.*##' | sort | uniq -c | while read -r count dir; do
			echo "   + $count arquivos só do fork em $dir/  (patches: $(patches_for "$dir"))"
		done
	fi
fi

if [ "$incoming" -eq 0 ]; then
	echo
	echo "Nada novo no upstream."
	exit 0
fi

echo
echo "== Commits novos no upstream (mais recentes primeiro, até 40)"
g log --no-merges --format='   %h %ad %s' --date=short "$base..$up" | head -n 40

their_files=$(g diff --name-only "$base" "$up")
overlap=$(comm -12 <(printf '%s\n' "$their_files" | sort) <(printf '%s\n' "$our_files" | sort) | sed '/^$/d')

echo
echo "== Arquivos que o fork altera E que mudaram no upstream (revisar mesmo se o merge for limpo)"
if [ -z "$overlap" ]; then
	echo "   nenhum"
else
	while IFS= read -r f; do
		n=$(g rev-list --count "$base..$up" -- "$f")
		echo "   $f  (upstream: $n commit(s); patches: $(patches_for "$f"))"
	done <<< "$overlap"
fi

echo
echo "== Simulação do merge (git merge-tree)"
set +e
out=$(g merge-tree --write-tree --name-only --no-messages HEAD "$up")
rc=$?
set -e
case $rc in
	0) echo "   merge limpo, sem conflitos textuais" ;;
	1)
		echo "   CONFLITOS em:"
		printf '%s\n' "$out" | tail -n +2 | sed '/^$/d; s/^/   /'
		echo "   (o rerere resolve sozinho conflitos que já foram resolvidos antes)"
		;;
	*) echo "   falha ao simular o merge (código $rc)"; exit "$rc" ;;
esac
