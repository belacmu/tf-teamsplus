#!/bin/bash
# Build the mod and deploy it into the FortRise Mods folder.
#   ./build.sh              debug build + deploy
#   ./build.sh Release      release build + deploy + release/TeamPlus.zip
set -e
cd "$(dirname "$0")"
CONFIG="${1:-Debug}"
dotnet build -c "$CONFIG" -v minimal
