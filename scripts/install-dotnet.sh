#!/usr/bin/env bash
# Installs the .NET 10 SDK for the current user (no admin rights needed).
set -euo pipefail
INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
TMP="$(mktemp -d)"
curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMP/dotnet-install.sh"
bash "$TMP/dotnet-install.sh" --channel 10.0 --install-dir "$INSTALL_DIR"
echo
echo "Add this to your shell profile (~/.zshrc):"
echo "  export DOTNET_ROOT=\"$INSTALL_DIR\""
echo "  export PATH=\"\$PATH:$INSTALL_DIR:$INSTALL_DIR/tools\""
