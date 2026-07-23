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
| `docker compose start` | `wslcc compose start` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose stop` | `wslcc compose stop` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose restart` | `wslcc compose restart` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose pull` | `wslcc compose pull` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose build` | `wslcc compose build` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose logs` | `wslcc compose logs` | Implemented (`-f`, `-p`, `[SERVICES]`, `--follow`, `--tail`) |
| `docker compose config` | `wslcc compose config` | Stub |

## WSLCC-specific commands

| Command | Description |
| --- | --- |
| `wslcc version` | Rich version report: wslcc, daemon, and every provider (analogous to `docker version`). |
| `wslcc --version` / `-v` | Print the wslcc build version only (no daemon contact). |
| `wslcc daemon start [--provider <name>]` | Launch the local daemon and wait until ready. `--provider` sets the daemon's default provider so other commands don't need to repeat it. |
| `wslcc daemon stop` | Stop the daemon gracefully. |
| `wslcc daemon status` | Report whether the daemon is running. |
| `wslcc daemon install [--startup auto\|manual\|disabled] [--start] [--provider <name>]` | Register `wslccd` as a Windows Service (requires Administrator). |
| `wslcc daemon uninstall` | Stop and remove the `wslccd` Windows Service (requires Administrator). |

## Compose command options

Applied to `up`, `down`, `ps`, `start`, `stop`, `restart`, `pull`, `build`, and `logs`:

| Option | Description |
| --- | --- |
| `-f`, `--file <path>` | Compose file. **Repeatable** — later files override earlier ones (`-f a.yaml -f b.yaml`). Defaults to `COMPOSE_FILE` (split on `COMPOSE_PATH_SEPARATOR`) then `compose.yaml` / `compose.yml` / `docker-compose.yaml` / `docker-compose.yml` in the current directory. |
| `-p`, `--project-name <name>` | Project name. Defaults to the file's `name:` or the project directory's name. Containers are named `<project>-<service>` and labelled `wslcc.project` / `wslcc.service`. |
| `--profile <name>` | Activate a profile (repeatable; also read from `COMPOSE_PROFILES`, or by naming a service that carries the profile). Services with a `profiles:` list are only included when one of their profiles is active. |
| `--env-file <path>` | Environment file for `${VAR}` interpolation. Defaults to `.env` in the project directory when present. |
| `--project-directory <path>` | Alternate project directory (default: the first compose file's directory). Sets the default `.env` location, the `build.context` base, and the default project name. |
| `--pull` | (`up`) Always pull images before starting. |
| `-a`, `--all` | (`ps`) Include stopped containers. |
| `[SERVICES]` | (`start`, `stop`, `restart`, `pull`, `build`, `logs`) Optional service names to target. Defaults to every matching service. |
| `--follow` | (`logs`) Keep streaming new log output; stop with Ctrl+C. No `-f` short form since `-f` is already `--file`. |
| `--tail <n>` | (`logs`) Show only the last `n` lines per container. Defaults to all. |

> The compose file is resolved on the client before anything is sent to the daemon: `-f` files are
> merged, `.env` is loaded, `${VAR}` references are interpolated, `extends` is resolved, and services
> excluded by the active profiles are dropped. See [compose-file.md](compose-file.md#resolution-features).

> `up` currently runs detached; foreground/attached log streaming for `up` itself is tracked in
> [todo.md](todo.md) (use `wslcc compose logs --follow` separately in the meantime).

> `ps` with no `-f`/`-p` lists **all** wslcc-managed containers across projects (with a Project column);
> supply `-f` or `-p` to scope to one project. `down`, `start`, `stop`, `restart`, and `logs` always
> require a project (`-f` or `-p`). `pull` and `build` (like `up`) require a compose file, since they
> read service images / build contexts from it.

> `build` tags the built image as `<project>-<service>` unless the service also specifies `image:`, in
> which case that name is used. Relative `build.context` paths are resolved against the compose file's
> directory (sent to the daemon as `base_directory`), so `wslcc compose build` works correctly even when
> the daemon is a separate long-running process with a different current directory.

> `logs` merges output from every matching container (like `docker compose logs`), tagging each line
> with its service name (`<service> | <line>`). It uses a server-streaming RPC, so `--follow` keeps the
> connection open and yields new lines as they're written; press Ctrl+C to stop.

## Global options

Available on every leaf command and supplied after the subcommand, e.g.
`wslcc compose ps --host npipe://wslccd --provider docker`.

| Option | Description |
| --- | --- |
| `-H`, `--host <uri>` | Daemon endpoint. `npipe://<name>` (default) or `npipe://<server>/<name>` for a local pipe; `http(s)://host:port` for a remote daemon. |
| `--provider <name>` | Target `wslc` or `docker` for this command. When omitted, the daemon's default provider is used (configured in appsettings.json or via `wslcc daemon start --provider <name>`). |

> Normally you don't need `--provider` at all: set it once with `wslcc daemon start --provider docker`,
> and every subsequent command uses that default. Use `--provider` only to override a single command.
