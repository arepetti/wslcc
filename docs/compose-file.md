# Compose file reference

This is a self-contained reference for the Compose YAML that WSLCC understands — every key it reads, the exact syntax accepted for each, and what happens to it at runtime. You should not need Docker's own Compose file reference to use `wslcc`; where WSLCC's behavior differs from `docker compose` (a narrower key set, a syntax it doesn't accept, or a key it reads but doesn't yet act on) it's called out explicitly below rather than left to be discovered by trial and error.

For the commands that consume these files (`-f`, `-p`, `--profile`, ...), see [cli-mapping.md](cli-mapping.md).

## How a file is resolved

Resolution happens in two stages, both inside the single `Wslcc.Compose` library (one YAML parser, shared by client and daemon):

1. **Client-side resolution** (`ComposeLoader`, run by the CLI): merges multiple `-f` files, loads `.env`, interpolates `${VAR}` references, resolves `extends`, and filters services by profile — then re-serializes a single resolved document. See [Resolution features](#resolution-features).
2. **Daemon-side parsing** (`ComposeFileParser`): parses that already-resolved document into the model described in [Service reference](#service-reference) below. This is the parser that decides which keys exist and what they do; profiles and `extends` are already gone by this point.

Because resolution happens before the daemon ever sees the file, `wslcc compose config` (which runs client-side resolution and stops) shows you *exactly* what every other command will act on.

## Top-level keys

| Key | Notes |
| --- | --- |
| `name` | Project name. See [Project name](cli-mapping.md#project-name-and-connection) for how it combines with `-p`/`--project-name` and the directory name. |
| `services` | Required for anything to happen. See [Service reference](#service-reference). |
| `networks` | Named networks to create. See [Networks and volumes](#networks-and-volumes). |
| `volumes` | Named volumes to create. See [Networks and volumes](#networks-and-volumes). |

Any other top-level key (`configs`, `secrets`, `x-*` extensions, ...) is ignored rather than rejected — unknown keys never cause a parse failure, at any level of the document.

## Service reference

Each entry under `services:` accepts the keys below. **Applied** means the key changes what actually runs; a few keys are recognized in the YAML (so referencing them isn't an error) but have no effect yet — those are marked **parsed only**, with a workaround where one exists.

| Key | Syntax | Applied? | Notes |
| --- | --- | --- | --- |
| `image` | string | ✅ | Used as-is. Not required if `build:` is present (see [`up` auto-build](cli-mapping.md#wslcc-compose-up)). |
| `build` | string, or map (`context`, `dockerfile`, `target`, `args`) | ✅ | See [Build](#build). |
| `command` | list, or a single string | ✅ (list); ⚠️ (string) | See [Command and entrypoint](#command-and-entrypoint) — the string short form is **not** shell-split. |
| `entrypoint` | list, or a single string | ❌ parsed only | Recognized but never overrides the image's entrypoint. |
| `environment` | map, or list of `KEY=VALUE` / bare `KEY` | ✅ | See [Environment](#environment). |
| `env_file` | string, or list of strings | ❌ parsed only | Recognized but its files are never read into the container. Put the same variables under `environment:` instead, or use `--env-file`/`.env` for *interpolation* (a different thing — see [Resolution features](#resolution-features)). |
| `ports` | list of `"[HOST:]CONTAINER[/PROTO]"` | ✅ | Short syntax only. Passed through to the runtime unchanged; the long map form (`target:`/`published:`/`protocol:`) is not read. |
| `volumes` | list of `[SOURCE:]TARGET[:MODE]` | ✅ | Short syntax only. See [Networks and volumes](#networks-and-volumes). |
| `depends_on` | list of names, or map of `name: { condition, required }` | ✅ | See [Startup order and health](#startup-order-and-health). |
| `healthcheck` | map (`test`, `interval`, `timeout`, `retries`, `start_period`, `disable`) | ✅ | See [Healthchecks](#healthchecks). |
| `networks` | list of names, or map of `name: {...}` | ✅ (membership); ⚠️ (map values) | See [Networks and volumes](#networks-and-volumes) — only the *keys* of the map form are read; per-network `aliases`/`ipv4_address` etc. are not. |
| `labels` | map, or list of `KEY=VALUE` | ❌ parsed only | Recognized but never applied as container labels (WSLCC's own `wslcc.project`/`wslcc.service`/`wslcc.config-hash` labels are applied regardless). |
| `restart` | string (`no`, `always`, `on-failure`, `on-failure:N`, `unless-stopped`) | ✅ | Passed through unchanged to the runtime; not validated by WSLCC. |
| `container_name` | string | ❌ parsed only | The container is always named `<project>-<service>` (see [Project name](cli-mapping.md#project-name-and-connection)); an explicit `container_name:` is ignored. |
| `working_dir` | string | ❌ parsed only | Recognized but the image's own working directory is always used. |
| `user` | string | ❌ parsed only | Recognized but the image's own user is always used. |
| `profiles` | list of strings | n/a | Consumed entirely during client-side resolution (see [Profiles](#profiles)); the daemon never sees this key. |
| `extends` | string, or map (`service`, `file`) | n/a | Consumed entirely during client-side resolution (see [`extends`](#extends)); the daemon never sees this key. |

`configs:`, `secrets:`, `deploy:`, and every other service key are not read at all.

### Build

```yaml
services:
  web:
    build: .                 # shorthand: context only
  api:
    build:
      context: ./api
      dockerfile: Dockerfile.prod
      target: runtime
      args:
        NODE_ENV: production
```

A relative `context` resolves against the compose file's directory (sent to the daemon as `base_directory`, so `wslcc compose build`/`up` works even though `wslccd` is a separate long-running process with its own current directory). `wslcc compose build` tags the result `<project>-<service>` unless the service also sets `image:`, in which case that name is used instead.

### Command and entrypoint

```yaml
services:
  worker:
    command: ["npm", "run", "worker"]   # exec form — each element becomes one argv token
  legacy:
    command: npm start                  # string short form — see caveat below
```

The **list form** is applied token-by-token, exactly like Compose's exec form.

The **string short form** is *not* split into words or wrapped in a shell the way `docker compose` does (Compose runs a string `command:` as `/bin/sh -c "<string>"`). WSLCC instead passes the whole string as a single argv element, which almost always fails for anything with arguments (the runtime tries to exec a binary literally named `npm start`, space included). Use the list form for any command that takes arguments; the string form only works for a bare, argument-less executable name.

`entrypoint:` is parsed (both forms) but never applied — see the table above.

### Environment

```yaml
services:
  api:
    environment:
      NODE_ENV: production
      DEBUG:                # or `- DEBUG` in list form — bare key, no value
  worker:
    environment:
      - NODE_ENV=production
      - DEBUG                # same bare-key meaning as above
```

Map form and list form (`KEY=VALUE`) are equivalent. A **bare key** (no `=`, or an explicit YAML `null` in map form) passes the value through from `wslccd`'s own process environment at container-start time — the same meaning as `docker run -e KEY` — rather than from the compose file's `.env`/interpolation environment. If `wslccd` doesn't have that variable set, the container simply doesn't get it.

`env_file:` is parsed but its files are never loaded into the container (see the table above); list every variable directly under `environment:` instead.

### Startup order and health

`up`, and the ordering used by `start`/`stop`/`restart`/`logs`/`down`, follow `depends_on`:

```yaml
services:
  api:
    depends_on:
      db:
        condition: service_healthy
      migrations:
        condition: service_completed_successfully
      cache:
        condition: service_started   # the default — just orders startup
      optional-metrics:
        condition: service_started
        required: false
  db:
    image: postgres:16
  cache:
    image: redis:7
```

The short list form (`depends_on: [db, cache]`) is equivalent to the long form with every entry at the default `service_started` condition and `required: true`.

- **`service_started`** (default) — only guarantees the dependency was started first.
- **`service_healthy`** — waits for the dependency's healthcheck to report healthy. The dependency *must* have a healthcheck (from `healthcheck:` below, or one baked into its image); otherwise `up` fails the dependent with a clear error rather than waiting forever.
- **`service_completed_successfully`** — waits for the dependency's container to exit with code `0`.

A service whose **required** dependency fails to start, never becomes healthy, or exits non-zero is not started, and is reported as failed — which in turn fails *its* own dependents. `required: false` (or a map entry with no such field, since the default is `true`) marks the dependency as optional: a failure doesn't block the dependent.

`start`, `restart`, and `logs` process services in dependency order (dependencies first); `stop` and `down` use the reverse order (dependents first). Ordering needs the compose file — if a command is given only `-p <project>` (no `-f`), the daemon has no dependency graph for that project and falls back to listing order.

### Healthchecks

```yaml
services:
  db:
    image: postgres:16
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 3s
      retries: 5
      start_period: 10s
  no-check:
    image: some/image
    healthcheck:
      disable: true
```

`test` accepts the same three Compose forms — `["CMD-SHELL", "<shell command>"]`, `["CMD", "arg", ...]` (both run through a shell), or a single string short form — and is applied to the container via the runtime's `--health-*` flags. `disable: true`, or `test: ["NONE"]`, turns the healthcheck off (`--no-healthcheck`) even if the image itself declares one. `interval`/`timeout`/`start_period` are Compose duration strings (e.g. `"30s"`, `"1m30s"`) passed straight through; `retries` is an integer.

A healthcheck here is what makes `depends_on: { condition: service_healthy }` work for this service — without one (here or baked into the image), a dependent waiting on `service_healthy` fails immediately with a clear error instead of hanging.

### Networks and volumes

`up` provisions the project's declared `networks:` and named `volumes:` before starting any container, and attaches/mounts each service accordingly; `down` cleans them up afterward.

**Top-level `networks:` / `volumes:`**

```yaml
networks:
  frontend:
  backend:
    driver: bridge
  shared:
    external: true    # must already exist; never created or removed

volumes:
  db-data:
  cache:
    driver: local
  shared-data:
    external: true
```

Only `driver` and `external` are read (an explicit resource `name:` override is not yet supported — see [todo.md](todo.md)). Each declared network is created as `<project>_<name>` (Compose's own naming convention) and labelled with the project so it can be found again later; likewise each named volume is created as `<project>_<name>`. `external: true` resources are assumed to already exist under their *bare* name — WSLCC never creates or removes them.

**Service-level `networks:`**

```yaml
services:
  web:
    networks: [frontend, backend]        # list form
  api:
    networks:                            # map form — only the keys are read
      backend: {}
      frontend:
        aliases: [api-alt]               # not read; the service name is always the alias
```

A service that attaches to a network is published on it under its **service name** as a network alias, so other services on the same network can reach it by that name — this is fixed and not configurable (the map form's `aliases:` is parsed as part of the network's config value but ignored). A service on more than one network is created on the first and connected to the rest afterward. A service with no `networks:` at all joins an implicit `<project>_default` network created for every project, so a minimal compose file with no `networks:` section still gets working service-name DNS between its services.

**Service-level `volumes:`** (short syntax only: `[SOURCE:]TARGET[:MODE]`)

```yaml
services:
  db:
    volumes:
      - db-data:/var/lib/postgresql/data   # SOURCE matches a declared volume -> named volume
      - ./config:/etc/app:ro               # SOURCE is a path -> bind mount (relative to the project directory)
      - /var/log                           # bare TARGET -> anonymous volume
volumes:
  db-data:
```

- A `SOURCE` that matches a name declared under the top-level `volumes:` is rewritten to that volume's project-prefixed name (`<project>_db-data` in the example above).
- A `SOURCE` that looks like a path — absolute, `./relative`, `../relative`, `~`, or a Windows drive letter (`C:\...`) — is a **bind mount**; relative paths resolve against the project directory. `external: true` volumes are referenced by their bare name here too.
- A bare `TARGET` with no `SOURCE` is an **anonymous volume**.
- The long map form (`type: volume`/`bind`, `source:`, `target:`, ...) is not supported.

**Teardown.** `down` removes the project's networks once its containers are gone. Named volumes are **kept** by default — pass `down --volumes`/`-v` to remove them too. `external: true` resources are never touched by `down` either way.

Only the common attributes above are modeled; richer per-network settings (`ipam`, static `ipv4_address`, subnets) and extra volume `driver_opts` are not — see [todo.md](todo.md).

### Profiles

```yaml
services:
  app:
    image: myapp:latest
  debugger:
    image: myapp:latest
    command: ["myapp", "--debug"]
    profiles: ["debug"]
```

A service with no `profiles:` is always enabled. A service that lists profiles is only enabled when one of them is **active**. Profiles are activated by, in combination:

- `--profile <name>` on the command line (repeatable),
- the `COMPOSE_PROFILES` environment variable (comma-separated),
- or naming a profiled service explicitly on the command line (e.g. `wslcc compose build debugger` activates `debugger`'s `debug` profile even without `--profile debug`) — this mirrors `docker compose`.

Filtering happens entirely on the client: disabled services are removed from the document before it's sent anywhere, and any `depends_on` reference to a disabled service is pruned along with it. `--profiles` on `wslcc compose config` reports every profile declared anywhere in the file, *before* this filtering — useful for discovering what's available.

### `extends`

```yaml
# base.yaml
services:
  base-app:
    image: myapp:latest
    environment:
      LOG_LEVEL: info

# compose.yaml
services:
  app:
    extends:
      file: base.yaml
      service: base-app
    environment:
      LOG_LEVEL: debug   # merged over the base per-attribute (see Resolution features)
```

`extends: base-app` (bare string) and `extends: { service: base-app }` both extend a service in the *same* file; `extends: { file: other.yaml, service: base-app }` extends one in another file, with `file` resolved relative to the file that declares the `extends`. Chains (`A extends B extends C`) are supported; cycles are rejected.

The base is resolved first, then the child is merged over it using the same per-attribute rules as multi-file merge (see [Resolution features](#resolution-features)) — so `environment:`/`labels:` merge by key, most lists append, and scalars are replaced. Extending a service that declares a service-referencing attribute is rejected, matching `docker compose`: `depends_on`, `volumes_from`, `links`, or `network_mode`/`ipc`/`pid`/`uts` set to `service:...`/`container:...`.

## Resolution features

The client-side resolver (`ComposeLoader`, driven by the CLI options in [cli-mapping.md](cli-mapping.md#compose-file-and-project-options)) combines everything above:

- **Multiple files** — repeat `-f` (e.g. `-f compose.yaml -f compose.override.yaml`), or set `COMPOSE_FILE` (paths separated by `COMPOSE_PATH_SEPARATOR`, defaulting to the OS path separator — `;` on Windows). When neither is given, the first of `compose.yaml`, `compose.yml`, `docker-compose.yaml`, `docker-compose.yml` found in the project directory is used. Files merge left-to-right: mapping attributes merge by key (`environment`/`labels`/`annotations`/`sysctls`, whether written as a map or a `KEY=VALUE` list, all merge this way too), `depends_on`/`networks` merge by key when either side uses the map form and otherwise append, most other sequences are **appended** (exact duplicate entries — compared after serialization — are dropped), `command`/`entrypoint` are replaced wholesale (a command line is one value, not a list to merge), and plain scalars are replaced.
- **`.env` + variable interpolation** — `${VAR}`, `$VAR`, `${VAR:-default}` / `${VAR-default}`, `${VAR:?error}` / `${VAR?error}`, `${VAR:+alternate}` / `${VAR+alternate}`, and `$$` for a literal `$`. Values come from the process environment overlaid on a `.env` file (process environment wins). `.env` is read from the project directory unless `--env-file <path>` is given. An unset variable with no default resolves to an empty string and prints a warning. The `.env` reader itself understands an optional leading `export`, whole-line and inline (` #`) comments, single-quoted values (literal), double-quoted values (C-style escapes `\n` `\t` `\r` `\f` `\b` `\v` `\"` `\\`), quoted values that span multiple lines, and in-value `${VAR}` expansion (referencing variables set earlier in the file, then the process environment). `--no-interpolate` leaves every `${VAR}` reference verbatim while still merging files, resolving `extends`, and filtering profiles.
- **`extends`** — see [`extends`](#extends) above.
- **`profiles`** — see [Profiles](#profiles) above.

The **project directory** (used for the default `.env` location, relative `build.context` resolution, relative bind-mount sources, and the default project name) is the first compose file's directory, or `--project-directory <path>` when given.

### Inspecting the resolved document

`wslcc compose config` runs the resolution above and prints the result — the exact document every other command sends to `wslccd`, plus the effective project name as a leading `name:` — without contacting the daemon at all:

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

The document goes to `stdout` (or `-o <path>`); warnings go to `stderr`, so `wslcc compose config > resolved.yaml` captures a clean file. `--hash` emits a WSLCC-specific SHA-256 of each service's canonical config (used by `up`'s change detection — see below), not Docker Compose's own hash; `--resolve-image-digests` is not implemented because `config` runs offline. Full flag reference: [cli-mapping.md](cli-mapping.md#wslcc-compose-config).

## Change detection (`up`)

`up` recreates a container only when it needs to. Every container is stamped with a `wslcc.config-hash` label — the same per-service hash `config --hash` reports, computed from the service's canonicalized, fully-resolved configuration. On a later `up`, a service whose container is still **running** with a matching hash is left in place (reported as `running`) instead of being recreated. A changed hash, a stopped or absent container, or passing `--pull`/`--build` (which fetch fresh content and so always recreate) all trigger recreation.

## Known limitations

Beyond the parsed-but-not-applied service keys called out in the [reference table](#service-reference) above, WSLCC's Compose support has these gaps (tracked in [todo.md](todo.md)):

- `configs:`, `secrets:`, and `deploy:` are not read at all.
- `ports:` and `volumes:` are kept and matched as short-syntax strings rather than fully structured objects; the long map forms are not supported for either.
- Multi-file/`extends` merge appends and de-duplicates *exact* duplicate sequence entries; it does not model Compose's per-resource unique-key merge for the long forms of `ports`/`volumes` (moot today, since those long forms aren't parsed anyway).
- `networks:`/`volumes:` model only `driver` and `external`; IPAM/static addresses, extra volume `driver_opts`, and an explicit resource `name:` override are not read.

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
