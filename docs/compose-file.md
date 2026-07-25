# Compose file support

WSLCC parses the same YAML that Docker Compose uses. Resolution happens in two stages:

1. **Client-side resolution** (`Wslcc.Compose.ComposeLoader`, run by the CLI): merges multiple files,
   loads `.env`, interpolates `${VAR}` references, resolves `extends`, and filters by profile — then
   emits a single resolved document. See [Resolution features](#resolution-features).
2. **Daemon-side parsing** (`Wslcc.Compose.ComposeFileParser`): parses the already-resolved
   document into the model in `Wslcc.Abstractions.Compose`.

Both stages live in the single `Wslcc.Compose` library — there is one YAML parser, shared by client
and daemon.

## Current status

The parser is intentionally **tolerant** and understands the common short/long forms of the
most-used keys:

- `services.<name>`: `image`, `build` (string shorthand or `{ context, dockerfile, target, args }`),
  `container_name`, `command`, `entrypoint`, `environment` (list `KEY=VALUE` or map), `env_file`,
  `ports`, `volumes`, `depends_on` (list or map, with `condition` / `required`), `healthcheck`
  (`test`, `interval`, `timeout`, `retries`, `start_period`, `disable`), `networks` (list or map),
  `labels`, `restart`, `working_dir`, `user`.
- Top-level `name`, `networks`, `volumes` (with `driver`, `external`).

Unknown keys are ignored rather than causing a failure.

### Startup order and health

`up` starts services in `depends_on` order and honors the long-form **conditions**:

- `service_started` (the default, and what the short list form means) — only orders startup.
- `service_healthy` — waits until the dependency's healthcheck passes. The dependency must have a
  healthcheck, either a `healthcheck:` in the compose file (WSLCC applies it to the container via the
  runtime's `--health-*` flags) or one baked into its image; otherwise the dependent fails.
- `service_completed_successfully` — waits until the dependency exits with code `0`.

A service whose required dependency fails to start, reports unhealthy, or exits non-zero is not started
(and is reported as failed, along with its own dependents). `healthcheck: { disable: true }` (or
`test: ["NONE"]`) turns the healthcheck off.

## Resolution features

The client resolver supports:

- **Multiple files** — repeat `-f` (e.g. `-f compose.yaml -f compose.override.yaml`), or set
  `COMPOSE_FILE` (paths separated by `COMPOSE_PATH_SEPARATOR`, defaulting to `;` on Windows). Files are
  merged left-to-right following the Compose merge rules: mappings merge by key (including
  `environment` / `labels` written as `KEY=VALUE` lists), most sequences are **appended** (with
  exact-duplicate entries dropped), `command` / `entrypoint` are replaced, and scalars are replaced.
- **`.env` + variable interpolation** — `${VAR}`, `$VAR`, `${VAR:-default}` / `${VAR-default}`,
  `${VAR:?error}` / `${VAR?error}`, `${VAR:+alternate}` / `${VAR+alternate}`, and `$$` for a literal
  `$`. Values come from the process environment overlaid on a `.env` file (process environment wins).
  `.env` is read from the project directory unless `--env-file <path>` is given. An unset variable with
  no default resolves to an empty string and prints a warning. The `.env` reader itself understands an
  optional leading `export`, whole-line and inline (` #`) comments, single-quoted values (literal),
  double-quoted values (C-style escapes `\n` `\t` `\r` `\f` `\b` `\v` `\"` `\\`), quoted values that
  span multiple lines, and in-value `${VAR}` expansion (referencing variables set earlier in the file,
  then the process environment).
- **`extends`** — `extends: base`, `extends: { service: base }`, or
  `extends: { file: other.yaml, service: base }`. The base is resolved first (chains work) and the
  child is merged over it using the same per-attribute rules; `file` paths are relative to the file
  declaring the `extends`. Cycles are rejected, and extending a service that declares a
  service-referencing attribute (`depends_on`, `volumes_from`, `links`, or `network_mode` / `ipc` /
  `pid` / `uts` set to `service:` / `container:`) is an error, matching Compose.
- **`profiles`** — a service with no `profiles:` is always enabled; one that lists profiles is enabled
  only when a listed profile is active. Profiles are activated via `--profile <name>` (repeatable),
  `COMPOSE_PROFILES`, or by naming a service that carries the profile on the command line (e.g.
  `wslcc compose build debugger` activates `debugger`'s profiles). Disabled services are removed and
  `depends_on` references to them are pruned.

The **project directory** (used for the default `.env` location, relative `build.context` resolution,
and the default project name) is the first compose file's directory, or `--project-directory <path>`
when given.

### Inspecting the resolved document

`wslcc compose config` runs the resolution above and prints the result — the exact document the other
verbs send to the daemon, plus the effective project name as a leading `name:` — without contacting
`wslccd`. It is the quickest way to see how your `-f` files, `.env`, `${VAR}` references, `extends`, and
profiles combine:

```console
$ wslcc compose config                  # print the fully-resolved YAML
$ wslcc compose config --format json     # ...as JSON instead
$ wslcc compose config --services        # just the enabled service names
$ wslcc compose config --profiles        # every declared profile (before filtering)
$ wslcc compose config --profile debug   # resolution with the 'debug' profile active
$ wslcc compose config --no-interpolate  # leave ${VAR} references verbatim
$ wslcc compose config --hash "*"        # a per-service config hash (change detection)
$ wslcc compose config -q                # validate only (no output; non-zero exit on error)
```

The document goes to `stdout` (or `-o <path>`); warnings go to `stderr`, so `wslcc compose config > resolved.yaml`
captures a clean file. `--hash` emits a wslcc-specific SHA-256 of each service's canonical config (not
Docker Compose's own hash); `--resolve-image-digests` is not implemented because `config` runs offline.

## Not yet covered

Full Compose specification fidelity is still planned (see [todo.md](todo.md)), including:
`configs`/`secrets` and `deploy` settings. Ports and volumes are currently kept as raw strings rather
than fully structured objects.

## Example

```yaml
name: sample
services:
  web:
    image: nginx:1.27
    ports:
      - "8080:80"
    depends_on:
      - redis
  redis:
    image: redis:7
```

See [../examples](../examples) for runnable samples.
