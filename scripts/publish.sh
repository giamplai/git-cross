#!/usr/bin/env bash
# Self-contained single-file builds for the three desktop OSes.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
for rid in linux-x64 osx-arm64 win-x64; do
  dotnet publish src/GitCross.App -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -o "artifacts/publish/$rid" --nologo -v q
done
ls -lh artifacts/publish/*/GitCross.App* 2>/dev/null
