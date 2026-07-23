# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial project scaffold: solution, libraries, providers, gRPC daemon, and CLI.
- Provider-agnostic core with a tolerant Compose file parser.
- Providers for WSL containers (`wslc`, with a gated managed-SDK path and a `wslc.exe` fallback) and Docker Compose (`docker`).
- `wslccd` daemon exposing a gRPC service over a named pipe (optional HTTP), runnable as a per-user process or a Windows Service.
- `wslcc` CLI: `compose` branch mirroring `docker compose`, `daemon start|stop|status`, top-level `version`, a `-v|--version` flag, and global `--host`/`--provider` options.
- End-to-end vertical slice: `wslcc daemon start` → `wslcc version` → `wslcc daemon stop`.
- Compose lifecycle: `compose up` (detached), `compose down`, and `compose ps`, implemented via container-level orchestration in the engine so the same path works for `docker` and `wslc`.
  - Dependency-ordered start (`depends_on`), per-service naming (`<project>-<service>`) and labelling (`wslcc.project`/`wslcc.service`), project-name resolution, and `-f`/`-p` file/project options.
  - Shared `Wslcc.Providers.Common` base for the standard container CLIs, plus new `Up`/`Down`/`Ps` gRPC methods and a client for them.
- Compose lifecycle: `compose start`, `compose stop`, and `compose restart`, operating on the project's existing containers (optionally scoped to specific `[SERVICES]`).
  - New `IContainerProvider.StartContainerAsync`/`RestartContainerAsync` members (shared by `docker` and `wslc` via `Wslcc.Providers.Common`), and new `Start`/`Stop`/`Restart` gRPC methods and client wrappers.
- `compose pull`: force-pulls the image for each service defined in the compose file (optionally scoped to specific `[SERVICES]`), reusing the provider's existing `EnsureImageAsync`. New `Pull` gRPC method and client wrapper.
- `compose build`: builds the image for each service with a `build:` section (optionally scoped to specific `[SERVICES]`), tagging it `<project>-<service>` (or the service's `image:` when set).
  - New `IContainerProvider.BuildImageAsync` member and `ImageBuildSpec` model (shared by `docker` and `wslc`), a new `Build` gRPC method/client wrapper carrying a `base_directory` so relative `build.context` paths resolve against the compose file's directory rather than the daemon's working directory.
- `compose logs`: streams (optionally follows with `--follow`, and limits with `--tail`) log output from the project's containers, merged and tagged by service name (optionally scoped to specific `[SERVICES]`).
  - First server-streaming gRPC method (`Logs`); new `IContainerProvider.GetLogsAsync` and `ProcessRunner.StreamLinesAsync` stream a container's/process's output line-by-line and kill the process promptly on cancellation, so `--follow` stops cleanly on Ctrl+C or client disconnect.
- `daemon install`/`daemon uninstall`: register/remove `wslccd` as a Windows Service (`sc.exe create`/`delete`, service name `WSLCC Daemon`), so it can start at boot without a signed-in user. `install` supports `--startup auto|manual|disabled` and `--start`, warns if a conflicting per-user daemon is already running, and accepts `--provider` to persist a default provider for the service. Both require an elevated (Administrator) prompt. New `ServiceControlCommandBuilder` (CLI-only) builds the `sc.exe` argument strings, and `Wslcc.Abstractions.WslccdConstants` shares the service name between the daemon's own `UseWindowsService` registration and these commands.
- Compose file resolution (client-side): new `Wslcc.Compose` library (`ComposeLoader`) that the CLI runs before contacting the daemon to produce a single fully-resolved document, adding:
  - **Multi-file merge**: `-f` is now repeatable (and `COMPOSE_FILE` is honored, split on `COMPOSE_PATH_SEPARATOR`); later files override earlier ones using the Compose per-attribute merge rules — mappings merge by key (including `environment`/`labels` written as `KEY=VALUE` lists), most sequences are appended (exact duplicates dropped), `command`/`entrypoint` are replaced, and scalars are replaced.
  - **`.env` + variable interpolation**: `${VAR}`, `$VAR`, `${VAR:-default}`/`${VAR-default}`, `${VAR:?err}`/`${VAR?err}`, `${VAR:+alt}`/`${VAR+alt}`, and `$$`; values from the process environment overlaid on `.env` (process environment wins). New `--env-file` option; unset-without-default warns.
  - **`extends`**: same-file and cross-file (`{ file, service }`), resolved relative to the declaring file, chains supported, cycles rejected, and extending a service that declares a service-referencing attribute (`depends_on`, `volumes_from`, `links`, or `network_mode`/`ipc`/`pid`/`uts` set to `service:`/`container:`) is rejected as an error.
  - **`profiles`**: new repeatable `--profile` option, `COMPOSE_PROFILES`, and auto-activation by naming a service that carries the profile on the command line; services gated by an inactive profile are dropped and dangling `depends_on` references pruned.
  - **`--project-directory`**: sets the default `.env` location, the relative `build.context` base, and the default project name (defaults to the first compose file's directory).
  - The daemon still parses the resolved YAML over the unchanged `compose_yaml` field (with the parser that now also lives in `Wslcc.Compose`), so it never needs the client's files or environment. New `Wslcc.Compose.Tests` project covers these features.
- `compose config`: renders the fully-resolved Compose document produced by the client resolver — the exact YAML the other verbs send to the daemon, with the effective project name added as a leading `name:` — and runs **entirely client-side** (no daemon required). Options:
  - `--format yaml|json` (default `yaml`) picks the output format for the full document.
  - `--services`/`--volumes`/`--images` print just those names; `--profiles` lists every declared profile (collected before profile filtering, so profile-gated services still contribute).
  - `--hash "*"|<svc,...>` prints a per-service config hash (a wslcc-specific SHA-256 of the canonical service config, for change detection).
  - `--no-interpolate` leaves `${VAR}` references verbatim (files are still merged, `extends` resolved, profiles filtered).
  - `-q|--quiet` validates only; `-o|--output <path>` writes to a file.
  - The document goes to `stdout` and resolver warnings to `stderr`, so a redirected/piped document stays clean. `--resolve-image-digests` is intentionally not implemented (it needs registry access, which the offline client-side `config` does not have). This was the last stubbed `compose` verb; the placeholder stub command has been removed.
- Global `--no-color` option (on every command) plus explicit `NO_COLOR` environment-variable handling: both switch the console to a color-less profile via a command interceptor. This also covers the colored per-service prefix of `compose logs` (the equivalent of `docker compose logs --no-color`). Spectre.Console additionally auto-disables color for non-terminal (piped/redirected) output.

### Changed
- Consolidated Compose parsing into a single library: the tolerant `ComposeFileParser` moved from `Wslcc.Core` to `Wslcc.Compose` (now referenced by `Wslcc.Grpc.Server`), so client resolution and daemon parsing share one YAML parser. `Wslcc.Core` no longer depends on `YamlDotNet`.
- Moved project-name resolution (`ProjectNames`) from `Wslcc.Core` to `Wslcc.Compose` so the CLI (for `compose config`'s synthesized `name:`) and the daemon share one implementation instead of duplicating the precedence/sanitization rules.
- Dropped `netstandard2.0` support: every project (including `Wslcc.Abstractions`, `Wslcc.Core`, and `Wslcc.Grpc.Contracts`) now targets `net10.0` only, removing the `Microsoft.Bcl.AsyncInterfaces`/`System.Threading.Channels` polyfills and the `IsExternalInit` shim now that `IAsyncEnumerable`, `Channel<T>`, and `init` accessors are natively available.
