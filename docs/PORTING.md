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
| Stash push/pop | Done | no stash list |
| Hunk/line staging | Todo | |
| Themes (system/light/dark) | Done | persisted |
| Settings dialog | Todo | |
| Clone / init | Done | clone dialog, init via folder picker |
| Merge/rebase/cherry-pick/revert/reset | Todo | |
| Conflict resolution | Todo | |
| Blame / file history | Todo | |
| Submodules | Todo | |
| Interactive rebase | Todo | |
| Search/filter commits | Todo | |
| Plugins | Todo | design a cross-platform extension API |
| Shell integration (context menu) | Todo | per-OS: Nautilus/Dolphin, Finder, Explorer |
| Localization | Todo | reuse upstream .xlf translations |
