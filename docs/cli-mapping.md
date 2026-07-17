# CLI mapping

`wslcc` mirrors `docker compose ...` under a `compose` branch, leaving the top level free for
WSLCC-specific commands. `wslcc compose ps` is the same token count as `docker compose ps`.

## Compose verbs

| Docker | WSLCC | Status |
| --- | --- | --- |
| `docker compose version` | `wslcc compose version` | Implemented (`--short`, `--format json\|pretty`) |
| `docker compose up` | `wslcc compose up` | Implemented (detached; `-f`, `-p`, `--pull`) |
| `docker compose down` | `wslcc compose down` | Implemented (`-f`, `-p`) |
| `docker compose ps` | `wslcc compose ps` | Implemented (`-f`, `-p`, `-a`) |
| `docker compose logs` | `wslcc compose logs` | Stub (see [todo.md](todo.md)) |
| `docker compose build` | `wslcc compose build` | Stub |
| `docker compose pull` | `wslcc compose pull` | Stub |
| `docker compose config` | `wslcc compose config` | Stub |
| `docker compose start/stop/restart` | `wslcc compose start/stop/restart` | Stub |

## WSLCC-specific commands

| Command | Description |
| --- | --- |
| `wslcc version` | Rich version report: wslcc, daemon, and every provider (analogous to `docker version`). |
| `wslcc --version` / `-v` | Print the wslcc build version only (no daemon contact). |
| `wslcc daemon start [--provider <name>]` | Launch the local daemon and wait until ready. `--provider` sets the daemon's default provider so other commands don't need to repeat it. |
| `wslcc daemon stop` | Stop the daemon gracefully. |
| `wslcc daemon status` | Report whether the daemon is running. |

## Compose command options

Applied to `up`, `down`, and `ps`:

| Option | Description |
| --- | --- |
| `-f`, `--file <path>` | Compose file. Defaults to `compose.yaml` / `compose.yml` / `docker-compose.yaml` / `docker-compose.yml` in the current directory. |
| `-p`, `--project-name <name>` | Project name. Defaults to the file's `name:` or the directory name. Containers are named `<project>-<service>` and labelled `wslcc.project` / `wslcc.service`. |
| `--pull` | (`up`) Always pull images before starting. |
| `-a`, `--all` | (`ps`) Include stopped containers. |

> `up` currently runs detached; foreground/attached log streaming is tracked in [todo.md](todo.md).

> `ps` with no `-f`/`-p` lists **all** wslcc-managed containers across projects (with a Project column);
> supply `-f` or `-p` to scope to one project. `down` always requires a project (`-f` or `-p`).

## Global options

Available on every leaf command and supplied after the subcommand, e.g.
`wslcc compose ps --host npipe://wslccd --provider docker`.

| Option | Description |
| --- | --- |
| `-H`, `--host <uri>` | Daemon endpoint. `npipe://<name>` (default) or `npipe://<server>/<name>` for a local pipe; `http(s)://host:port` for a remote daemon. |
| `--provider <name>` | Target `wslc` or `docker` for this command. When omitted, the daemon's default provider is used (configured in appsettings.json or via `wslcc daemon start --provider <name>`). |

> Normally you don't need `--provider` at all: set it once with `wslcc daemon start --provider docker`,
> and every subsequent command uses that default. Use `--provider` only to override a single command.
