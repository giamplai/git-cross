# Feature map (Git Extensions as the reference → git-cross)

This tracks *which capabilities* of Git Extensions we have matched. Nothing is ported as code: every feature is an original
implementation (see [PROVENANCE.md](PROVENANCE.md)).

| Feature | Status | Notes |
|---|---|---|
| Open repo / recent list | Done | persisted in settings.json, reopens last repo |
| Commit history + graph | Done | lane layout, 2000 commit cap, no paging; ref chips, avatars, relative dates |
| Commit search | Done | subject/author/sha/ref; hides graph while filtering |
| Branches / remotes / tags sidebar, checkout | Done | |
| Create branch, checkout commit | Done | |
| Commit diff viewer | Done | line numbers, intra-line highlights, side-by-side toggle; no syntax highlighting yet |
| Stage / unstage / commit / amend | Done | file-level only |
| Fetch / Pull / Push | Done | no progress output, no credentials UI |
| Stash push/pop/apply/drop + list | Done | sidebar section, context menu; untracked files in a stash are not shown |
| Hunk and line staging (stage/unstage/discard) | Done | line selection in unified view only |
| Themes (system/light/dark) | Done | persisted |
| Settings dialog | Done | git path, identity, pull mode, history size |
| Clone / init | Done | clone dialog, init via folder picker |
| Cherry-pick / revert / reset / tag (commit context menu) | Done | hard reset asks for confirmation; tags are name-only (see "Advanced tag creation") |
| Merge (default, no-ff, squash) | Done | sidebar context menu |
| Rebase onto branch, pull --rebase | Done | |
| Pull keeping work in progress (smart pull) | Done | parks staged/unstaged/untracked changes, pulls, re-applies; banner with Restore / Undo sync / Keep |
| Interactive rebase (pick/reword/squash/fixup/drop, reorder) | Done | edit/exec/merges not supported |
| Conflict resolution | Done | banner, marker view, accept ours/theirs, continue/abort; 3-way merge via external tool |
| External merge/diff tools (Collaterale, Meld, KDiff3, VS Code, custom) | Done | command templates, no shell; folder compare of commits Todo |
| Blame / file history | Done | right-click a file: blame (at commit or work tree), file history with renames; double-click blame line jumps to commit |
| Submodules (list, init/update, open, clone recursive) | Done | add/remove/set-url not in UI |
| Advanced tag creation | Todo | tag creation is too basic (name only, lightweight): add annotated tags (message), signed tags, force, tag a chosen commit/ref with options dialog, delete/push tags |
| Coordinated submodule operations | Done | same branch, align on branch, commit and push everywhere with pre-flight check; not yet: pull everywhere, per-submodule commit messages, selecting a subset, `submodule add` UI |
| Plugins | Todo | design a cross-platform extension API |
| Shell integration (context menu) | Todo | per-OS: Nautilus/Dolphin, Finder, Explorer |
| Localization | Todo | reuse upstream .xlf translations |
| Packaging (deb, tar.gz, zip, macOS .app) + release workflow | Done | unsigned; no MSI/dmg/AppImage yet |
