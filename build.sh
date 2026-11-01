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
  # Fall back to the SeaPowerDir already configured for the build.
  if [ -z "$sp" ]; then
    for props in "$root/Directory.Build.local.props" "$root/Directory.Build.props"; do
      [ -f "$props" ] || continue
      sp="$(sed -n 's:.*<SeaPowerDir>\([^<]*\)</SeaPowerDir>.*:\1:p' "$props" | head -n 1)"
      [ -n "$sp" ] && break
    done
  fi
  if [ -z "$sp" ]; then
    echo "Set SeaPowerDir in Directory.Build.local.props (or SEAPOWER_DIR) to use --install." >&2
    exit 2
  fi
  if [ ! -d "$sp/Sea Power_Data/Managed" ]; then
    echo "SeaPowerDir does not look like a Sea Power folder: $sp" >&2
    exit 2
  fi
  target="$sp/Sea Power_Data/StreamingAssets/SeaPowerAdvanced"
  mkdir -p "$target"
  cp "$dist/SeaPowerAdvanced.dll" "$dist/_info.ini" "$target/"
  echo "Installed to: $target"
fi
