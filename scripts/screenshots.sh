#!/usr/bin/env bash
# Render the UI headlessly (Skia) against a generated repo and write PNGs for visual review.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" GITCROSS_SCREENSHOTS="${1:-artifacts/screenshots}"
mkdir -p "$GITCROSS_SCREENSHOTS"
dotnet test tests/GitCross.App.UiTests --nologo -v q
echo "screenshots in $GITCROSS_SCREENSHOTS"
