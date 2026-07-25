# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Provider-agnostic engine with providers for WSL containers (`wslc`) and Docker Compose (`docker`).
- `wslccd` daemon: gRPC over a named pipe (optional HTTP), runnable as a per-user process or a Windows Service.
- `compose` commands mirroring `docker compose`:
  - `up` (attached by default — streams logs and gracefully stops on Ctrl+C — or `-d`/`--detach`; auto-builds `build:` services when their image is missing, with `--build`/`--no-build`/`--pull`), `down`, `ps`.
  - `start`, `stop`, `restart`, `pull`, `build`, `logs` (`--follow`, `--tail`, `--timestamps`, `--since`; a non-follow dump is merged in timestamp order across containers) — all scoped to optional `[SERVICES]`.
  - `config` (client-side): `--format yaml|json`, `--services`/`--volumes`/`--images`, `--profiles`, `--hash`, `--no-interpolate`, `-q`, `-o`.
  - Containers are named `<project>-<service>` and labelled `wslcc.project`/`wslcc.service`; commands run in `depends_on` order (reversed for `stop`/`down`) and reject unknown service names.
- Client-side compose file resolution: multi-file merge (`-f` repeatable, `COMPOSE_FILE`), `.env` (quotes, escapes, inline comments, multi-line and self-referencing values) and `${VAR}` interpolation (`--env-file`), `extends`, `profiles` (`--profile`, `COMPOSE_PROFILES`), and `--project-directory`.
- Daemon commands: `daemon start`/`stop`/`status`, `daemon install`/`uninstall` (Windows Service), and top-level `version`.
- Options: `--no-color` (and `NO_COLOR`) on every command; `compose` commands use `--wslcc-host`/`--wslcc-provider`, while `version`/`daemon` commands use `-H`/`--host` (plus `--provider` on `daemon start`/`install` to set the daemon's default).
- Targets `net10.0`.
