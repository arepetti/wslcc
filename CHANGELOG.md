# Changelog

All notable changes to this project are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

No version has been tagged or published yet; everything below is under development on `main`.

## [Unreleased]

### Added

- Provider-agnostic engine with providers for WSL containers (`wslc`) and Docker (`docker`).
- `wslccd` daemon: gRPC over a named pipe (optional HTTPS with TLS and a bearer token; `wslcc daemon cert` mints a self-signed PEM pair). Runs as a per-user process (on demand or auto-started at logon).
- `compose` commands mirroring `docker compose`:
  - `up` (attached by default — streams logs and gracefully stops on Ctrl+C — or `-d`/`--detach`; auto-builds `build:` services when their image is missing, with `--build`/`--no-build`/`--pull`), `down`, `ps`.
  - `start`, `stop`, `restart`, `pull`, `build`, `logs` (`--follow`, `--tail`, `--timestamps`, `--since`; a non-follow dump is merged in timestamp order across containers) — all scoped to optional `[SERVICES]`.
  - `config` (client-side): `--format yaml|json`, `--services`/`--volumes`/`--images`, `--profiles`, `--hash`, `--no-interpolate`, `-q`, `-o`.
  - Containers are named `<project>-<service>` and labelled `wslcc.project`/`wslcc.service`; commands run in `depends_on` order (reversed for `stop`/`down`) and reject unknown service names.
  - `up` honors `depends_on` conditions (`service_healthy`, `service_completed_successfully`) and applies service `healthcheck:` settings, waiting for dependencies (and aborting dependents that fail).
  - `up` change-detection: an unchanged, still-running service (same config hash) is left in place instead of recreated; `--pull`/`--build` force recreation.
  - `up` creates the project's `networks:` and named `volumes:` (and a default network) and attaches each service, so services reach each other by name; `down` removes the project's networks, and `down --volumes`/`-v` also removes its named volumes.
- Client-side compose file resolution: multi-file merge (`-f` repeatable, `COMPOSE_FILE`), `.env` (quotes, escapes, inline comments, multi-line and self-referencing values) and `${VAR}` interpolation (`--env-file`), `include` (local files; own project directory and env), `extends`, `profiles` (`--profile`, `COMPOSE_PROFILES`), and `--project-directory`.
- Service `annotations:` (map and `KEY=VALUE` list) are passed to Docker as `--annotation` (OCI annotations; not mixed into the label store); WSLc rejects them because 3.0.1 exposes no equivalent.
- WSLc 3.0.1 GA integration through `Microsoft.WSL.Containers` 3.0.1: a managed, persistent named session handles prerequisites/version, image pull/lookup, and basic lifecycle, with a dedicated session-scoped CLI fallback for builds, labels/discovery, retained logs, healthchecks, networks, and generic volumes.
- Compose support for long-form ports; `domainname`; `gpus`; `cpus`; `mem_limit`; DNS settings; `shm_size`; `stdin_open`; `tty`; `ulimits`; `stop_signal`; `stop_grace_period`; supported `network_mode`; network aliases/static IPv4; network IPAM/internal/driver options; volume driver options; and build labels/no-cache/pull/secrets.
- Compose `secrets:` (top-level `file:` / `environment:`, service short and `source`/`target` form) are bind-mounted read-only at `/run/secrets/<name>` (or `target`). Swarm `external` secrets fail the load; `uid`/`gid`/`mode` and `build.secrets` are not applied.
- Daemon commands: `daemon start`/`stop`/`status`, `daemon install`/`uninstall` (per-user autostart at logon, no elevation), `daemon cert` (self-signed TLS material for HTTPS), and top-level `version`.
- Options: `--no-color` (and `NO_COLOR`) on every command; `compose` commands use `--wslcc-host`/`--wslcc-provider`, while `version`/`daemon` commands use `-H`/`--host` (plus `--provider` on `daemon start`/`install` to set the daemon's default).
- Targets `net10.0`; WSL SDK consumers target `net10.0-windows10.0.26100.0`.
- Documentation: [docs/troubleshooting.md](docs/troubleshooting.md), [docs/compatibility.md](docs/compatibility.md), [docs/README.md](docs/README.md), roadmap milestones.

### Changed
- Renamed the Compose libraries to `Wslcc.Compose.Configuration` and `Wslcc.Compose.Engine` to distinguish file resolution from runtime orchestration.
- The WSLc provider no longer reuses Docker Go-template commands. WSLc listings use GA JSON output and every fallback call targets the SDK-owned `wslcc` session.
- WSLc unsupported annotations, restart policies, read-only root filesystems, and host networking now fail clearly instead of being passed as invalid flags.
- Compose lifecycle RPCs (`Up`/`Down`/`Ps`/`Start`/`Stop`/`Restart`/`Pull`/`Build`) are server-streaming: per-service `ServiceProgress` events, then a completed response. The CLI prints live progress instead of a blocking spinner.
- Optional remote daemon transport is **HTTPS + bearer token** only; enabling HTTP without a certificate and token (or with `http://`) fails at daemon start. The local named pipe is unchanged.

### Fixed
- `compose pull` skips build-only services (no `image:`) instead of reporting them as failed.
- Long-form `ports:` map entries are rejected with a clear error instead of being coerced into a garbage runtime argument.
- String `command:` / `entrypoint:` use Compose shell form (`/bin/sh -c "…"`) instead of a single argv token.
- Service `user:`, `working_dir:`, `labels:`, `entrypoint:`, `env_file:`, `container_name:`, `hostname:`, and `read_only:` are applied at runtime (no longer silent no-ops).
- Service `tmpfs:` and long-form `volumes:` with `type: volume` / `bind` / `tmpfs` (including nested option blocks) are applied as `-v` / `--mount` / `--tmpfs`. Unsupported types (`npipe`, `cluster`, `image`) fail the load with a clear error.

