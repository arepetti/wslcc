# Contributing to WSLCC

Thanks for your interest! WSLCC is a single-maintainer, spare-time project (see [GOVERNANCE.md](GOVERNANCE.md)). Contributions are welcome; please keep changes focused and discuss larger ideas in an issue first.

## Prerequisites

- Windows 10/11 with the WSL **pre-release** for WSL containers: `wsl --update --pre-release`.
- [.NET 10 SDK](https://dotnet.microsoft.com/) (see [`global.json`](global.json) for the pinned version).
- Optional: Docker, for the `docker` provider and for testing without WSL containers.

## Build and test

```powershell
dotnet build Wslcc.slnx
dotnet test Wslcc.slnx
```

App binaries land side-by-side in `src\out\` (see [`Directory.Build.props`](Directory.Build.props)). Invoke them from there (or add that folder to your `PATH` for the session):

```powershell
.\src\out\wslcc.exe daemon start
.\src\out\wslcc.exe version
.\src\out\wslcc.exe daemon stop
```

## Project conventions

- Central Package Management: add/adjust NuGet versions in [`Directory.Packages.props`](Directory.Packages.props); reference packages without a `Version`.
- Shared build settings live in [`Directory.Build.props`](Directory.Build.props) (including `Nullable` enable). Style preferences are suggested via [`.editorconfig`](.editorconfig) (file-scoped namespaces, 4-space indent, etc.) at suggestion severity — they are **not** enforced in CI today (`TreatWarningsAsErrors` is off; there is no `dotnet format --verify` step).
- Every project targets `net10.0` (no `netstandard2.0` compatibility is maintained).
- The WSL SDK path is isolated behind the `WSLC_SDK` compile constant so the solution builds without the preview `Microsoft.WSL.Containers` package. Prefer that seam over sprinkling `#if` throughout.
- Comments should explain intent/trade-offs, not restate the code.

## Planning and issues

Planning artifacts:

| Artifact | Role |
| --- | --- |
| [docs/roadmap.md](docs/roadmap.md) | Milestones, exit criteria, sequencing |
| [docs/todo.md](docs/todo.md) | Backlog detail (priority / size / target milestone) |
| [CHANGELOG.md](CHANGELOG.md) | User-facing changes under `[Unreleased]` until a tag exists |

**GitHub issues** are the work tracker. Before a large change, open an issue (or attach to an existing one), put `#N` in the matching [docs/todo.md](docs/todo.md) **Issue** cell, and reference the roadmap milestone. Suggested conventions (lightweight; the maintainer may adjust):

- Labels: `bug`, `enhancement`, and optionally `milestone-0.2` / `milestone-1.0` / `security` when relevant.
- GitHub **Milestones** named `0.2` and `1.0` when enough issues exist to bother; until then, the markdown roadmap is authoritative.
- **P0** backlog rows (especially security) should have an open issue before a milestone is cut — a todo bullet alone is not the commitment.
- PRs: fill “Related issues” (`Closes #…` when applicable).

**Releases** (when cutting one): tag `vMAJOR.MINOR.PATCH` (triggers [`.github/workflows/release.yml`](.github/workflows/release.yml)); move `[Unreleased]` notes into a dated section in `CHANGELOG.md`; optionally run [`scripts/publish.ps1`](scripts/publish.ps1) / winget submission. There is **no** tagged release yet.

## Pull requests

1. Fork and create a topic branch.
2. Keep PRs small and focused; include tests where it makes sense.
3. Update [CHANGELOG.md](CHANGELOG.md) under `Unreleased` (add that section at the top if it is missing).
4. Make sure `dotnet build` and `dotnet test` pass.

## Architecture

See [docs/README.md](docs/README.md) for the documentation index, then [docs/architecture.md](docs/architecture.md) and [docs/todo.md](docs/todo.md) before starting a significant change.
