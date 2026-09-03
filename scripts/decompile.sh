#!/usr/bin/env bash
# Decompiles Assembly-CSharp.dll into decompiled/ (gitignored) for API research.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
KSP_ROOT="${KSP_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Kerbal Space Program}"
MANAGED="${KSP_MANAGED_DIR:-$KSP_ROOT/KSP.app/Contents/Resources/Data/Managed}"
[ -f "$MANAGED/Assembly-CSharp.dll" ] || { echo "Assembly-CSharp.dll not found in $MANAGED"; exit 1; }
command -v ilspycmd >/dev/null 2>&1 || dotnet tool install --global ilspycmd
OUT="$REPO/decompiled/Assembly-CSharp"
rm -rf "$OUT" && mkdir -p "$OUT"
ilspycmd -p -o "$OUT" --nested-directories -r "$MANAGED" "$MANAGED/Assembly-CSharp.dll"
echo "Decompiled to $OUT"
