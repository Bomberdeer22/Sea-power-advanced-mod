#!/usr/bin/env bash
# Build the mod with a portable .NET SDK, so nothing has to be installed.
#
# The first run downloads the official .NET SDK into .dotnet/ inside this repo
# (about 200 MB, once). Nothing is installed system-wide: no admin rights, no PATH
# changes, and deleting .dotnet/ undoes it. The SDK is only a build tool; the mod is
# compiled against the game's own DLLs, so it matches your Sea Power version.
#
# Usage:
#   ./tools/build-portable.sh                # Release build into dist/
#   ./tools/build-portable.sh Debug          # Debug build
#   ./tools/build-portable.sh Release --install   # also install into StreamingAssets
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_dir="$root/.dotnet"
channel="${DOTNET_CHANNEL:-8.0}"

if [ ! -x "$dotnet_dir/dotnet" ]; then
  echo "No portable SDK yet; fetching .NET $channel into .dotnet/ (one time, no install)..."
  mkdir -p "$dotnet_dir"
  installer="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  bash "$installer" --install-dir "$dotnet_dir" --channel "$channel" --no-path
  rm -f "$installer"
fi

export PATH="$dotnet_dir:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

exec "$root/build.sh" "$@"
