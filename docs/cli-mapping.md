# wslcc(1) — WSLCC CLI Reference

## SYNOPSIS

```
wslcc [--no-color] <command> [<args>]
wslcc --version | -v
wslcc --help | -h
```

## DESCRIPTION

`wslcc` is the command-line interface for WSLCC, a provider-agnostic container orchestrator for Windows. It talks to a small background daemon, `wslccd`, over a local named pipe (or, optionally, a remote HTTP/2 endpoint) — see [daemon.md](daemon.md). The daemon in turn drives one of two providers: WSL containers (`wslc`) or Docker Compose (`docker`) — see [providers.md](providers.md).

Commands are organized under two branches, plus one top-level command:

- **`wslcc compose ...`** — manages a Compose application. It mirrors `docker compose` deliberately: the verbs, most flags, and the YAML format are the same, so if you already know `docker compose` you already know most of `wslcc compose`. This document is nonetheless self-contained — every command and option below is described on its own terms, not as a diff against Docker's docs. If you want the quick side-by-side instead, see [Coming from Docker Compose](#coming-from-docker-compose).
- **`wslcc daemon ...`** — starts, stops, and registers autostart for `wslccd` itself.
- **`wslcc version`** — a top-level status report across the CLI, daemon, and every provider.

## GLOBAL OPTIONS

One option is available on **every** command, supplied after the subcommand (e.g. `wslcc compose up --no-color`):

