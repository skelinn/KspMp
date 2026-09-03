#!/usr/bin/env bash
# Copies GameData/KspMp (and 000_Harmony if missing) into KSP installs.
# Usage: scripts/deploy.sh [install-dir ...]
#   default: the two test installs if they exist, otherwise $KSP_ROOT / the Steam install.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${KSP_TEST_DIR:-$HOME/ksp-test}"
HARMONY_DLL="$(ls "$HOME"/.nuget/packages/lib.harmony/2.2.*/lib/net472/0Harmony.dll 2>/dev/null | tail -1 || true)"
if [ $# -eq 0 ]; then
  if [ -d "$DEST/ksp-a" ]; then set -- "$DEST/ksp-a" "$DEST/ksp-b"
  else set -- "${KSP_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program}"; fi
fi
for install in "$@"; do
  [ -d "$install/GameData" ] || { echo "Skipping $install: no GameData folder"; continue; }
  echo "Deploying to $install/GameData/KspMp"
  mkdir -p "$install/GameData/KspMp"
  rsync -a --delete --exclude 'PluginData/' "$REPO/GameData/KspMp/" "$install/GameData/KspMp/"
  if [ ! -d "$install/GameData/000_Harmony" ] && [ -n "$HARMONY_DLL" ]; then
    mkdir -p "$install/GameData/000_Harmony" && cp "$HARMONY_DLL" "$install/GameData/000_Harmony/"
    echo "  added 000_Harmony/0Harmony.dll"
  fi
done
