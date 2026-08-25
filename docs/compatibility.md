# Compatibility with Docker Compose

What WSLCC supports, what differs, and what is simply missing when you point `wslcc` at an existing Compose project.

This document is the **single source of truth for support status**. If the question is “does WSLCC honor this key?”, the answer is in the tables below. If the question is “what does this YAML mean?”, the answer is in [compose-file.md](compose-file.md), which describes the file format itself and carries no status marks. The command-name cheat sheet is in [cli-mapping.md#coming-from-docker-compose](cli-mapping.md#coming-from-docker-compose).

WSLCC aims to *feel* like `docker compose`, but it is a separate orchestrator. Even with `--wslcc-provider docker`, it does **not** call the Docker Compose plugin for lifecycle — it drives the plain `docker` CLI and applies its own labels and rules.

**Suggested first step:** run `wslcc compose config` on your project and read the resolved document (and any warnings). Then skim the tables below against the keys you actually use.

---

## Support status legend

| Mark | Meaning |
| --- | --- |
| ✅ **Applied** | Parsed and honored at runtime (or fully honored during client-side resolution). |
| ⚠️ **Partial** | Parsed, but only a subset of the Compose semantics is honored, or one syntax form is refused. |
| 🧩 **Client-only** | Consumed during client-side resolution; the daemon never sees the key. |
| 🛑 **Rejected** | Recognized syntax that fails the load with a clear error rather than being ignored. |
| ❌ **Not read** | Absent from WSLCC's model. Ignored if present, with no error. **Do not assume security, resource, or networking keys take effect.** |

Two rules that follow from the legend:

- Unknown keys — at any level, including keys WSLCC does not model — never fail the parse by themselves. They are dropped silently, so a clean `wslcc compose config` is **not** evidence that a key is honored.
- Only the 🛑 rows produce a loud failure. Everything marked ❌ fails quietly, which is exactly the class of problem this page exists to surface.

**“Introduced in” convention.** The column records the first WSLCC version in which the behavior described by the row's status shipped. Rows that do something today — ✅ Applied, 🧩 Client-only, and ⚠️ Partial — are marked `0.1`, the initial release. Rows that are ❌ Not read, or whose only implemented behavior is a 🛑 rejection, are left **blank**: there is nothing to have introduced yet. When a capability lands, set its version here and update the status mark in the same table.

---

## Top-level keys

| Key | Status | Note | Introduced in |
| --- | --- | --- | --- |
| `version` | ❌ Not read | Obsolete in the Compose Specification; ignored without warning. [Format](compose-file.md#sec-3-2) | |
| `name` | ✅ Applied | Project name when `-p` / `--project-name` is absent. [Format](compose-file.md#sec-3-3) | 0.1 |
| `services` | ✅ Applied | Required for any meaningful operation. [Format](compose-file.md#sec-3-4) | 0.1 |
| `networks` | ⚠️ Partial | Only `driver` and `external` modeled; `name`, `ipam`, `internal`, `attachable`, `labels`, `driver_opts` ignored. [Format](compose-file.md#sec-5) | 0.1 |
| `volumes` | ⚠️ Partial | Only `driver` and `external` modeled; `name`, `labels`, `driver_opts` ignored. [Format](compose-file.md#sec-6) | 0.1 |
| `configs` | ❌ Not read | Neither the top-level objects nor the service attachments are created. [Format](compose-file.md#sec-7) | |
| `secrets` | ❌ Not read | Same as `configs`; move sensitive values to `environment` / `env_file` for now. [Format](compose-file.md#sec-8) | |
| `include` | 🧩 Client-only | Local paths only (short form and long `path` / `project_directory` / `env_file`). Nested includes allowed; name collisions with the including file fail the load; Git/OCI/HTTP URLs are 🛑 rejected. [Format](compose-file.md#sec-3-9) | 0.1 |
| `x-*` | ❌ Not read | Extension fields are ignored by design, never an error. YAML anchors declared under them still resolve at parse time. [Format](compose-file.md#sec-3-10) | |

---

## Service keys

Every service attribute, alphabetically. “Format” links to the syntax and nested-field reference in [compose-file.md](compose-file.md).

| Key | Status | Note | Format | Introduced in |
| --- | --- | --- | --- | --- |
| `annotations` | ✅ Applied | Map and list forms, passed as `--annotation`. Docker Engine 25+; preview `wslc` may reject the flag ([providers.md](providers.md)). Distinct from `labels` — WSLCC does not inject `wslcc.*` keys here. | [4.10.4](compose-file.md#sec-4-10-4) | 0.1 |
| `attach` | ❌ Not read | Output streaming is not configurable per service. | [4.3.10](compose-file.md#sec-4-3-10) | |
| `blkio_config` | ❌ Not read | No block-IO weights or per-device limits. | [4.12.17](compose-file.md#sec-4-12-17) | |
| `build` | ⚠️ Partial | String form, or map with `context` / `dockerfile` / `target` / `args`. All other build fields ignored. | [4.2.2](compose-file.md#sec-4-2-2) | 0.1 |
| `cap_add` | ❌ Not read | Capabilities are not granted. | [4.11.2](compose-file.md#sec-4-11-2) | |
| `cap_drop` | ❌ Not read | **Capabilities are not dropped** — no hardening effect. | [4.11.3](compose-file.md#sec-4-11-3) | |
| `cgroup` | ❌ Not read | Cgroup namespace mode not applied. | [4.12.23](compose-file.md#sec-4-12-23) | |
| `cgroup_parent` | ❌ Not read | Parent cgroup not applied. | [4.12.24](compose-file.md#sec-4-12-24) | |
| `command` | ✅ Applied | List = exec form; string = shell form via `/bin/sh -c`. | [4.3.1](compose-file.md#sec-4-3-1) | 0.1 |
| `configs` | ❌ Not read | Requires top-level `configs`, also unread. | [4.15.1](compose-file.md#sec-4-15-1) | |
| `container_name` | ✅ Applied | Used as `--name`; duplicates across services fail `up`. | [4.10.1](compose-file.md#sec-4-10-1) | 0.1 |
| `cpu_count` | ❌ Not read | Windows CPU count not applied. | [4.12.7](compose-file.md#sec-4-12-7) | |
| `cpu_percent` | ❌ Not read | Windows CPU percentage not applied. | [4.12.8](compose-file.md#sec-4-12-8) | |
| `cpu_period` | ❌ Not read | CFS period not applied. | [4.12.4](compose-file.md#sec-4-12-4) | |
| `cpu_quota` | ❌ Not read | CFS quota not applied. | [4.12.3](compose-file.md#sec-4-12-3) | |
| `cpu_rt_period` | ❌ Not read | Real-time period not applied. | [4.12.6](compose-file.md#sec-4-12-6) | |
| `cpu_rt_runtime` | ❌ Not read | Real-time runtime not applied. | [4.12.5](compose-file.md#sec-4-12-5) | |
| `cpu_shares` | ❌ Not read | Relative CPU weight not applied. | [4.12.2](compose-file.md#sec-4-12-2) | |
| `cpus` | ❌ Not read | **No CPU limit is enforced.** | [4.12.1](compose-file.md#sec-4-12-1) | |
| `cpuset` | ❌ Not read | No core pinning. | [4.12.9](compose-file.md#sec-4-12-9) | |
| `credential_spec` | ❌ Not read | Windows gMSA credentials not applied. | [4.11.7](compose-file.md#sec-4-11-7) | |
| `depends_on` | ✅ Applied | Short and long forms; `condition` and `required` honored. `restart: true` is not read. Waits cap at 5 minutes. | [4.8.1](compose-file.md#sec-4-8-1) | 0.1 |
| `deploy` | ❌ Not read | Whole subtree ignored, including `replicas` and `resources.limits`. | [4.16.1](compose-file.md#sec-4-16-1) | |
| `develop` | ❌ Not read | No file watching or hot reload. | [4.16.2](compose-file.md#sec-4-16-2) | |
| `device_cgroup_rules` | ❌ Not read | Device cgroup rules not applied. | [4.12.19](compose-file.md#sec-4-12-19) | |
| `devices` | ❌ Not read | Host devices are not exposed to the container. | [4.12.18](compose-file.md#sec-4-12-18) | |
| `dns` | ❌ Not read | Custom resolvers not applied. | [4.6.4](compose-file.md#sec-4-6-4) | |
| `dns_opt` | ❌ Not read | Resolver options not applied. | [4.6.5](compose-file.md#sec-4-6-5) | |
| `dns_search` | ❌ Not read | Search domains not applied. | [4.6.6](compose-file.md#sec-4-6-6) | |
| `domainname` | ❌ Not read | Container domain name not applied. | [4.3.6](compose-file.md#sec-4-3-6) | |
| `entrypoint` | ✅ Applied | Same exec/shell rules as `command`; passed as `--entrypoint`. | [4.3.2](compose-file.md#sec-4-3-2) | 0.1 |
| `env_file` | ✅ Applied | Passed as `--env-file`; `{path, required}` long form honored. `format: raw` is not. | [4.4.2](compose-file.md#sec-4-4-2) | 0.1 |
| `environment` | ✅ Applied | Map and list forms; overrides `env_file`. Bare keys inherit from **`wslccd`'s** environment. | [4.4.1](compose-file.md#sec-4-4-1) | 0.1 |
| `expose` | ❌ Not read | Documentation-only key; no effect either way. | [4.5.2](compose-file.md#sec-4-5-2) | |
| `extends` | 🧩 Client-only | Resolved before the daemon; chains allowed, cycles rejected. | [4.10.6](compose-file.md#sec-4-10-6) | 0.1 |
| `external_links` | ❌ Not read | External aliases not created. | [4.6.8](compose-file.md#sec-4-6-8) | |
| `extra_hosts` | ❌ Not read | No `/etc/hosts` entries added. | [4.6.3](compose-file.md#sec-4-6-3) | |
| `gpus` | ❌ Not read | No GPU devices requested. | [4.12.20](compose-file.md#sec-4-12-20) | |
| `group_add` | ❌ Not read | Supplementary groups not added. | [4.11.8](compose-file.md#sec-4-11-8) | |
| `healthcheck` | ✅ Applied | `test`, `interval`, `timeout`, `retries`, `start_period`, `disable`. `start_interval` is not read; `test` is flattened to a shell command. | [4.8.2](compose-file.md#sec-4-8-2) | 0.1 |
| `hostname` | ✅ Applied | Passed as `--hostname`. | [4.3.5](compose-file.md#sec-4-3-5) | 0.1 |
| `image` | ✅ Applied | Optional when `build:` is present. | [4.2.1](compose-file.md#sec-4-2-1) | 0.1 |
| `init` | ❌ Not read | No init shim; PID 1 stays your process. | [4.3.11](compose-file.md#sec-4-3-11) | |
| `ipc` | ❌ Not read | IPC namespace mode not applied. | [4.14.1](compose-file.md#sec-4-14-1) | |
| `isolation` | ❌ Not read | Windows isolation mode not applied. | [4.14.4](compose-file.md#sec-4-14-4) | |
| `label_file` | ❌ Not read | Label files are not loaded. | [4.10.3](compose-file.md#sec-4-10-3) | |
| `labels` | ✅ Applied | Map and list forms. WSLCC's `wslcc.*` labels win on a key clash. | [4.10.2](compose-file.md#sec-4-10-2) | 0.1 |
| `links` | ❌ Not read | Legacy links ignored; also no implied startup ordering. | [4.6.7](compose-file.md#sec-4-6-7) | |
| `logging` | ❌ Not read | Driver and options ignored; the runtime default applies. | [4.13.1](compose-file.md#sec-4-13-1) | |
| `mac_address` | ❌ Not read | MAC address not assigned. | [4.3.7](compose-file.md#sec-4-3-7) | |
| `mem_limit` | ❌ Not read | **No memory limit is enforced.** | [4.12.10](compose-file.md#sec-4-12-10) | |
| `mem_reservation` | ❌ Not read | Soft reservation not applied. | [4.12.11](compose-file.md#sec-4-12-11) | |
| `mem_swappiness` | ❌ Not read | Swappiness not applied. | [4.12.13](compose-file.md#sec-4-12-13) | |
| `memswap_limit` | ❌ Not read | Combined memory + swap limit not applied. | [4.12.12](compose-file.md#sec-4-12-12) | |
| `models` | ❌ Not read | Compose models integration is absent. | [4.16.5](compose-file.md#sec-4-16-5) | |
| `network_mode` | ❌ Not read | `host` / `none` / `service:` / `container:` all ignored; the service joins normal networks instead. | [4.6.2](compose-file.md#sec-4-6-2) | |
| `networks` | ⚠️ Partial | Membership applied (list and map keys). Per-network values — `aliases`, `ipv4_address`, `ipv6_address`, `priority`, `mac_address`, `driver_opts` — ignored. | [4.6.1](compose-file.md#sec-4-6-1) | 0.1 |
| `oom_kill_disable` | ❌ Not read | OOM killer not disabled. | [4.12.14](compose-file.md#sec-4-12-14) | |
| `oom_score_adj` | ❌ Not read | OOM score not adjusted. | [4.12.15](compose-file.md#sec-4-12-15) | |
| `pid` | ❌ Not read | PID namespace mode not applied. | [4.14.2](compose-file.md#sec-4-14-2) | |
| `pids_limit` | ❌ Not read | Process count not capped. | [4.12.16](compose-file.md#sec-4-12-16) | |
| `platform` | ❌ Not read | No `--platform` passed; the host architecture is used. | [4.2.4](compose-file.md#sec-4-2-4) | |
| `ports` | ⚠️ Partial | Short syntax published as `-p`. Long map form is 🛑 **rejected** with an error. | [4.5.1](compose-file.md#sec-4-5-1) | 0.1 |
| `post_start` | ❌ Not read | Lifecycle hooks are not implemented. | [4.9.5](compose-file.md#sec-4-9-5) | |
| `pre_start` | ❌ Not read | Lifecycle hooks are not implemented. | [4.9.4](compose-file.md#sec-4-9-4) | |
| `pre_stop` | ❌ Not read | Lifecycle hooks are not implemented. | [4.9.6](compose-file.md#sec-4-9-6) | |
| `privileged` | ❌ Not read | Containers are **not** privileged; workloads that need it will fail at runtime. | [4.11.1](compose-file.md#sec-4-11-1) | |
| `profiles` | 🧩 Client-only | List form only — a scalar is read as “no profiles”. Filtering happens before the daemon. | [4.10.5](compose-file.md#sec-4-10-5) | 0.1 |
| `provider` | ❌ Not read | External provider plugins are not supported. | [4.16.4](compose-file.md#sec-4-16-4) | |
| `pull_policy` | ❌ Not read | Pulling is driven by `compose pull` / `up --pull` instead. | [4.2.3](compose-file.md#sec-4-2-3) | |
| `read_only` | ✅ Applied | Passed as `--read-only` when true. | [4.11.5](compose-file.md#sec-4-11-5) | 0.1 |
| `restart` | ⚠️ Partial | Passed through as `--restart`. Accepted by the `docker` provider; the preview `wslc` provider may reject the flag ([providers.md](providers.md)). | [4.9.1](compose-file.md#sec-4-9-1) | 0.1 |
| `runtime` | ❌ Not read | Alternative OCI runtimes not selected. | [4.14.5](compose-file.md#sec-4-14-5) | |
| `scale` | ❌ Not read | One container per service; no replicas. | [4.16.3](compose-file.md#sec-4-16-3) | |
| `secrets` | ❌ Not read | Requires top-level `secrets`, also unread. | [4.15.2](compose-file.md#sec-4-15-2) | |
| `security_opt` | ❌ Not read | **`no-new-privileges`, seccomp, and AppArmor overrides have no effect.** | [4.11.4](compose-file.md#sec-4-11-4) | |
| `shm_size` | ❌ Not read | `/dev/shm` keeps the runtime default (usually 64 MB). | [4.12.21](compose-file.md#sec-4-12-21) | |
| `stdin_open` | ❌ Not read | No `-i`. | [4.3.8](compose-file.md#sec-4-3-8) | |
| `stop_grace_period` | ❌ Not read | Runtime default stop timeout applies. | [4.9.2](compose-file.md#sec-4-9-2) | |
| `stop_signal` | ❌ Not read | Always the runtime default (`SIGTERM`). | [4.9.3](compose-file.md#sec-4-9-3) | |
| `storage_opt` | ❌ Not read | Storage driver options not passed. | [4.17.1](compose-file.md#sec-4-17-1) | |
| `sysctls` | ❌ Not read | Kernel parameters not set (they do merge across files, then get dropped). | [4.17.2](compose-file.md#sec-4-17-2) | |
| `tmpfs` | ✅ Applied | Emitted as `--tmpfs` (path or `path:opts`). | [4.7.3](compose-file.md#sec-4-7-3) | 0.1 |
| `tty` | ❌ Not read | No `-t`. | [4.3.9](compose-file.md#sec-4-3-9) | |
| `ulimits` | ❌ Not read | Daemon defaults apply; `nofile` is not raised. | [4.12.22](compose-file.md#sec-4-12-22) | |
| `use_api_socket` | ❌ Not read | The engine API socket is not mounted. | [4.17.3](compose-file.md#sec-4-17-3) | |
| `user` | ✅ Applied | Passed as `-u`; accepts `uid`, `uid:gid`, or names. | [4.3.4](compose-file.md#sec-4-3-4) | 0.1 |
| `userns_mode` | ❌ Not read | User namespace mode not applied. | [4.11.6](compose-file.md#sec-4-11-6) | |
| `uts` | ❌ Not read | UTS namespace mode not applied. | [4.14.3](compose-file.md#sec-4-14-3) | |
| `volumes` | ⚠️ Partial | Short syntax and long-form `type: volume` / `bind` / `tmpfs` (including option blocks → `--mount` / `--tmpfs`). `type: npipe` / `cluster` / `image` are 🛑 **rejected**. | [4.7.1](compose-file.md#sec-4-7-1) | 0.1 |
| `volumes_from` | ❌ Not read | Mounts are not inherited from other containers. | [4.7.2](compose-file.md#sec-4-7-2) | |
| `working_dir` | ✅ Applied | Passed as `-w`. | [4.3.3](compose-file.md#sec-4-3-3) | 0.1 |

---

## What “Partial” means, key by key

The ⚠️ rows are the ones that reward a closer look, because they accept your YAML and then honor only part of it.

| Key | Honored | Ignored or refused |
| --- | --- | --- |
| `build` | `context`, `dockerfile`, `target`, `args`; string shorthand | `cache_from`/`cache_to`, `secrets`, `ssh`, `platforms`, `tags`, `labels`, `network`, `no_cache`, `pull`, `extra_hosts`, `ulimits`, `isolation`, `privileged`, `additional_contexts`, `dockerfile_inline`, `entitlements` |
| `ports` | Short syntax, including host IP, ranges, `/udp` | Long map form → hard error (not silently dropped) |
| `volumes` (service) | Short syntax; long-form `volume` / `bind` / `tmpfs` (+ nested option blocks); service `tmpfs:` | `type: npipe` / `cluster` / `image` → hard error; `consistency` ignored |
| `networks` (service) | Which networks the service joins | `aliases`, `ipv4_address`, `ipv6_address`, `link_local_ips`, `mac_address`, `priority`, `gw_priority`, `driver_opts`, `interface_name` |
| `networks` (top level) | `driver`, `external` | `name`, `ipam` (subnets, gateways), `internal`, `attachable`, `labels`, `driver_opts`, `enable_ipv4`/`enable_ipv6` |
| `volumes` (top level) | `driver`, `external` | `name`, `labels`, `driver_opts` |
| `restart` | The value, passed to the provider as `--restart` | Nothing in the value itself; the `wslc` preview provider may refuse the flag |
| `depends_on` | `condition`, `required`, short list form | `restart: true`; unknown `condition` values silently become `service_started` |
| `healthcheck` | `test`, `interval`, `timeout`, `retries`, `start_period`, `disable` | `start_interval`; `CMD` vs `CMD-SHELL` distinction (both flatten to a shell command) |

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
| `config --hash` | Compose's own digest | WSLCC-specific SHA-256 (for change detection only) |
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
| Interpolation | Process env overlays `.env` | Same idea; grammar documented in [compose-file.md §2.3](compose-file.md#sec-2-3) |
| Profiles | List or string shorthand | **List form only** — `profiles: debug` (scalar) is treated as “no profiles” (service always on). Use `profiles: ["debug"]`. |
| Profiled dependencies | Spec/tooling nuances | Disabled services are removed and `depends_on` refs pruned; dependencies are **not** auto-activated |
| `include` | Nested project: own directory and `.env`; name clashes with the including file are errors | Same for local files. Git/OCI/HTTP include URLs fail the load. Relative bind/`env_file`/`build` paths in the included file are rewritten to absolute so the engine's single project directory still works. |

---

## Compose keys that look valid but do not work as in Compose

These are the ones that bite when you reuse an existing file: WSLCC may accept the YAML (or a close subset) while runtime behavior diverges.

### Applied with different semantics

| Key / form | Compose | WSLCC |
| --- | --- | --- |
| `command:` / `entrypoint:` string | Shell form: `/bin/sh -c "…"` | Same shell form. List form remains exec (no shell). |
| `environment: [FOO]` / bare `FOO:` | Inherit from the **client** shell / project env | Passed as `-e FOO` to the runtime → inherits from **`wslccd`'s** process environment, not your shell |
| `env_file` vs CLI `--env-file` / `.env` | Both feed container env / project env per Compose rules | Service `env_file:` → container `--env-file`. CLI `--env-file` / `.env` only affect *interpolation* |
| `container_name` | Fixed name; blocks scale | Honored; duplicate names across services fail `up` |
| `labels` (service) | Container labels (+ Compose's own) | Honored; WSLCC's `wslcc.*` labels win on key clash |
| `ports` long map form | Supported | **Rejected** with an error (short syntax only) |
| `volumes` long map form | Supported | `volume` / `bind` / `tmpfs` applied; `npipe` / `cluster` / `image` **rejected** |
| `networks:` map values (`aliases`, `ipv4_address`, …) | Applied | Only membership (keys) applied; map values ignored |
| Top-level `networks` / `volumes` | `driver`, `external`, `name`, IPAM, `driver_opts`, … | Only `driver` and `external` modeled; no resource `name:` override, no IPAM |

### Not read at all (silently dropped)

Every key marked ❌ in the [service key matrix](#service-keys) above. In practice the ones that hurt are `configs`, `secrets`, `deploy`, `privileged`, `cap_add`, `cap_drop`, `security_opt`, `devices`, `ulimits`, `extra_hosts`, `dns`, `domainname`, `shm_size`, `pids_limit`, `mem_limit`, `cpus`, `init`, `stdin_open`, `tty`, `network_mode`, `pid`, `ipc`, `uts`, `stop_grace_period`, `stop_signal`, `expose`, `volumes_from`, `links`, `logging`, and `scale` / `deploy.replicas`.

**Security note:** `user:` and `read_only:` are applied. Other hardening keys are not — `privileged:`, `cap_drop:`, `security_opt:`, `userns_mode:` all have **no effect**. Do not assume YAML you trusted under Compose still enforces those unread constraints under WSLCC.

**Resource note:** none of the CPU, memory, PID, or IO limits are enforced. A container that Compose would have capped at 512 MB can consume everything the host has.

---

## Runtime / orchestration differences

| Topic | Docker Compose | WSLCC |
| --- | --- | --- |
| Dependency condition wait | Tool-defined | Caps at **5 minutes**, then fails the dependent ([troubleshooting](troubleshooting.md#up-seems-hung-for-minutes)) |
| Unknown `depends_on.condition` | Rejected | Silently treated as `service_started` |
| `depends_on` + `required: false` | Optional dependency | Intended to match Compose; see [compose-file.md §4.8.1](compose-file.md#sec-4-8-1) |
| Change detection | Recreate heuristics / flags | Config-hash on **running** containers; stopped-but-unchanged containers are recreated |
| Config hash | Compose's | WSLCC's own; not interchangeable |
| Named volumes / networks | Compose naming + labels | `<project>_<name>`, `<project>_default`; WSLCC labels for teardown |
| `external: true` | Must exist; not created/removed | Same intent |
| Build tagging | Compose conventions | `<project>-<service>` unless `image:` is set |
| Healthcheck `test` exec form | Distinct CMD vs CMD-SHELL | Flattened to a shell `--health-cmd` string |

---

## Merge / `extends` fidelity gaps

These matter if you rely on multi-file overrides or `extends` the way Compose documents them:

| Topic | Compose | WSLCC today |
| --- | --- | --- |
| Sequence merge for long-form ports/volumes | Unique-key merge by target | Ports long form rejected; volumes long form accepted for volume/bind/tmpfs (lists append); short-form lists append + exact-dedup |
| `build: .` merged with `build: { dockerfile: … }` | Becomes `{ context: ., dockerfile: … }` | Override can **drop** the string-side context (map wins wholesale when types differ) |
| Long-form `depends_on` field merge | Per-dependency fields merge | Overriding one field (e.g. `required`) can **replace** the whole dependency object and lose `condition` |
| `extends` non-inheritable keys | e.g. `container_name` not inherited | Several keys may still merge from the base (stricter Compose non-inherit rules not fully matched) |

Merge rules as implemented: [compose-file.md §2.2](compose-file.md#sec-2-2). Tracking: [todo.md](todo.md).

---

## Practical migration checklist

1. Install WSLCC, start the daemon: `wslcc daemon start` (watch elevation — [troubleshooting](troubleshooting.md#daemon-not-reachable)).
2. Pick a provider: default `wslc`, or `wslcc daemon start --provider docker`.
3. From the project directory (so `.env` resolves as you expect), run:
   ```powershell
   wslcc compose config
   wslcc compose config --hash "*"
   ```
4. Search your compose files for: long-form `ports`, unsupported volume types (`npipe` / `cluster` / `image`), `configs`/`secrets`/`deploy`, `privileged` / `cap_*` / `security_opt`, resource limits (`mem_limit`, `cpus`, `ulimits`), and bare `environment` keys you expect from your shell (those inherit from `wslccd`, not your client shell).
5. Cross-check each hit against the [service key matrix](#service-keys): long-form `ports` and unsupported volume types fail loudly; unread keys fail silently.
6. Pass `-f` for overrides explicitly; use `--project-directory` if you invoke from another cwd (also affects `env_file:` / bind / build path resolution).
7. Bring the stack up under WSLCC (`up -d`), verify with `wslcc compose ps` — not `docker compose ps`.
8. Tear down with `wslcc compose down` (add `-v` only if you intend to delete named volumes).

---

## See also

- [compose-file.md](compose-file.md) — the Compose file format itself: every key's syntax, nested fields, and meaning
- [cli-mapping.md](cli-mapping.md) — full CLI reference and the Compose→wslcc command table
- [providers.md](providers.md) — `wslc` vs `docker` backends
- [troubleshooting.md](troubleshooting.md) — first-run failures
- [todo.md](todo.md) — intentional fidelity backlog
- [roadmap.md](roadmap.md) — when the gaps above are expected to close
