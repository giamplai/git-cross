# Architecture

```
GitCross.App (Avalonia, MVVM)  ──►  GitCross.Core (no UI)  ──►  `git` executable
 Views / ViewModels / Controls     GitRepository · Parsers · GraphLayout · GitRunner
```
- **Why drive the git CLI** (like upstream GitExtensions): identical behavior to the user's git, hooks/credential helpers/ssh config just work, easy to test with real repos.
- **Why Avalonia**: one XAML codebase rendering natively on Windows, Linux, macOS; MVVM + compiled bindings.
- **Graph**: `GraphLayout.Build` turns a `--topo-order` commit list into lane rows; `GraphCell` draws each row.
- **Errors**: `GitException` carries stderr; ViewModels surface it in the status bar.
- **Why not reuse upstream assemblies**: `GitExtensions.Extensibility` exposes WinForms types (`IWin32Window`, `Font`, `Form`) so it cannot target plain `net10.0`; `GitCommands` is portable in spirit and is used as a porting reference.
