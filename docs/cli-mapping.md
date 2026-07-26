# CLI mapping

`wslcc` mirrors `docker compose ...` under a `compose` branch, leaving the top level free for
WSLCC-specific commands. `wslcc compose ps` is the same token count as `docker compose ps`.

## Compose verbs

| Docker | WSLCC | Status |
| --- | --- | --- |
| `docker compose version` | `wslcc compose version` | Implemented (`--short`, `--format json\|pretty`) |
| `docker compose up` | `wslcc compose up` | Implemented (attached by default, `-d`/`--detach` to background; `-f`, `-p`, `--pull`, `--build`, `--no-build`; auto-builds `build:` services when their image is missing; honors `depends_on` conditions and applies service healthchecks; leaves unchanged, still-running services in place) |
| `docker compose down` | `wslcc compose down` | Implemented (`-f`, `-p`) |
| `docker compose ps` | `wslcc compose ps` | Implemented (`-f`, `-p`, `-a`) |
| `docker compose start` | `wslcc compose start` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose stop` | `wslcc compose stop` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose restart` | `wslcc compose restart` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose pull` | `wslcc compose pull` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose build` | `wslcc compose build` | Implemented (`-f`, `-p`, `[SERVICES]`) |
| `docker compose logs` | `wslcc compose logs` | Implemented (`-f`, `-p`, `[SERVICES]`, `--follow`, `--tail`, `--timestamps`, `--since`) |
| `docker compose config` | `wslcc compose config` | Implemented (`--format`, `--services`, `--volumes`, `--images`, `--profiles`, `--hash`, `--no-interpolate`, `-q`, `-o`) |

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

Applied to `up`, `down`, `ps`, `start`, `stop`, `restart`, `pull`, `build`, `logs`, and `config`:

