# Contributing
1. `scripts/check.sh` must pass (warnings are errors).
2. Core changes need tests (pure parser tests and/or `TempRepo` integration tests).
3. UI logic goes into ViewModels, not code-behind.
4. Conventional, small commits. Reading Git Extensions for behavior is fine; copying its code, translations or icons is not — see [docs/PROVENANCE.md](docs/PROVENANCE.md) for the rules and the overlap check. New dependency? Check its license and regenerate `THIRD-PARTY-NOTICES.md`.
