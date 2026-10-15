#!/usr/bin/env bash
# Run the test suites the way a clean CI machine sees them: no global git identity, no system config, and
# git must not invent an identity from the user name / host name (user.useConfigOnly).
# A test that only passes on a developer machine with a configured identity fails here.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
H=$(mktemp -d); trap 'rm -rf "$H"' EXIT
printf '[user]\n\tuseConfigOnly = true\n' > "$H/system.gitconfig"
export HOME="$H" XDG_CONFIG_HOME="$H" GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM="$H/system.gitconfig"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$OLDPWD/.nuget/packages}"
[ -d "$NUGET_PACKAGES" ] || export NUGET_PACKAGES="$(eval echo ~"$USER")/.nuget/packages"
dotnet test tests/GitCross.Core.Tests --nologo -v q
dotnet test tests/GitCross.App.UiTests --nologo -v q
