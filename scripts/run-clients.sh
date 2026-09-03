#!/usr/bin/env bash
# Launches the two test installs directly (bypasses Steam and the launcher). Env: KSP_TEST_DIR (~/ksp-test)
set -euo pipefail
DEST="${KSP_TEST_DIR:-$HOME/ksp-test}"
for name in "${@:-ksp-a ksp-b}"; do :; done
NAMES=("$@"); [ ${#NAMES[@]} -eq 0 ] && NAMES=(ksp-a ksp-b)
for name in "${NAMES[@]}"; do
  app="$DEST/$name/KSP.app"
  [ -d "$app" ] || { echo "Missing $app (run scripts/make-test-installs.sh)"; exit 1; }
  echo "Launching $app"
  open -n -a "$app" --args -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$DEST/$name/player.log"
done
