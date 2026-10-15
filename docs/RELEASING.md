# Releasing

1. Make sure `main` is green: `scripts/check.sh`.
2. Bump `<Version>` in `Directory.Build.props` (semantic versioning) and describe the changes in `CHANGELOG.md`.
3. Commit: `git commit -am "chore: release 0.2.0"`.
4. Tag and push: `git tag v0.2.0 && git push origin main v0.2.0`.
5. The **Release** workflow (`.github/workflows/release.yml`) checks that the tag matches the version, runs the Core tests, builds every package and attaches them to a GitHub release together with `SHA256SUMS`.

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
