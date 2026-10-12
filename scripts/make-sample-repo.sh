#!/usr/bin/env bash
# Create a scratch repo with branches, a merge, tags and a dirty working tree for manual testing.
set -euo pipefail
d="${1:?usage: make-sample-repo.sh <dir>}"
rm -rf "$d"; mkdir -p "$d"; cd "$d"
git init -q -b main; git config user.name Sample; git config user.email sample@example.com
echo one > a.txt; git add .; git commit -qm "first"
git checkout -qb feature; echo f > f.txt; git add .; git commit -qm "feature work"
git checkout -q main; echo two >> a.txt; git commit -qam "main work"
git merge -q --no-ff feature -m "merge feature"; git tag v1.0
echo three >> a.txt; echo new > untracked.txt; echo staged > s.txt; git add s.txt
echo "sample repo at $d"
