#!/usr/bin/env bash
# Build the mod and lay out a ready-to-install folder in dist/SeaPowerAdvanced/.
# Usage: ./build.sh [Configuration] [--install]
#   ./build.sh                 Release build into dist/
#   ./build.sh Debug           Debug build into dist/
#   ./build.sh Release --install   also copy into the game's StreamingAssets folder
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
configuration="Release"
install=false

for arg in "$@"; do
  case "$arg" in
    --install) install=true ;;
    Debug|Release) configuration="$arg" ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

project="$root/src/SeaPowerAdvanced/SeaPowerAdvanced.csproj"
dotnet build "$project" -c "$configuration"

dist="$root/dist/SeaPowerAdvanced"
mkdir -p "$dist"
cp "$root/src/SeaPowerAdvanced/bin/$configuration/SeaPowerAdvanced.dll" "$dist/"
cp "$root/mod/_info.ini" "$dist/"
echo "Package ready: $dist"

if $install; then
  sp="${SEAPOWER_DIR:-}"
  if [ -z "$sp" ]; then
    echo "Set SEAPOWER_DIR to the Sea Power folder to use --install." >&2
    exit 2
  fi
  target="$sp/Sea Power_Data/StreamingAssets/SeaPowerAdvanced"
  mkdir -p "$target"
  cp "$dist/SeaPowerAdvanced.dll" "$dist/_info.ini" "$target/"
  echo "Installed to: $target"
fi
