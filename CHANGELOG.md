# Changelog

## Unreleased

## 0.2.0 — 2026-10-20
* Pull with work in progress: uncommitted changes (staged, unstaged, untracked) are parked, the pull runs, and they are re-applied; banner with **Restore my changes / Undo sync / Keep**. `Pull (merge)` now works on diverged branches without `pull.rebase` configured.
* Coordinated submodule operations (SUBMODULES header menu): create the same branch everywhere, check the branch out in every submodule, commit all, push all, pull all (keeping work in progress, one global undo). Each runs a pre-flight check and changes nothing if any repository would fail. The sidebar shows branch, ahead/behind and dirty state per submodule.
* Tags: annotated, signed, force, any target commit or reference, push after creating; push, delete and delete-on-remote from the tag context menu; tooltip shows annotation.
* Provenance audit fixed: it skipped the upstream checkout it was meant to scan (now compares 1859 upstream files; no identical lines). Dependencies and GitHub Actions updated.
* Project: git-flow branching (`develop`), CI fix for setup-dotnet 6 (full SDK version in `global.json`).
* Legal and provenance: MIT license (c) Giampiero Lai, credits to Git Extensions (README, NOTICE, Settings), THIRD-PARTY-NOTICES with verbatim OFL/BSD texts, notices shipped in every package, repeatable upstream-overlap audit.
* External merge and diff tools: presets for Collaterale (formerly Collate), Meld, KDiff3 and VS Code or a custom command; resolve conflicts and open diffs from the file context menus.

## 0.1.0 — first release
* History graph with branch/tag chips, search, blame and per-file history.
* Staging by file, hunk and individual line; commit and amend.
* Side-by-side and unified diffs with intra-line highlighting.
* Branches, tags, stashes, merge (default / no-ff / squash), rebase and interactive rebase, cherry-pick, revert, reset.
* Conflict resolution banner with ours/theirs and continue/abort.
* Submodules: list, initialize/update, open, recursive clone.
* Clone / init dialogs, settings (git path, identity, pull mode, history size), light/dark themes.
* Packages: Linux (.tar.gz, .deb), Windows (.zip), macOS (.app zip).