| Option | Description |
| --- | --- |
| `-f`, `--file <path>` | Compose file. **Repeatable** — later files override earlier ones (`-f a.yaml -f b.yaml`). Defaults to `COMPOSE_FILE` (split on `COMPOSE_PATH_SEPARATOR`) then `compose.yaml` / `compose.yml` / `docker-compose.yaml` / `docker-compose.yml` in the current directory. |
| `-p`, `--project-name <name>` | Project name. Defaults to the file's `name:` or the project directory's name. Containers are named `<project>-<service>` and labelled `wslcc.project` / `wslcc.service`. |
| `--profile <name>` | Activate a profile (repeatable; also read from `COMPOSE_PROFILES`, or by naming a service that carries the profile). Services with a `profiles:` list are only included when one of their profiles is active. |
| `--env-file <path>` | Environment file for `${VAR}` interpolation. Defaults to `.env` in the project directory when present. |
| `--project-directory <path>` | Alternate project directory (default: the first compose file's directory). Sets the default `.env` location, the `build.context` base, and the default project name. |
| `--wslcc-host <uri>` | (all except `config`) Daemon endpoint. `npipe://<name>` (default) or `npipe://<server>/<name>` for a local pipe; `http(s)://host:port` for a remote daemon. wslcc-prefixed so it never collides with a standard `docker compose` option. |
| `--wslcc-provider <name>` | (all except `config`) Target `wslc` or `docker` for this command. When omitted, the daemon's default provider is used. wslcc-prefixed so it never collides with a standard `docker compose` option. |
| `--pull` | (`up`) Always pull images before starting. |
| `--build` | (`up`) Always (re)build services with a `build:` section before starting, even if the image already exists. Mutually exclusive with `--no-build`. |
| `--no-build` | (`up`) Never build; fail a `build:` service whose image is missing instead of building it. Mutually exclusive with `--build`. |
| `-d`, `--detach` | (`up`) Start the services in the background and return. By default `up` stays attached, streaming the containers' combined logs until Ctrl+C, which then gracefully stops the project. |
| `-a`, `--all` | (`ps`) Include stopped containers. |
| `[SERVICES]` | (`start`, `stop`, `restart`, `pull`, `build`, `logs`) Optional service names to target. Defaults to every matching service. |
| `--follow` | (`logs`) Keep streaming new log output; stop with Ctrl+C. No `-f` short form since `-f` is already `--file`. |
| `--tail <n>` | (`logs`) Show only the last `n` lines per container. Defaults to all. |
| `--timestamps` | (`logs`) Prefix each line with its timestamp (`<service> \| <timestamp> <line>`). |
| `--since <time>` | (`logs`) Only show lines newer than a duration (e.g. `10m`, `1h30m`) or an RFC3339 timestamp. |
| `--format <fmt>` | (`config`) Output format for the full document: `yaml` (default) or `json`. |
| `--services` | (`config`) Print the enabled service names, one per line, instead of the full document. |
| `--volumes` | (`config`) Print the declared volume names instead of the full document. |
| `--images` | (`config`) Print the distinct service image references instead of the full document. |
| `--profiles` | (`config`) Print the profile names declared across services (all of them, before filtering). |
| `--hash <services>` | (`config`) Print a per-service config hash. Use `*` for all services or a comma-separated list. |
| `--no-interpolate` | (`config`) Leave `${VAR}` references verbatim (files are still merged, `extends` resolved, profiles filtered). |
| `-q`, `--quiet` | (`config`) Validate only: resolve the configuration and print nothing on success. |
| `-o`, `--output <path>` | (`config`) Write the resolved document to a file instead of stdout. |

> The compose file is resolved on the client before anything is sent to the daemon: `-f` files are
> merged, `.env` is loaded, `${VAR}` references are interpolated, `extends` is resolved, and services
> excluded by the active profiles are dropped. See [compose-file.md](compose-file.md#resolution-features).

> `up` attaches to the project's combined log output by default (the same rendering as `compose logs`):
> the first Ctrl+C gracefully stops the containers (a second one abandons the wait), and the command then
> exits with code `130`. Pass `-d`/`--detach` to return immediately and leave the services running.

> `ps` with no `-f`/`-p` lists **all** wslcc-managed containers across projects (with a Project column);
> supply `-f` or `-p` to scope to one project. `down`, `start`, `stop`, `restart`, and `logs` always
> require a project (`-f` or `-p`). `pull` and `build` (like `up`) require a compose file, since they
> read service images / build contexts from it.

> `start`, `restart`, and `logs` process containers in `depends_on` order (dependencies first); `stop`
> and `down` use the reverse (dependents first). Ordering needs the compose file — with `-p` only, the
> daemon has no dependency graph and falls back to listing order. Naming a `[SERVICES]` argument that the project
> does not define (or, with `-p` only, that has no container) is rejected with `no such service: <name>`
> instead of being silently ignored; `pull` and `build` reject unknown service names against the compose
> file the same way.

> Beyond ordering, `up` honors `depends_on` **conditions**: `service_started` (the default) only
> guarantees start order, `service_healthy` waits for the dependency's healthcheck to pass, and
> `service_completed_successfully` waits for it to exit with code `0`. A service whose required
> dependency fails to start, becomes unhealthy, or exits non-zero is not started and is reported as
> failed. `service_healthy` needs the dependency to actually have a healthcheck — a `healthcheck:` in the
> compose file (applied to the container via `--health-*` run flags) or one baked into its image;
> otherwise the dependent fails with a clear message. A `healthcheck: { disable: true }` (or `test:
> ["NONE"]`) turns healthchecks off.

> `build` tags the built image as `<project>-<service>` unless the service also specifies `image:`, in
> which case that name is used. Relative `build.context` paths are resolved against the compose file's
> directory (sent to the daemon as `base_directory`), so `wslcc compose build` works correctly even when
> the daemon is a separate long-running process with a different current directory.

> `up` auto-builds any service with a `build:` section when its target image (the same `<project>-<service>`
> or `image:` tag `build` would use) is not present locally, mirroring `docker compose up`; if the image
> already exists it is reused. Pass `--build` to force a rebuild every time, or `--no-build` to skip
> building entirely (a `build:` service whose image is missing then fails instead of being built).

> `up` recreates containers only when needed. Each container is stamped with a `wslcc.config-hash` label
> (the same per-service hash `config --hash` reports); on a later `up`, a service whose container is still
> **running** with a matching hash is left in place (reported as `running`) instead of being recreated.
> Anything else — a changed hash, a stopped/absent container, or `--pull`/`--build` (which fetch fresh
> images) — recreates the container.

> `logs` merges output from every matching container (like `docker compose logs`), tagging each line
> with its service name (`<service> | <line>`). It uses a server-streaming RPC, so `--follow` keeps the
> connection open and yields new lines as they're written; press Ctrl+C to stop. A bounded (non-follow)
> dump is buffered and merged in **timestamp order** across containers, so the combined output reads
> chronologically rather than container-by-container; a live `--follow` stream is still interleaved as
> lines arrive (a running stream cannot be globally sorted). Timestamps are read from the container
> runtime for both ordering and `--timestamps` display, and `--since` is passed through to the runtime.

> `config` runs **entirely client-side** and needs no daemon: it performs the same client resolution the
> other verbs do (multi-file merge, `.env`, `${VAR}` interpolation, `extends`, profile filtering) and
> prints the resulting document — i.e. exactly what would be sent to `wslccd`, plus the effective project
> name as a leading `name:`. The document is written to `stdout` (or `-o <path>`); resolver warnings go to
> `stderr` so a redirected/piped document stays clean. `extends` and profile-gated services are already
> resolved, so the output has no `extends:` keys and only the services active for the selected profiles.
> `--profiles` is the exception — it reports every declared profile, including those not active. The
> `--hash` digest is a wslcc-specific SHA-256 of the canonical per-service config (for change detection),
> not Docker Compose's own hash. `--resolve-image-digests` is not implemented, since `config` is offline
> and pinning digests needs registry access.

## Global option

`--no-color` is the only option on **every** leaf command (it needs no daemon or provider), supplied after
the subcommand:

| Option | Description |
| --- | --- |
| `--no-color` | Disable colored output for the command. Also honored via the `NO_COLOR` environment variable. |

## Daemon endpoint and provider

The daemon endpoint and provider are **not** global — they are scoped to the commands that use them, and the
compose commands rename them to avoid clashing with standard `docker compose` options (wslcc mirrors the
compose CLI, and `--host`/`--provider` are wslcc-specific).

| Command family | Endpoint option | Provider option | Meaning of the provider option |
| --- | --- | --- | --- |
| `wslcc compose <up\|down\|ps\|start\|stop\|restart\|pull\|build\|logs\|version>` | `--wslcc-host <uri>` | `--wslcc-provider <name>` | Target this provider for this one command. |
| `wslcc compose config` | — | — | Runs entirely client-side; contacts no daemon or provider. |
| `wslcc version`, `wslcc daemon status\|stop` | `-H`, `--host <uri>` | — | (no provider option) |
| `wslcc daemon start`, `wslcc daemon install` | `-H`, `--host <uri>` | `--provider <name>` | Persist this provider as the daemon's **default**. |
| `wslcc daemon uninstall` | — | — | (neither) |

Examples: `wslcc compose ps --wslcc-host npipe://wslccd --wslcc-provider docker`,
`wslcc daemon start --provider docker`.

> Normally you don't need `--wslcc-provider` at all: set the daemon's default once with
> `wslcc daemon start --provider docker` (or `wslcc daemon install --provider docker`), and every
> subsequent command uses that default. Use `--wslcc-provider` only to override a single command.

> `--no-color` (and `NO_COLOR`) disables ANSI colors across all output, including the per-service prefix
> that `wslcc compose logs` normally colors — this is the equivalent of `docker compose logs --no-color`.
> Spectre.Console also auto-disables color when output is not a terminal (e.g. piped or redirected).
