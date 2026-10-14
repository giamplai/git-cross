# git-cross

A cross-platform (Linux · macOS · Windows) take on [GitExtensions](https://github.com/gitextensions/gitextensions), built with .NET 10 and Avalonia.

## Install
Download a package from the Releases page (Linux `.deb`/`.tar.gz`, Windows `.zip`, macOS `.app` zip) — see the [installation guide](docs/HOWTO.md#1-installing-and-starting-the-app).

## Quick start
```bash
dotnet run --project src/GitCross.App -- /path/to/repo   # needs .NET 10 SDK and git
scripts/check.sh                                       # build + tests
```
**User guide with screenshots: [docs/HOWTO.md](docs/HOWTO.md)**

Docs: [architecture](docs/ARCHITECTURE.md) · [porting map](docs/PORTING.md) · [roadmap](ROADMAP.md) · [contributing](CONTRIBUTING.md)

## Credits and license

Inspired by **[Git Extensions](https://github.com/gitextensions/gitextensions)** — thank you to its authors and contributors.
This is an independent implementation: no Git Extensions code, translations or artwork are included, and the project is not
affiliated with them ([how this is verified](docs/PROVENANCE.md)).

Licensed under the **GNU GPL v3** ([LICENSE.md](LICENSE.md)). Third-party components and their licenses:
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Overview: [NOTICE.md](NOTICE.md).
