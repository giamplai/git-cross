# Contributing to git-cross

Thank you for wanting to help! Bug reports, ideas, documentation and code are all welcome.
By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to contribute

* **Report a bug** — open an issue using the *Bug report* form. Include the app version (Settings, bottom left), your OS,
  your git version, and the exact steps. Error text shown in red in the status bar is git's own output; please paste it.
* **Suggest a feature** — open a *Feature request* and describe the workflow you are trying to achieve before the solution.
* **Fix something** — pick an issue (look for `good first issue`), or open one first for larger changes so we can agree on
  the approach before you spend time on it.
* **Improve the guide** — `docs/HOWTO.md` is the user manual; corrections and clearer wording are always useful.
* **Security problems** — do **not** open a public issue; follow [SECURITY.md](SECURITY.md).

## Development setup

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and `git`.

```bash
git clone https://github.com/<you>/git-cross.git && cd git-cross
git config core.hooksPath scripts/hooks      # enables the pre-commit checks described below
dotnet run --project src/GitCross.App -- .      # run the app on this very repository
scripts/check.sh                              # build with warnings-as-errors + run every test
```

More detail (layout, conventions, how the tests and screenshots work): [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Making a change

1. **Fork** and create a topic branch from `develop` (`feature/short-name` or `fix/short-name`) and open the pull request against `develop`. See *Branching (git-flow)* below.
2. **Write the change with tests.**
   * Anything that parses git output → a pure test in `tests/GitCross.Core.Tests` with sample text.
   * Anything that runs git → an integration test using the `TempRepo` helper (real temporary repositories).
   * Anything the user can see → a UI test in `tests/GitCross.App.UiTests` that renders it (this also makes the screenshot).
3. **Run `scripts/check.sh`** — it must pass. Warnings are errors.
4. **Update the user guide** for visible changes: edit `docs/HOWTO.md`, then run `scripts/update-docs.sh` to regenerate
   the screenshots from the real UI. The pre-commit hook blocks code commits that do not touch `docs/HOWTO.md`
   (set `DOCS_OK=1` for a change with no user-visible effect) and checks that every referenced image exists.
5. **Commit** with a clear [Conventional Commits](https://www.conventionalcommits.org) message — `feat(app): …`, `fix(core): …`,
   `docs: …`, `test: …`, `chore: …` — one logical change per commit.
6. **Open a pull request** and fill in the checklist. Small, focused pull requests are reviewed fastest.

## Code guidelines

* **Core has no UI.** `GitCross.Core` must not reference Avalonia. All git behavior lives there, behind `GitRepository` and
  pure parsers; the app project never starts a process itself.
* **MVVM.** Logic belongs in view-models (`[ObservableProperty]`, `[RelayCommand]`); code-behind is for pickers and dialogs only.
  Bindings are compiled — set `x:DataType`.
* **Never use a shell.** Pass arguments as lists. Use `-z` / NUL or `%x1f` / `%x1e` separators for machine parsing; never
  parse human-readable or localized git output.
* **Cross-platform.** No `System.Windows.*`, no registry, no hard-coded `\`; use `Path.Combine`. Tests that need `sh` must
  return early on Windows.
* **Destructive actions confirm** (discard, hard reset, drop, abort), and error paths must leave the repository in a state
  the user can understand and recover from.
* **Style.** File-scoped namespaces, nullable enabled, `.editorconfig` is authoritative. Package versions live only in
  `Directory.Packages.props`.

## Originality and licensing — please read

This project is released under the [MIT License](LICENSE.md) and is an independent implementation inspired by
[Git Extensions](https://github.com/gitextensions/gitextensions).

* **Your contribution must be your own work** (or clearly yours to license) and is released under the MIT License.
* **Do not copy code, comments, strings, translations, icons or other assets from Git Extensions** or from any project with
  an incompatible license (GPL, LGPL, AGPL, "source available"). Reading it to understand *behavior* is fine; write the code
  yourself. The reasoning, the verification script and the full rules are in [docs/PROVENANCE.md](docs/PROVENANCE.md).
* **New dependencies** must have a permissive license (MIT, BSD, Apache-2.0, OFL). Add them to
  `Directory.Packages.props`, run `python3 scripts/third-party-notices.py` and commit the updated `THIRD-PARTY-NOTICES.md`.
* Keep existing copyright notices intact; contributors are credited through the git history and release notes.

## Review and releases

The maintainer ([@giamplai](https://github.com/giamplai)) reviews pull requests; expect questions rather than silence. Releases are
tagged `vX.Y.Z` and built by the release workflow — see [docs/RELEASING.md](docs/RELEASING.md).

## Branching (git-flow)
| Branch | Purpose | Created from → merged into |
|---|---|---|
| `main` | Released code only; every commit is a release, tagged `vX.Y.Z`. | |
| `develop` | Integration branch for the next release; all pull requests target it. | |
| `feature/*`, `fix/*` | One change each. | `develop` → `develop` |
| `release/X.Y.Z` | Version bump, changelog and last fixes before a release. | `develop` → `main` (tagged) and back into `develop` |
| `hotfix/*` | Urgent fix of a released version (or of CI on `main`). | `main` → `main` and `develop` |

Commits follow Conventional Commits; the version follows semantic versioning (`feat` → minor, `fix` → patch, breaking change → major).
Dependabot pull requests target `develop`. Details of a release are in [docs/RELEASING.md](docs/RELEASING.md).
