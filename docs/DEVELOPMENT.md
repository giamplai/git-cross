# Development guide

## Layout

| Path | Contents |
|---|---|
| `src/GitCross.Core` | UI-free library: `Git/` (runner, repository operations, parsers), `Diff/`, `Graph/`, `Conflicts/`, `Rebase/`, `Submodules/`, `Blame/`, `Tools/` (external tools), `Settings/`, `Models/`. **No Avalonia references.** |
| `src/GitCross.App` | Avalonia application: `ViewModels/`, `Views/` (windows and dialogs), `Controls/` (custom controls, converters), `Assets/` (icons). |
| `tests/GitCross.Core.Tests` | xUnit. Pure parser tests plus integration tests on real temporary repositories (`TempRepo`). |
| `tests/GitCross.App.UiTests` | Headless Avalonia (Skia) tests that render the real windows; they also write the documentation screenshots. |
| `docs/` | `HOWTO.md` (user guide), `ARCHITECTURE.md`, `PORTING.md` (feature map), `RELEASING.md`, `PROVENANCE.md`, `images/` (generated). |
| `scripts/` | `check.sh`, `package.sh`, `update-docs.sh`, `third-party-notices.py`, `audit-upstream-overlap.py`, `make-icon.py`, `hooks/`. |
| `packaging/` | Linux desktop entry, macOS `Info.plist`. |

## Everyday commands

```bash
dotnet build                                   # warnings are errors
dotnet test tests/GitCross.Core.Tests            # fast (a second)
dotnet test tests/GitCross.App.UiTests           # headless UI tests (about a minute)
scripts/check.sh                               # everything above
scripts/update-docs.sh                         # regenerate docs/images from the real UI
scripts/package.sh [rid…]                      # release packages (linux-x64, linux-arm64, win-x64, osx-arm64, osx-x64)
python3 scripts/third-party-notices.py         # after changing dependencies
python3 scripts/audit-upstream-overlap.py PATH # provenance check against a Git Extensions checkout
```

The SDK is selected by `global.json`. The pre-commit hook (enable with `git config core.hooksPath scripts/hooks`) checks that
code changes come with a `docs/HOWTO.md` update, that the solution builds, and that every image the guide references exists.

## Adding a git operation

1. Write the failing test first (`tests/GitCross.Core.Tests`): a `Parsers` test with captured output and/or a `TempRepo` test.
2. Add a thin method to `GitRepository`; parse structured output in `Parsers` (pure functions).
3. Surface it in the app through a view-model command (use `Mutate(...)` so errors go to the status bar and the view refreshes),
   then bind it in the view.
4. Update `docs/HOWTO.md`; add a `Shot(...)` in the UI tests if there is a new screen; run `scripts/update-docs.sh`.

## Testing notes

* Tests that start `sh` or rely on Unix permissions return early on Windows.
* Submodule tests allow the `file://` transport only inside the test process (`protocol.file.allow`), never globally.
* Screenshot tests use a fake global git config and fake repositories — never read the developer's real `~/.gitconfig`.
* The integration tests for an external merge tool run against a real installation of Collaterale when it is on `PATH`
  (or `COLLATERALE_BIN` points at it) and are no-ops otherwise.

## Releasing

See [RELEASING.md](RELEASING.md): bump `<Version>` in `Directory.Build.props`, update `CHANGELOG.md`, tag `vX.Y.Z`.
