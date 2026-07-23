# Contributing to WSLCC

Thanks for your interest! WSLCC is a single-maintainer, spare-time project (see
[GOVERNANCE.md](GOVERNANCE.md)). Contributions are welcome; please keep changes focused and discuss
larger ideas in an issue first.

## Prerequisites

- Windows 10/11 with the WSL **pre-release** for WSL containers: `wsl --update --pre-release`.
- [.NET 10 SDK](https://dotnet.microsoft.com/) (see [`global.json`](global.json) for the pinned version).
- Optional: Docker, for the `docker` provider and for testing without WSL containers.

## Build and test

```powershell
dotnet build Wslcc.slnx
dotnet test Wslcc.slnx
```

Run the CLI against a local daemon:

```powershell
wslcc daemon start
wslcc version
wslcc daemon stop
```

## Project conventions

- Central Package Management: add/adjust NuGet versions in
  [`Directory.Packages.props`](Directory.Packages.props); reference packages without a `Version`.
- Shared build settings live in [`Directory.Build.props`](Directory.Build.props). Style is enforced
  via [`.editorconfig`](.editorconfig) (nullable enabled, file-scoped namespaces, 4-space indent).
- Every project targets `net10.0` (no `netstandard2.0` compatibility is maintained).
- The WSL SDK path is isolated behind the `WSLC_SDK` compile constant so the solution builds without
  the preview `Microsoft.WSL.Containers` package. Prefer that seam over sprinkling `#if` throughout.
- Comments should explain intent/trade-offs, not restate the code.

## Pull requests

1. Fork and create a topic branch.
2. Keep PRs small and focused; include tests where it makes sense.
3. Update [CHANGELOG.md](CHANGELOG.md) under `Unreleased`.
4. Make sure `dotnet build` and `dotnet test` pass.

## Architecture

See [docs/architecture.md](docs/architecture.md) and [docs/todo.md](docs/todo.md) before starting a
significant change.
