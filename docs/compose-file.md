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
  `ports`, `volumes`, `depends_on` (list or map), `networks` (list or map), `labels`, `restart`,
  `working_dir`, `user`.
- Top-level `name`, `networks`, `volumes` (with `driver`, `external`).

Unknown keys are ignored rather than causing a failure.

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
  no default resolves to an empty string and prints a warning.
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

## Not yet covered

Full Compose specification fidelity is still planned (see [todo.md](todo.md)), including:
`configs`/`secrets`, healthchecks, and `deploy` settings. Ports and volumes are currently kept as raw
strings rather than fully structured objects.

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
