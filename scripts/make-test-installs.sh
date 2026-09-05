#!/usr/bin/env bash
# Creates two independent copies of the KSP install for local multiplayer testing.
# Usage: scripts/make-test-installs.sh [--stock]
#   default: mirrors the whole install, mods included (~= the size of your install).
#   --stock: copies only the stock GameData (Squad, SquadExpansion) and leaves every mod
#            behind. Much smaller, loads in seconds, and keeps a Kraken from being someone
#            else's bug. 000_Harmony and KspMp are added afterwards by scripts/deploy.sh.
set -euo pipefail
STOCK=0
[ "${1:-}" = "--stock" ] && STOCK=1
KSP_ROOT="${KSP_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program}"
DEST="${KSP_TEST_DIR:-$HOME/ksp-test}"
[ -d "$KSP_ROOT/GameData" ] || { echo "KSP not found at $KSP_ROOT (set KSP_ROOT)"; exit 1; }
for name in ksp-a ksp-b; do
  echo "Copying $KSP_ROOT -> $DEST/$name ..."
  mkdir -p "$DEST/$name"
  if [ "$STOCK" = 1 ]; then
    # Everything outside GameData first, then the stock GameData folders only.
    rsync -a --delete --exclude 'saves/' --exclude 'KSP.log' --exclude 'Logs/' --exclude 'CKAN/' \
      --exclude 'GameData/' "$KSP_ROOT/" "$DEST/$name/"
    # --delete prunes mods a previous non-stock run left behind; 'protect' keeps the two
    # folders deploy.sh writes, which do not exist in the source install.
    rsync -a --delete --include 'Squad/***' --include 'SquadExpansion/***' \
      --filter 'protect 000_Harmony' --filter 'protect KspMp' \
      --exclude '*' "$KSP_ROOT/GameData/" "$DEST/$name/GameData/"
  else
    rsync -a --delete --exclude 'saves/' --exclude 'KSP.log' --exclude 'Logs/' --exclude 'CKAN/' \
      --exclude 'GameData/KspMp/' "$KSP_ROOT/" "$DEST/$name/"
  fi
done
echo "Done. Deploy the mod with scripts/deploy.sh, then launch with scripts/run-clients.sh"
