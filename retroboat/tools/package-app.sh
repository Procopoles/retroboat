#!/usr/bin/env bash
# Compila o app do Retroboat (Release) e coloca dentro de um build do RetroBat, em <build>\retroboat.
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

echo "Retroboat copiado para $dest:"
ls -1 "$dest"