**--no-color** :   Disable colored output for this invocation. Also honored via the `NO_COLOR` environment variable — see [Environment variables](#environment-variables). Spectre.Console (the rendering library `wslcc` is built on) additionally auto-disables color when output is not a terminal (piped or redirected), so scripts rarely need this explicitly.

The daemon endpoint and target provider are deliberately **not** global — see [Project name and connection](#project-name-and-connection) for why, and which option each command family actually uses.

---

## TOP-LEVEL COMMANDS

### wslcc version

Report the CLI version, the daemon's version, and every provider's underlying tool version — analogous to `docker version`.

```
wslcc version [-H|--host <uri>]
```

**-H, --host** _uri_ :   Daemon endpoint to contact. See [Project name and connection](#project-name-and-connection) for the `npipe://`/`http(s)://` syntax. Default: `npipe://wslccd`.

Prints a table with one row per component (`wslccd`, `wslc`, `docker`) showing its version and availability. If a provider's underlying tool isn't installed or reachable, its row is marked `unavailable` with a detail line explaining why, rather than failing the whole command. If the daemon itself is unreachable, the command reports that and exits non-zero — start it first with `wslcc daemon start`.

---

### wslcc --version

Print only the `wslcc` build version and exit — no daemon contact, no provider checks. `-v` is equivalent. This is the flag Spectre.Console registers automatically for the application root; it is not a subcommand.

```
wslcc --version
wslcc -v
```

---

## DAEMON COMMANDS

Commands under `wslcc daemon` manage the `wslccd` background process. See [daemon.md](daemon.md) for how the daemon itself works (transport, configuration, autostart mechanics). Every command here shares one connection option:

**-H, --host** _uri_ :   Daemon endpoint. See [Project name and connection](#project-name-and-connection) for the `npipe://`/`http(s)://` syntax. Default: `npipe://wslccd`. Kept as the plain, unprefixed name (unlike the compose commands) because these commands aren't part of the `docker compose` surface `wslcc compose` mirrors, so there's no standard name to avoid colliding with.

### wslcc daemon start

Launch `wslccd` as a detached per-user process and wait until it responds, or report that it's already running.

```
wslcc daemon start [--provider <name>] [-H|--host <uri>]
```

**--provider** _name_ :   Provider to make the daemon's **default** (`wslc` or `docker`) — persisted for the life of that daemon process by passing `--Wslcc:DefaultProvider=<name>` on its command line, so every later command that doesn't pass its own provider override uses this one. If the daemon is already running with a different default, `start` warns rather than silently ignoring the mismatch; changing it requires `wslcc daemon stop` followed by `daemon start --provider <name>` again.

Only manages a **local** daemon — reachable over a named pipe. Passing an `http(s)://` `--host` (a remote daemon) is rejected, since there's nothing local to launch. `wslccd` is located via the `WSLCCD_PATH` environment variable, otherwise next to `wslcc.exe` (see [Environment variables](#environment-variables)).

---

### wslcc daemon stop

Ask the running daemon to shut down gracefully.

```
wslcc daemon stop [-H|--host <uri>]
```

If no daemon is reachable at the given endpoint, this is reported (not an error) and the command exits `0` — "already stopped" and "successfully stopped" are the same end state.

---

### wslcc daemon status

Report whether the daemon is reachable at the given endpoint.

```
wslcc daemon status [-H|--host <uri>]
```

Exits `0` when the daemon responds (printing its version and configured default provider), `1` otherwise.

---

### wslcc daemon install

Register a **per-user autostart** so `wslccd` launches automatically every time you sign in — no Windows Service, no Administrator elevation.

```
wslcc daemon install [--start] [--provider <name>] [-H|--host <uri>]
```

**--start** :   Also start the daemon immediately (equivalent to running `wslcc daemon start` right after), instead of waiting for your next sign-in.

**--provider** _name_ :   Same meaning as on `daemon start` — persists this provider as the default for every future autostarted launch.

Mechanically, this writes a value named `WSLCC Daemon` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (via `reg.exe`) that points at `wslccd`. `HKCU` is writable without elevation, and the daemon then runs in *your* session at logon — where it can see your WSL distros and Docker context, which a `LocalSystem` Windows Service in session 0 could not. Re-running `install` overwrites the existing entry, so it's safe to run again (e.g. to change `--provider`). If `wslcc` was installed via winget, the entry points at the stable `%LOCALAPPDATA%\Microsoft\WinGet\Links\wslccd.exe` alias so it keeps working across package upgrades; otherwise it falls back to the same resolution as `daemon start` (`WSLCCD_PATH`, then next to `wslcc.exe`).

> Need a machine-wide daemon that runs without anyone signed in? `wslccd` still calls `UseWindowsService`, so an administrator *can* register it manually with `sc.exe create` — but it would then run as `LocalSystem` in session 0 and generally can't reach a user's WSL/Docker context. The per-user autostart above is the model this CLI is built around; see [roadmap.md](roadmap.md) for a possible future MSI-based machine-wide service.

---

### wslcc daemon uninstall

Remove the autostart entry created by `daemon install`.

```
wslcc daemon uninstall
```

Does **not** stop an already-running daemon — use `wslcc daemon stop` for that. Running `uninstall` when no entry exists is reported (not an error) and exits `0`.

---

## COMPOSE COMMANDS

`wslcc compose` manages a Compose application: one or more YAML files describing services, plus a project name that scopes their containers. Every command below shares the options in [Compose file and project options](#compose-file-and-project-options) and (except `config`) the connection options in [Project name and connection](#project-name-and-connection).

For the YAML format itself — every key `wslcc` reads, exactly what it does with each, and what it doesn't yet support — see [compose-file.md](compose-file.md). That document is the authoritative reference for compose *files*; this one is the reference for the compose *commands*.

### Compose file and project options

These select **which** files make up the project and **which** of its services/profiles are in scope. They're accepted by every compose command, including `config`.

| Option | Description |
| --- | --- |
| `-f`, `--file <path>` | Compose file to load. **Repeatable** (`-f a.yaml -f b.yaml`) — later files override earlier ones using Compose's per-attribute merge rules (see [compose-file.md#resolution-features](compose-file.md#resolution-features)). |
| `-p`, `--project-name <name>` | Project name. See [Project name](#project-name-and-connection) for how the effective name is chosen. |
| `--profile <name>` | Activate a profile. Repeatable. See [compose-file.md#profiles](compose-file.md#profiles). |
| `--env-file <path>` | Environment file used for `${VAR}` interpolation. Defaults to `.env` in the project directory when present. |
| `--project-directory <path>` | Alternate project directory. Changes the default `.env` location, the base for relative `build.context`/bind-mount paths, and the default project name. Defaults to the first `-f` file's directory. |

**File discovery.** With no `-f`, `wslcc` looks for `COMPOSE_FILE` in the environment (paths separated by `COMPOSE_PATH_SEPARATOR`, defaulting to `;` on Windows); if that's unset too, it looks in the project directory for `compose.yaml`, then `compose.yml`, then `docker-compose.yaml`, then `docker-compose.yml`, using the first one found. A command that needs a project and finds none this way (no `-f`, no `COMPOSE_FILE`, no conventional file present, and — for commands where it would help — no `-p` either) fails with a clear "no compose file found" message rather than guessing.

Resolution — merging files, loading `.env`, interpolating variables, resolving `extends`, and filtering by profile — happens entirely **on the client**, before anything is sent to `wslccd`: the daemon always receives one already-resolved document. `wslcc compose config` (below) shows you exactly that document.

### Project name and connection

**Project name.** The effective name is, in order: `-p`/`--project-name` if given, else the resolved file's top-level `name:`, else the project directory's own name, else `wslcc` if nothing else applies. Whatever is chosen is *sanitized* — lowercased, with anything that isn't a letter, digit, `-`, or `_` either turned into `_` (spaces and dots) or dropped — matching Compose's own project-name rules. Containers are then named `<project>-<service>` and labelled `wslcc.project=<project>` / `wslcc.service=<service>`, which is how `ps`, `logs`, and the rest find them again later.

**Daemon endpoint.** Every compose command except `config` (which is entirely client-side, see below) talks to `wslccd`, and needs to know where it is:

**--wslcc-host** _uri_ :   `npipe://<name>` (default `npipe://wslccd`) for a local named pipe, or `npipe://<server>/<name>` for one on a remote machine, or `http(s)://host:port` for a remote daemon over HTTP/2. Named `--wslcc-host` rather than plain `--host` specifically so it can never collide with a real `docker compose` option, since `wslcc compose` otherwise mirrors that CLI's flag names on purpose.

**--wslcc-provider** _name_ :   Target `wslc` or `docker` for this one command, overriding the daemon's configured default. Same `--wslcc-` prefix rationale as above.

You normally don't need `--wslcc-provider` at all: set the daemon's default once with `wslcc daemon start --provider docker` (or `daemon install --provider docker`), and every later command uses it automatically. Reach for `--wslcc-provider` only to override a single invocation. (These are the *only* two places `--provider` is spelled `--wslcc-provider` — on `daemon start`/`daemon install` themselves it stays plain `--provider`, because there it's setting the daemon's own default rather than targeting one `docker compose`-shaped command; see [daemon.md](daemon.md).)

---

### wslcc compose version

Report the version of whichever provider is active — analogous to `docker compose version`.

```
wslcc compose version [--short] [--format <fmt>] [--wslcc-host <uri>] [--wslcc-provider <name>]
```

**--short** :   Print only the version string.

**--format** _fmt_ :   `pretty` (default) or `json`.

Unlike the other compose commands, `version` takes no compose file options — it isn't scoped to a project — which is why it declares `--wslcc-host`/`--wslcc-provider` directly rather than inheriting the full file/project option set.

---

### wslcc compose up

Create and start the project's services.

```
wslcc compose up [--pull] [--build | --no-build] [-d|--detach] [OPTIONS]
```

(`[OPTIONS]` here, and in every synopsis below, stands for the shared [compose file/project](#compose-file-and-project-options) and [connection](#project-name-and-connection) options.)

**--pull** :   Always pull every service's image before starting, even if it already exists locally. Forces recreation of any container that would otherwise be left alone by change detection (below).

**--build** :   (Re)build every service that has a `build:` section before starting, even if its target image already exists. Forces recreation, same as `--pull`. Mutually exclusive with `--no-build`.

**--no-build** :   Never build. A `build:` service whose image is missing fails instead of triggering a build. Mutually exclusive with `--build`.

**-d, --detach** :   Start the services and return immediately, instead of the default attached mode.

**Auto-build.** With neither `--build` nor `--no-build`, a service with a `build:` section is built automatically the first time (when its target image — the same `<project>-<service>`, or its `image:` tag if set — doesn't exist yet) and reused on every later `up` (the image already exists), mirroring `docker compose up`'s default behavior.

**Attached by default.** Unlike `docker compose up` (which defaults to detached), `up` here **attaches** to the project's combined log output by default — the same rendering `compose logs` uses, service names colored and prefixed. The first Ctrl+C gracefully stops every container (a second Ctrl+C abandons the wait instead), and the command then exits with code `130`. Pass `-d`/`--detach` for the traditional "start and return" behavior.

**Ordering, dependencies, health, change detection, networks, and volumes** are all part of `up`'s behavior but substantial enough to have their own sections below: see [Startup order, dependencies, and health](#startup-order-dependencies-and-health), [Change detection](#change-detection), and [Networks and volumes](#networks-and-volumes).

Requires a compose file (`-f` or a conventional file found in the project directory) — unlike `start`/`stop`/`restart`/`logs`/`down`, a bare `-p <project>` isn't enough, since there's no existing container to start from scratch.

---

### wslcc compose down

Stop and remove the project's containers, and its networks.

```
wslcc compose down [-v|--volumes] [OPTIONS]
```

**-v, --volumes** :   Also remove the project's named volumes. Off by default, so data survives a `down` — pass this explicitly when you actually want to discard it.

Containers are torn down in reverse dependency order (dependents before their dependencies — see [Startup order](#startup-order-dependencies-and-health)), then the project's networks are removed. `external: true` networks/volumes are never touched, with or without `-v`. Requires a project (`-f` or `-p`) — see [File discovery](#compose-file-and-project-options).

---

### wslcc compose ps

List the project's containers.

```
wslcc compose ps [-a|--all] [OPTIONS]
```

**-a, --all** :   Include stopped containers (default: running only).

Unlike the other compose commands, `ps` with **no** `-f`/`-p` at all doesn't fail — it lists every `wslcc`-managed container across **every** project, with an extra `Project` column. Supply `-f` or `-p` to scope the listing to one project (the `Project` column is then redundant and omitted).

---

### wslcc compose start

Start existing (stopped) containers for the given services, without recreating them.

```
wslcc compose start [SERVICES] [OPTIONS]
```

**[SERVICES]** :   Service names to start. Optional — defaults to every service that has an existing container. Naming a service the project doesn't define is rejected (`no such service: <name>`) rather than silently skipped.

Processes services in dependency order (dependencies first) — see [Startup order](#startup-order-dependencies-and-health). Requires a project.

---

### wslcc compose stop

Stop the project's containers without removing them.

```
wslcc compose stop [SERVICES] [OPTIONS]
```

**[SERVICES]** :   Service names to stop. Optional — defaults to every running service. Same unknown-name rejection as `start`.

Processes services in **reverse** dependency order (dependents first, mirroring `down`) — see [Startup order](#startup-order-dependencies-and-health). Requires a project.

---

### wslcc compose restart

Restart the project's containers in place.

```
wslcc compose restart [SERVICES] [OPTIONS]
```

**[SERVICES]** :   Service names to restart. Optional — defaults to every service with an existing container. Same unknown-name rejection as `start`.

Processes services in dependency order (dependencies first). Requires a project.

---

### wslcc compose pull

Pull each service's image, unconditionally (bypassing whatever is already cached locally).

```
wslcc compose pull [SERVICES] [OPTIONS]
```

**[SERVICES]** :   Service names to pull. Optional — defaults to every service that has an `image:`. Same unknown-name rejection as `start`. Requires a compose file (pulling needs to read each service's image reference from it — a bare `-p` isn't enough).

---

### wslcc compose build

Build (or rebuild) the image for each service that has a `build:` section.

```
wslcc compose build [SERVICES] [OPTIONS]
```

**[SERVICES]** :   Service names to build. Optional — defaults to every service with a `build:` section. Same unknown-name rejection as `start`. Requires a compose file, for the same reason as `pull` — building needs the `build:` context/dockerfile/args, not just a container list.

The built image is tagged `<project>-<service>` unless the service also sets `image:`, in which case that name is used. A relative `build.context` resolves against the compose file's own directory, sent to the daemon as `base_directory` — so this works correctly even though `wslccd` is a separate long-running process with its own current directory.

---

### wslcc compose logs

Show (or follow) log output from the project's containers, merged and tagged with each container's service name.

```
wslcc compose logs [SERVICES] [--follow] [--tail <n>] [--timestamps] [--since <time>] [OPTIONS]
```

**[SERVICES]** :   Service names to show. Optional — defaults to every service with an existing container. Same unknown-name rejection as `start`.

**--follow** :   Keep streaming new output (like `tail -f`) until interrupted with Ctrl+C. There is deliberately no `-f` short form for this — `-f` already means `--file` on every compose command.

**--tail** _n_ :   Show only the last `n` lines per container. Default: all available lines.

**--timestamps** :   Prefix each line with its timestamp, read from the container runtime.

**--since** _time_ :   Only show lines newer than a duration (`10m`, `1h30m`, ...) or an RFC3339 timestamp. Passed straight through to the runtime.

Each line is printed as `<service> | <line>` (or `<service> | <timestamp> <line>` with `--timestamps`), colored per service unless [`--no-color`](#global-options) is in effect. A **bounded** (non-`--follow`) dump is buffered and merged in **timestamp order** across all matching containers, so the combined output reads chronologically rather than one container at a time; a live `--follow` stream is still interleaved as lines arrive instead (a running stream can't be globally sorted without delaying every line). This uses a server-streaming RPC under the hood, so `--follow` genuinely keeps the connection open rather than polling.

Requires a project (`-f` or `-p`), and — like `start`/`stop`/`restart` — needs the compose file itself to enforce dependency ordering or reject unknown service names against anything other than "has no container".

---

### wslcc compose config

Resolve the project's compose file(s) — merge, `.env`, interpolation, `extends`, profile filtering — and print (or inspect) the result. Runs **entirely client-side**; no daemon contact at all.

```
wslcc compose config [--format <fmt>] [--services | --volumes | --images | --profiles | --hash <services>]
                     [--no-interpolate] [-q|--quiet] [-o|--output <path>]
                     [-f <path>]... [-p <name>] [--profile <name>]... [--env-file <path>] [--project-directory <path>]
```

**--format** _fmt_ :   Output format for the full document: `yaml` (default) or `json`.

**--services** :   Print the enabled service names, one per line, instead of the full document.

**--volumes** :   Print the declared top-level volume names instead of the full document.

**--images** :   Print the distinct service `image:` references instead of the full document.

**--profiles** :   Print every profile name declared across services — **before** filtering, i.e. including profiles that aren't currently active — instead of the full document.

**--hash** _services_ :   Print a per-service config hash instead of the full document. `*` for every service, or a comma-separated list of names.

**--no-interpolate** :   Leave `${VAR}` references verbatim. Files are still merged, `extends` is still resolved, and profile filtering still runs.

**-q, --quiet** :   Resolve and validate only; print nothing on success (non-zero exit on error). Useful in CI to lint a compose file without producing output.

**-o, --output** _path_ :   Write the resolved document to a file instead of `stdout`.

`--services`, `--volumes`, `--images`, `--profiles`, and `--hash` are mutually exclusive (pick at most one; with none, the full document is printed). The document — exactly what every other compose command would send to `wslccd`, plus the effective [project name](#project-name-and-connection) added as a leading `name:` — goes to `stdout` (or `-o`); resolver warnings (e.g. an unset interpolation variable) go to `stderr`, so `wslcc compose config > resolved.yaml` always captures a clean file. Because `extends` and profile-gated services are already resolved by this point, the output never contains an `extends:` key and only ever lists services active for the selected profiles. `--hash` is a WSLCC-specific SHA-256 of each service's canonical configuration (used by `up`'s [change detection](#change-detection)) — not Docker Compose's own config hash. `--resolve-image-digests` is not implemented, since `config` runs offline and pinning digests needs registry access.

Full key-by-key detail on what gets resolved is in [compose-file.md#resolution-features](compose-file.md#resolution-features).

---

## STARTUP ORDER, DEPENDENCIES, AND HEALTH

`up`, and the container ordering used by `start`/`stop`/`restart`/`logs`/`down`, follow each service's `depends_on:`:

- `start`, `restart`, and `logs` process services in dependency order (**dependencies first**).
- `stop` and `down` use the **reverse** order (**dependents first**).
- Naming a `[SERVICES]` argument the project doesn't define (or, when scoped only by `-p` with no compose file, one with no existing container) is rejected with `no such service: <name>` rather than silently ignored; `pull` and `build` reject unknown names against the compose file the same way.
- Ordering needs the compose file. With only `-p <project>` (no `-f`/conventional file found), the daemon has no dependency graph for that project and falls back to listing order.

`up` additionally honors `depends_on`'s **conditions** — `service_started` (default, order only), `service_healthy` (waits for the dependency's healthcheck), and `service_completed_successfully` (waits for exit code `0`) — and fails a service whose *required* dependency doesn't come up, which cascades to that service's own dependents. Full syntax and the exact healthcheck fields are documented in [compose-file.md#startup-order-and-health](compose-file.md#startup-order-and-health).

## CHANGE DETECTION

`up` recreates a container only when something actually changed: each container carries a `wslcc.config-hash` label (the same hash `config --hash` reports), and a service whose container is still **running** with a matching hash is left alone — reported as `running` rather than `started`. `--pull`/`--build` always force recreation (they fetch fresh content). Details: [compose-file.md#change-detection-up](compose-file.md#change-detection-up).

## NETWORKS AND VOLUMES

`up` creates the project's declared `networks:` and named `volumes:` (plus an implicit default network) and attaches every service to its networks under its service name, so services resolve each other by name with no extra configuration. `down` removes the project's networks once its containers are gone; named volumes are preserved unless you pass `-v`/`--volumes`. `external: true` resources are never created or removed either way. Full mount-syntax handling (bind vs. named vs. anonymous volumes) is in [compose-file.md#networks-and-volumes](compose-file.md#networks-and-volumes).

---

## SCRIPTING

**Exit codes.** Every command exits `0` on success. Most failures exit `1`. `up` in attached mode (the default — see [`up`](#wslcc-compose-up)) exits `130` on Ctrl+C, matching a shell's own SIGINT convention. A partial failure across several services (e.g. one service in `up`/`start`/`stop` fails while others succeed) still exits `1` overall, with the per-service detail printed above the summary.

**Machine-readable output.** `wslcc compose version --format json` and `wslcc compose config --format json` (or the whole document with no `--format` flag at all, in YAML) are the two structured-output escape hatches; every other command's output is meant for a human at a terminal. Combine with [`--no-color`](#global-options) (or redirect output — Spectre.Console detects that automatically) when scripting the human-oriented commands.

**Debugging an RPC failure.** Set `WSLCC_DEBUG=1` to print the full exception (including a gRPC status and stack trace) when a compose command's call to `wslccd` fails, instead of just the friendly one-line message.

## ENVIRONMENT VARIABLES

| Variable | Read by | Effect |
| --- | --- | --- |
| `NO_COLOR` | every command | Same as passing [`--no-color`](#global-options); either one disables color. |
| `COMPOSE_FILE` | compose commands | Compose file(s) to load when no `-f` is given — see [File discovery](#compose-file-and-project-options). |
| `COMPOSE_PATH_SEPARATOR` | compose commands | Separator used to split `COMPOSE_FILE` into multiple paths. Defaults to `;` on Windows. |
| `COMPOSE_PROFILES` | compose commands | Comma-separated profiles to activate, unioned with `--profile` — see [compose-file.md#profiles](compose-file.md#profiles). |
| `WSLCCD_PATH` | `daemon start`, `daemon install` (fallback) | Explicit path to `wslccd(.exe)`, checked before the default "next to `wslcc.exe`" search. |
| `WSLCC_DEBUG` | compose commands | Set to `1` to print full exception detail on an RPC failure instead of a friendly summary. |

---

## COMING FROM DOCKER COMPOSE

If you already know `docker compose`, this table is a quick lookup — but the sections above are the actual reference; use them for anything beyond "what's the command called".

| Docker Compose | wslcc | Notable difference |
| --- | --- | --- |
| `docker compose version` | `wslcc compose version` | — |
| `docker compose up` | `wslcc compose up` | **Attached by default** here (Compose defaults to detached); `-d`/`--detach` for the Compose-like behavior. |
| `docker compose down` | `wslcc compose down` | — |
| `docker compose ps` | `wslcc compose ps` | With no `-f`/`-p`, lists containers across **all** projects, not just the one in the current directory. |
| `docker compose start` / `stop` / `restart` | `wslcc compose start` / `stop` / `restart` | Unknown `[SERVICES]` names are a hard error here, not silently ignored. |
| `docker compose pull` / `build` | `wslcc compose pull` / `build` | — |
| `docker compose logs` | `wslcc compose logs` | Non-`--follow` output is sorted by timestamp across containers, not grouped container-by-container. |
| `docker compose config` | `wslcc compose config` | `--resolve-image-digests` isn't implemented (runs offline); the `--hash` digest is WSLCC's own, not Compose's. |
| `--host` / `-H`, `--context` | `--wslcc-host` (compose commands) / `-H, --host` (daemon, version) | Renamed on compose commands specifically to avoid clashing with a real Compose option. |
| n/a | `--wslcc-provider` (compose) / `--provider` (`daemon start`/`install`) | WSLCC-specific: which backend (`wslc` or `docker`) executes the command. |
| n/a | `wslcc daemon ...` | WSLCC-specific: manage the background `wslccd` process itself. |

The full, honest list of where the underlying compose *file* support falls short of Docker Compose's own (rather than just being renamed or reordered) is in [compose-file.md#known-limitations](compose-file.md#known-limitations).

## SEE ALSO

[compose-file.md](compose-file.md) (compose file format reference), [daemon.md](daemon.md) (the `wslccd` background process), [providers.md](providers.md) (the `wslc`/`docker` backends), [architecture.md](architecture.md), [roadmap.md](roadmap.md), [todo.md](todo.md).
