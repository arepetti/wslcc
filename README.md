# WSLCC — WSL Containers Compose

`wslcc` brings a `docker compose`-style workflow to Microsoft's [WSL containers](https://learn.microsoft.com/en-us/windows/wsl/wsl-container) feature. It fills the gap of a missing `compose` command in `wslc`, and it can also drive the Docker CLI so you have a single, unified interface across both backends — locally or (with optional **HTTPS + bearer token**) on a remote machine.

> Status: WSL containers are generally available in WSL 3.0.1. WSLCC itself is pre-1.0; expect breaking changes and do not treat it as production-ready yet.

## Install

**Prerequisites:** Windows x64 with current WSL (`wsl --update`; WSL 3.0.1 or newer recommended). Optionally Docker, for the `docker` provider.

There is **no tagged release yet** (packaging and winget manifests exist for when one is cut — see [docs/roadmap.md](docs/roadmap.md) milestone 0.2). Until then, build from source with the .NET 10 SDK:

```powershell
dotnet build Wslcc.slnx
.\src\out\wslcc.exe daemon start
```

See [CONTRIBUTING.md](CONTRIBUTING.md). After a release exists: `winget install AdrianoRepetti.WSLCC`, or download the `win-x64` zip from [GitHub Releases](https://github.com/arepetti/wslcc/releases) (self-contained — no SDK required to *run*).

Before removing an installed build, unregister autostart if you used it: `wslcc daemon uninstall` (then `wslcc daemon stop` if a daemon is still running).

## Components

- `wslcc` — the command-line tool. Mirrors `docker compose ...` under a `compose` branch (`wslcc compose up`, `wslcc compose ps`, ...), plus `wslcc daemon ...` and `wslcc version`.
- `wslccd` — a small background daemon exposing a gRPC service (named pipe locally; optional **HTTPS** for remote). Runs as a per-user process, on demand or started automatically at logon.
- A provider-agnostic Compose engine, plus providers for **WSL containers** (`wslc`) and **Docker** (`docker`).

```mermaid
graph LR
  cli["wslcc (CLI)"] -->|gRPC over npipe/http| daemon["wslccd (daemon)"]
  gui["GUI (WinUI3, future)"] -.->|gRPC| daemon
  daemon --> engine["Compose engine"]
  engine --> wslc["Provider: WSL containers"]
  engine --> docker["Provider: Docker"]
```

## Quick start

Bring up the sample stack (options go **after** the leaf command):

```powershell
wslcc daemon start
wslcc compose up --project-directory examples/web-redis -d
wslcc compose ps --project-directory examples/web-redis
wslcc compose down --project-directory examples/web-redis
```

If a command says the daemon is not reachable, see [docs/troubleshooting.md](docs/troubleshooting.md) — an elevated shell often cannot talk to a non-elevated daemon (and vice versa).

`wslcc` talks to `wslccd` over a named pipe by default (`npipe://wslccd`). Use `-H`/`--host` to change transport (the `compose` commands use `--wslcc-host`/`--wslcc-provider` so they don't clash with standard `docker compose` options):

```powershell
wslcc --help                                   # help
wslcc version -H npipe://wslccd                       # local named pipe (default)
wslcc version -H https://remote-host:5211 --token … --tls-ca server.pem
wslcc compose version --wslcc-provider docker         # target a specific provider
```

## Repository layout

- `src/` — C# source (libraries, providers, daemon, CLI).
- `tests/` — unit tests.
- `docs/` — see [docs/README.md](docs/README.md) for the index (architecture, CLI/daemon/compose references, [compatibility](docs/compatibility.md), [troubleshooting](docs/troubleshooting.md), roadmap).
- `examples/` — sample compose projects (start with [examples/web-redis](examples/web-redis), or [examples/mongo-ui](examples/mongo-ui) for a database plus a browser UI).

Coming from `docker compose`? Start with [docs/compatibility.md](docs/compatibility.md). See [docs/architecture.md](docs/architecture.md) for the full picture and [docs/todo.md](docs/todo.md) for what's next (including a planned managed API NuGet package and a WinUI3 GUI). Contributors building from source need the .NET 10 SDK — see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT — see [LICENSE](LICENSE).
