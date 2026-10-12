#!/usr/bin/env bash
# Clone (or update) the upstream GitExtensions sources as a read-only reference.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -d external/gitextensions/.git ]; then git -C external/gitextensions pull --ff-only
else git clone --depth 50 https://github.com/gitextensions/gitextensions.git external/gitextensions; fi
