# Compatibility with Docker Compose

What breaks, what differs, and what is simply missing when you point `wslcc` at an existing Compose project. This is the migration checklist; per-key detail lives in [compose-file.md](compose-file.md), and the command-name cheat sheet is in [cli-mapping.md#coming-from-docker-compose](cli-mapping.md#coming-from-docker-compose).

WSLCC aims to *feel* like `docker compose`, but it is a separate orchestrator. Even with `--wslcc-provider docker`, it does **not** call the Docker Compose plugin for lifecycle — it drives the plain `docker` CLI and applies its own labels and rules.

**Suggested first step:** run `wslcc compose config` on your project and read the resolved document (and any warnings). Then skim the tables below against keys you actually use.

---

## Interop (projects do not share a world)

| Topic | Docker Compose | WSLCC |
| --- | --- | --- |
| Project labels | `com.docker.compose.project` / `.service` | `wslcc.project` / `wslcc.service` / `wslcc.config-hash` |
| Visibility | `docker compose ps` sees Compose projects | `wslcc compose ps` sees only WSLCC-managed containers |
| Cross-tool teardown | `docker compose down` manages its own stack | Does **not** remove WSLCC containers; WSLCC `down` does not remove Compose stacks |
| Backend | Docker Compose plugin | `wslc` or plain `docker` CLI + WSLCC engine ([providers.md](providers.md)) |

**Implication:** you cannot incrementally “hand off” a running Compose stack to WSLCC (or the reverse) and expect `ps`/`down` to agree. Treat a move as a recreate under WSLCC, or keep the tools on separate projects.

---

## CLI differences that surprise migrants

| Behavior | Docker Compose | WSLCC |
| --- | --- | --- |
| `up` / `down` service list | `compose up web`, `compose down web` | **No `[SERVICES]`** — always the whole project |
| `up` attach vs detach | Detached by default | **Attached by default**; use `-d` / `--detach` |
| Unknown service names | Often ignored | Hard error (`no such service: …`) on `start`/`stop`/`restart`/`pull`/`build`/`logs` |
| Host / remote | `--host` / `-H`, contexts | Compose commands: `--wslcc-host`; daemon/`version`: `-H`/`--host` |
| Backend selection | n/a | `--wslcc-provider wslc\|docker` (or daemon `--provider`) |
| Daemon | Optional (engine is always there) | **`wslccd` must be running** (`wslcc daemon start`) |
| Option position | Many global flags before the verb | Options belong on the **leaf** command (`wslcc compose up --project-directory …`, not `compose --project-directory … up`) — see [troubleshooting.md](troubleshooting.md#no-compose-file-found) |
| `config --hash` | Compose’s own digest | WSLCC-specific SHA-256 (for change detection only) |
| `config --resolve-image-digests` | Supported | **Not implemented** |
| Missing Compose verbs | `exec`, `run`, `cp`, `top`, `events`, `pause`, `unpause`, `kill`, `rm`, `port`, `images`, `push`, `create`, `watch`, … | **Not implemented** |
| Common `up` flags | `--force-recreate`, `--no-recreate`, `--remove-orphans`, `--wait`, `--scale`, `--abort-on-container-exit`, `--timeout`, … | **Not implemented** (change detection replaces some recreate cases) |

Full command mapping: [cli-mapping.md#coming-from-docker-compose](cli-mapping.md#coming-from-docker-compose).

---

## File resolution differences

| Topic | Docker Compose | WSLCC |
| --- | --- | --- |
| Default `.env` | Project directory (directory of the first compose file / `--project-directory`) | **Current working directory** unless `--project-directory` or `--env-file` is set. `wslcc compose up -f apps/web/compose.yaml` from the repo root reads `./.env`, not `apps/web/.env`. |
| Auto `compose.override.yaml` | Often merged automatically when present | **Not** auto-merged — pass `-f compose.yaml -f compose.override.yaml` (or `COMPOSE_FILE`) explicitly |
| Default file discovery | `compose.yaml` / `compose.yml` / `docker-compose.yaml` / `docker-compose.yml` | Same names, first match only (no automatic override pair) |
| Interpolation | Process env overlays `.env` | Same idea; grammar documented in [compose-file.md#resolution-features](compose-file.md#resolution-features) |
| Profiles | List or string shorthand | **List form only** — `profiles: debug` (scalar) is treated as “no profiles” (service always on). Use `profiles: ["debug"]`. |
| Profiled dependencies | Spec/tooling nuances | Disabled services are removed and `depends_on` refs pruned; dependencies are **not** auto-activated |

---

## Compose keys that look valid but do not work as in Compose

These are the ones that bite when you reuse an existing file: WSLCC may accept the YAML (or a close subset) while runtime behavior diverges.

### Parsed but not applied (silent — no error)

You get no failure; the key is simply ignored at container create time. Prefer rewriting or verifying with `compose config` + a real `up`.

| Key | Compose expectation | WSLCC today |
| --- | --- | --- |
| `container_name` | Fixed container name | Always `<project>-<service>` |
| `user` | Run as that user | Image default user |
| `working_dir` | Working directory | Image default |
| `entrypoint` | Override entrypoint | Image entrypoint unchanged |
| `labels` (service) | Container labels | Only WSLCC’s own labels are set |
| `env_file` | Load vars into the container | **Not read** into the container (use `environment:`; `--env-file`/`.env` only affect *interpolation*) |

Full table: [compose-file.md#service-reference](compose-file.md#service-reference). Tracked in [todo.md](todo.md).

### Applied with different semantics

| Key / form | Compose | WSLCC |
| --- | --- | --- |
| `command: "npm start"` (string) | Shell form: `/bin/sh -c "…"` | **Single argv token** — almost always wrong if the string has spaces. Use list form: `["npm", "start"]`. |
| `environment: [FOO]` / bare `FOO:` | Inherit from the **client** shell / project env | Passed as `-e FOO` to the runtime → inherits from **`wslccd`’s** process environment, not your shell |
| `ports` / `volumes` long map form | Supported | **Rejected** with an error (short syntax only) |
| `networks:` map values (`aliases`, `ipv4_address`, …) | Applied | Only membership (keys) applied; map values ignored |
| Top-level `networks` / `volumes` | `driver`, `external`, `name`, IPAM, `driver_opts`, … | Only `driver` and `external` modeled; no resource `name:` override, no IPAM |

### Not read at all (silently dropped)

Including but not limited to: top-level and service `configs`, `secrets`, `deploy`, and every other service key not listed in the [service reference](compose-file.md#service-reference) (`privileged`, `cap_add`, `cap_drop`, `read_only`, `devices`, `tmpfs`, `ulimits`, `extra_hosts`, `dns`, `hostname`, `domainname`, `shm_size`, `pids_limit`, `init`, `stdin_open`, `tty`, `network_mode`, `pid`, `ipc`, `uts`, `stop_grace_period`, `stop_signal`, `expose`, `volumes_from`, `links`, `scale` / deploy replicas, …).

**Security note:** keys like `user:`, `read_only:`, `privileged:`, and `cap_drop:` can look like they harden a container. If they are only “not read” or “parsed only”, they have **no effect** — do not assume the YAML you trusted under Compose still enforces those constraints under WSLCC.

---

## Runtime / orchestration differences

| Topic | Docker Compose | WSLCC |
| --- | --- | --- |
| Dependency condition wait | Tool-defined | Caps at **5 minutes**, then fails the dependent ([troubleshooting](troubleshooting.md#up-seems-hung-for-minutes)) |
| Unknown `depends_on.condition` | Rejected | Silently treated as `service_started` |
| `depends_on` + `required: false` | Optional dependency | Intended to match Compose; see [compose-file.md](compose-file.md#startup-order-and-health) |
| Change detection | Recreate heuristics / flags | Config-hash on **running** containers; stopped-but-unchanged containers are recreated |
| Config hash | Compose’s | WSLCC’s own; not interchangeable |
| Named volumes / networks | Compose naming + labels | `<project>_<name>`, `<project>_default`; WSLCC labels for teardown |
| `external: true` | Must exist; not created/removed | Same intent |
| Build tagging | Compose conventions | `<project>-<service>` unless `image:` is set |
| Healthcheck `test` exec form | Distinct CMD vs CMD-SHELL | Flattened to a shell `--health-cmd` string |

---

## Merge / `extends` fidelity gaps

These matter if you rely on multi-file overrides or `extends` the way Compose documents them:

| Topic | Compose | WSLCC today |
| --- | --- | --- |
| Sequence merge for long-form ports/volumes | Unique-key merge by target | N/A (long form rejected); short-form lists append + exact-dedup |
| `build: .` merged with `build: { dockerfile: … }` | Becomes `{ context: ., dockerfile: … }` | Override can **drop** the string-side context (map wins wholesale when types differ) |
| Long-form `depends_on` field merge | Per-dependency fields merge | Overriding one field (e.g. `required`) can **replace** the whole dependency object and lose `condition` |
| `extends` non-inheritable keys | e.g. `container_name` not inherited | Several keys may still merge from the base (and then still not apply at runtime — see above) |

Details and tracking: [compose-file.md#resolution-features](compose-file.md#resolution-features), [todo.md](todo.md).

---

## Practical migration checklist

1. Install WSLCC, start the daemon: `wslcc daemon start` (watch elevation — [troubleshooting](troubleshooting.md#daemon-not-reachable)).
2. Pick a provider: default `wslc`, or `wslcc daemon start --provider docker`.
3. From the project directory (so `.env` resolves as you expect), run:
   ```powershell
   wslcc compose config
   wslcc compose config --hash "*"
   ```
4. Search your compose files for: string `command:`, `env_file:`, `container_name:`, `user:`, `working_dir:`, `entrypoint:`, service `labels:`, long-form `ports`/`volumes`, `configs`/`secrets`/`deploy`, `privileged` / `cap_*` / `read_only`, and bare `environment` keys you expect from your shell.
5. Rewrite string commands to lists; fold `env_file` into `environment`; pass `-f` for overrides explicitly; use `--project-directory` if you invoke from another cwd.
6. Bring the stack up under WSLCC (`up -d`), verify with `wslcc compose ps` — not `docker compose ps`.
7. Tear down with `wslcc compose down` (add `-v` only if you intend to delete named volumes).

---

## See also

- [cli-mapping.md](cli-mapping.md) — full CLI reference and short Compose→wslcc command table  
- [compose-file.md](compose-file.md) — every supported key and “Applied?” column  
- [providers.md](providers.md) — `wslc` vs `docker` backends  
- [troubleshooting.md](troubleshooting.md) — first-run failures  
- [todo.md](todo.md) — intentional fidelity backlog  
