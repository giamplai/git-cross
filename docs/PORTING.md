# Porting map (upstream GitExtensions → git-cross)

| Feature | Status | Notes |
|---|---|---|
| Open repo / recent list | Done | persisted in settings.json, reopens last repo |
| Commit history + graph | Done | lane layout, 2000 commit cap, no paging; ref chips, avatars, relative dates |
| Commit search | Done | subject/author/sha/ref; hides graph while filtering |
| Branches / remotes / tags sidebar, checkout | Done | |
| Create branch, checkout commit | Done | |
| Commit diff viewer | Done | line numbers, colored; no side-by-side / syntax highlight yet |
| Stage / unstage / commit / amend | Done | file-level only |
| Fetch / Pull / Push | Done | no progress output, no credentials UI |
| Stash push/pop/apply/drop + list | Done | sidebar section, context menu; untracked files in a stash are not shown |
| Hunk staging (stage/unstage/discard hunk) | Done | line-level selection still Todo |
| Themes (system/light/dark) | Done | persisted |
| Settings dialog | Todo | |
| Clone / init | Done | clone dialog, init via folder picker |
| Cherry-pick / revert / reset / tag (commit context menu) | Done | hard reset asks for confirmation |
| Merge (from sidebar) | Done | no merge dialog/options |
| Rebase | Todo | |
| Conflict resolution | Todo | |
| Blame / file history | Done | right-click a file: blame (at commit or work tree), file history with renames; double-click blame line jumps to commit |
| Submodules | Todo | |
| Interactive rebase | Todo | |
| Search/filter commits | Todo | |
| Plugins | Todo | design a cross-platform extension API |
| Shell integration (context menu) | Todo | per-OS: Nautilus/Dolphin, Finder, Explorer |
| Localization | Todo | reuse upstream .xlf translations |
