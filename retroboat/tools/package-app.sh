#!/usr/bin/env bash
# Compila o app do Retroboat (Release) e inclui o tema PlayStation-X no build do RetroBat.
# Rode depois do RetroBuild (opção 1) e antes das opções 2/3 (arquivo .zip e instalador).
#
# Uso: bash retroboat/tools/package-app.sh [pasta do build]   (padrão: build/ na raiz do repositório)
set -eu

root=$(cd "$(dirname "$0")/../.." && pwd)
build=${1:-$root/build}
app="$root/retroboat/app"

if [ ! -d "$build/emulationstation" ]; then
	echo "Build não encontrado em $build. Rode o RetroBuild (opção 1) antes." >&2
	exit 1
fi

dotnet build "$app/Retroboat.sln" -c Release -nologo -v q

# O lançador acha a raiz do RetroBat pela pasta-mãe, então tudo do Retroboat fica numa pasta só.
dest="$build/retroboat"
mkdir -p "$dest"
cp "$app/src/Retroboat.Launcher/bin/Release/"{Retroboat.exe,Retroboat.exe.config,Retroboat.Core.dll} "$dest/"
cp "$app/src/Retroboat.Cli/bin/Release/"{retroboat-cli.exe,retroboat-cli.exe.config} "$dest/"
cp "$app/src/Retroboat.Launch/bin/Release/"{retroboat-launch.exe,retroboat-launch.exe.config} "$dest/"

# Use a revisão do tema em que o status do Retroboat foi validado.
themes="$build/emulationstation/.emulationstation/themes"
playstation_theme="$themes/es-theme-PlayStation-X"
if [ ! -f "$playstation_theme/theme.xml" ]; then
	if [ -e "$playstation_theme" ]; then
		echo "A pasta do PlayStation-X existe sem theme.xml: $playstation_theme" >&2
		exit 1
	fi

	mkdir -p "$themes"
	(
		cd "$themes"
		staging=$(mktemp -d "$(pwd -P)/.playstation-x.XXXXXX")
		trap 'rm -rf -- "$staging"' EXIT
		revision=1e4650e78811f9ce76d18303b0bae772e319173e
		curl --fail --location --retry 2 --output "$staging/theme.tar.gz" \
			"https://codeload.github.com/pajarorrojo/es-theme-PlayStation-X/tar.gz/$revision"
		mkdir "$staging/es-theme-PlayStation-X"
		tar -xzf "$staging/theme.tar.gz" --strip-components=1 -C "$staging/es-theme-PlayStation-X"
		test -f "$staging/es-theme-PlayStation-X/theme.xml"
		mv -T -- "$staging/es-theme-PlayStation-X" es-theme-PlayStation-X
	)
fi

echo "Retroboat copiado para $dest:"
ls -1 "$dest"
