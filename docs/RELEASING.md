# Releasing

Releases follow git-flow (see CONTRIBUTING.md): `develop` is integrated continuously, `main` only receives releases.

1. Make sure `develop` is green: `scripts/check.sh` and CI.
2. Cut the release branch: `git checkout -b release/0.2.0 develop`.
3. On it, bump `<Version>` in `Directory.Build.props` (semantic versioning: `feat` → minor, `fix` → patch, breaking → major) and
   turn **Unreleased** in `CHANGELOG.md` into the new version with its date. Only release fixes go on this branch.
4. Commit `chore: release 0.2.0` and open a pull request `release/0.2.0` → `main`; merge it with a **merge commit** (no squash, so the history stays connected).
5. Tag the merge commit on `main` and push the tag: `git tag v0.2.0 origin/main && git push origin v0.2.0`.
6. The **Release** workflow (`.github/workflows/release.yml`) checks that the tag matches the version, runs the Core tests, builds every package and attaches them to a GitHub release together with `SHA256SUMS`.
7. Merge `main` back into `develop` (pull request `main` → `develop`) so the version bump and changelog are not lost.

Urgent fix of a released version: `hotfix/0.2.1` from `main`, bump the patch version and changelog, pull request into `main`, tag, then merge back into `develop`.

## Building packages locally
```bash
scripts/package.sh                 # everything → artifacts/packages
scripts/package.sh linux-x64       # one target
```
| Target | Output |
|---|---|
| linux-x64, linux-arm64 | `.tar.gz` (with `install.sh`, desktop entry, icon) and `.deb` |
| win-x64 | `.zip` containing the single-file `git-cross.exe` |
| osx-arm64, osx-x64 | `git-cross.app` in a `.zip` |

Everything can be cross-built from Linux. Only the Linux x64 package is smoke-tested automatically by the maintainers' workflow; run the others on their OS before announcing a release.

## Known gaps
* macOS and Windows builds are **not signed or notarized**: users see Gatekeeper / SmartScreen warnings (see the HowTo, "Installing").
* No MSI/MSIX, dmg or AppImage yet; those need signing infrastructure or platform tooling.
* Regenerate the icon with `python3 scripts/make-icon.py` (needs Pillow).
