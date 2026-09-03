#!/usr/bin/env bash
# Runs the dedicated server. Env: KSPMP_PORT (7777), KSPMP_UNIVERSE (./universe)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
exec dotnet run --project "$REPO/src/KspMp.Server.Host" -- --port "${KSPMP_PORT:-7777}" --universe "${KSPMP_UNIVERSE:-$REPO/universe}" "$@"
