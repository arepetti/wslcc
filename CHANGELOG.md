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
