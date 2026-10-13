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

Licensed GPL-3.0, like upstream.
