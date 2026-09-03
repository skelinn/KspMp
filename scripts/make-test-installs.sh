#!/usr/bin/env bash
# Creates two independent copies of the KSP install for local multiplayer testing (~6 GB each).
set -euo pipefail
KSP_ROOT="${KSP_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program}"
DEST="${KSP_TEST_DIR:-$HOME/ksp-test}"
[ -d "$KSP_ROOT/GameData" ] || { echo "KSP not found at $KSP_ROOT (set KSP_ROOT)"; exit 1; }
for name in ksp-a ksp-b; do
  echo "Copying $KSP_ROOT -> $DEST/$name ..."
  mkdir -p "$DEST/$name"
  rsync -a --delete --exclude 'saves/' --exclude 'KSP.log' --exclude 'Logs/' --exclude 'GameData/KspMp/' "$KSP_ROOT/" "$DEST/$name/"
done
echo "Done. Deploy the mod with scripts/deploy.sh, then launch with scripts/run-clients.sh"
