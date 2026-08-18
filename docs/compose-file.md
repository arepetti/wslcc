# Compose file reference

The authoritative reference for the Compose file format (`compose.yaml` / `docker-compose.yml`) as used by WSLCC. It is **self-contained**: every top-level key and every service attribute is described here — purpose, YAML syntax, nested fields, and the values each field accepts — so you can write or read a Compose file without consulting anything else.

This document answers **“what does this YAML mean?”**. It does not answer “is it supported yet?” — support status, gaps, and migration traps live in exactly one place: [compatibility.md](compatibility.md). Command-line flags (`-f`, `-p`, `--profile`, …) live in [cli-mapping.md](cli-mapping.md).

A handful of sections carry a short ***WSLCC note*** where the way WSLCC drives the runtime changes what a key does in practice (naming, ordering, resolution). Those notes exist because they change how you write the file; they are not a support matrix.

<a id="sec-1"></a>
## 1 Introduction
<a id="sec-1-1"></a>
### 1.1 Purpose and scope
This reference covers the whole Compose application model:

- how a Compose file is discovered, merged, interpolated, and resolved ([§2](#sec-2));
- top-level document keys — `name`, `services`, `networks`, `volumes`, `configs`, `secrets`, `include`, extensions ([§3](#sec-3));
- every service attribute, grouped by topic, with an alphabetical index ([§4](#sec-4));
- top-level `networks` ([§5](#sec-5)), `volumes` ([§6](#sec-6)), `configs` ([§7](#sec-7)), `secrets` ([§8](#sec-8));
- fragments, merge keys, and includes ([§9](#sec-9));
- WSLCC runtime behavior that a compose file author needs to know: resource naming, the implicit default network, change detection, teardown ([§10](#sec-10)).

WSLCC does **not** invoke the Docker Compose plugin. Even with `--wslcc-provider docker`, the lifecycle is driven by WSLCC's own engine on top of the plain `docker` or `wslc` CLI ([providers.md](providers.md)).

<a id="sec-1-2"></a>
### 1.2 How this relates to compatibility.md
Every key in this document is described as the Compose format defines it, regardless of how much of it WSLCC currently honors. That keeps the format reference stable while implementation catches up.

If your question is any of these:

- “Does WSLCC read this key?”
- “Will this key silently do nothing?”
- “Which version of WSLCC started honoring it?”
- “What will break when I move an existing project over?”

…then the answer is in [compatibility.md](compatibility.md), which carries the support-status legend, the top-level key table, the full alphabetical service-key matrix, and the migration checklist.

One rule worth stating here because it affects how you write files: **unknown keys never fail the parse by themselves.** A key WSLCC does not model is dropped silently. Do not infer from “it loaded fine” that a security or resource key took effect.

<a id="sec-1-3"></a>
### 1.3 How to cite this document
Sections are numbered so code comments, reviews, and issues can point at a stable place. **Prefer the section title over the bare number** — titles survive renumbering and read better at the call site:

```csharp
// Compose file — command: shell form expands to /bin/sh -c
// Compose file — ports: short syntax is [HOST:]CONTAINER[/PROTOCOL]
```

Add the number only when you also want a clickable link: `docs/compose-file.md#sec-4-3-1` (dots in the section number become hyphens in the fragment, so §4.3.1 → `#sec-4-3-1`).

Legacy aliases (`#resolution-features`, `#service-reference`, `#profiles`, `#startup-order-and-health`, `#networks-and-volumes`, `#change-detection-up`) still resolve for older links; prefer `#sec-…` in new citations.

<a id="sec-1-4"></a>
### 1.4 Related documents
| Document | Role |
| --- | --- |
| [compatibility.md](compatibility.md) | Support status for every key, plus the migration / “what breaks” checklist |
| [cli-mapping.md](cli-mapping.md) | `wslcc` commands and flags |
| [providers.md](providers.md) | `wslc` vs `docker` backends |
| [troubleshooting.md](troubleshooting.md) | Common load and runtime failures |
| [todo.md](todo.md) | Deferred fidelity work |

<a id="sec-2"></a>
<a id="resolution-features"></a>
## 2 How WSLCC loads a Compose file
<a id="sec-2-1"></a>
### 2.1 Two-stage pipeline
Resolution uses the single `Wslcc.Compose` library in two stages:

1. **Client-side** (`ComposeLoader`, in the CLI): multi-file merge, `.env` + `${VAR}` interpolation, `extends` expansion, profile filtering — then re-serialize the result as one YAML document.
2. **Daemon-side** (`ComposeFileParser`): parse that single document into the typed model used by `ComposeEngine`. By this point profiles and `extends` no longer exist.

`wslcc compose config` runs stage 1 only and prints exactly what every other command will send to `wslccd`. It is the fastest way to see what your file actually resolves to.

<a id="sec-2-2"></a>
### 2.2 Multi-file merge
A Compose application can be split across several files. Repeat `-f` (`-f compose.yaml -f compose.override.yaml`) or set `COMPOSE_FILE` (paths separated by `COMPOSE_PATH_SEPARATOR`, default `;` on Windows). Later files override earlier ones.

Merge rules:

| Attribute shape | Rule |
| --- | --- |
| Mapping attributes (`environment`, `labels`, `annotations`, `sysctls`), including their `KEY=VALUE` list forms | Merge by key; later value wins |
| `depends_on`, `networks` | Merge by key when either side is a map; otherwise append |
| Most other sequences (`ports`, `volumes`, `dns`, …) | Append, dropping exact duplicates |
| `command`, `entrypoint` | Replace wholesale (never element-wise) |
| Scalars (`image`, `user`, `restart`, …) | Replace |

***WSLCC note:*** `compose.override.yaml` is **not** merged automatically — pass it explicitly. Default discovery with no `-f` / `COMPOSE_FILE` picks the first of `compose.yaml`, `compose.yml`, `docker-compose.yaml`, `docker-compose.yml` in the project directory.

<a id="sec-2-3"></a>
### 2.3 Variable interpolation and `.env`
Values anywhere in the document may reference variables. Supported forms:

| Form | Meaning |
| --- | --- |
| `${VAR}` / `$VAR` | Substitute the value; empty if unset |
| `${VAR:-default}` | Use `default` when `VAR` is unset **or empty** |
| `${VAR-default}` | Use `default` only when `VAR` is unset |
| `${VAR:?message}` | Fail with `message` when `VAR` is unset or empty |
| `${VAR?message}` | Fail with `message` only when `VAR` is unset |
| `${VAR:+alternate}` | Use `alternate` when `VAR` is set and non-empty |
| `${VAR+alternate}` | Use `alternate` when `VAR` is set (even if empty) |
| `$$` | A literal `$` |

Values come from the process environment overlaid on a `.env` file; the process environment wins.

`.env` grammar: one `KEY=VALUE` per line, an optional leading `export`, `#` comments, single quotes (fully literal), double quotes (C-style escapes plus `${VAR}` expansion), multi-line quoted values, and self-references to variables defined earlier in the same file. A variable that is unset and has no default resolves to the empty string and emits a warning.

`--no-interpolate` leaves `${VAR}` verbatim in the output while still merging files, expanding `extends`, and filtering profiles.

***WSLCC note:*** without `--env-file`, the default `.env` is read from the **current working directory**, or from `--project-directory` when that is set — not automatically from the directory of the first compose file.

<a id="sec-2-4"></a>
### 2.4 Project directory
The project directory anchors every relative path in the document: `build.context`, bind-mount sources, `env_file` paths, and the default project name. It is the directory of the first compose file, or `--project-directory` when given. The default `.env` location follows `--project-directory` when set, otherwise the current working directory ([§2.3](#sec-2-3)).

<a id="sec-2-5"></a>
### 2.5 `wslcc compose config`
Client-only: prints the fully resolved document (including the effective `name:`) without contacting the daemon. Flags are documented in [cli-mapping.md — config](cli-mapping.md#wslcc-compose-config). `--hash` prints WSLCC's own configuration digest, the one used for change detection ([§10.2](#sec-10-2)).

<a id="sec-3"></a>
## 3 Top-level elements
<a id="sec-3-1"></a>
### 3.1 Overview
| Key | Summary | Detail |
| --- | --- | --- |
| `version` | Obsolete schema-version declaration. | [3.2](#sec-3-2) |
| `name` | Project name that scopes all resources. | [3.3](#sec-3-3) |
| `services` | The containers that make up the application. | [3.4](#sec-3-4) / [4](#sec-4) |
| `networks` | Networks the application creates or reuses. | [3.5](#sec-3-5) / [5](#sec-5) |
| `volumes` | Named volumes the application creates or reuses. | [3.6](#sec-3-6) / [6](#sec-6) |
| `configs` | Non-sensitive files granted to services. | [3.7](#sec-3-7) / [7](#sec-7) |
| `secrets` | Sensitive files granted to services. | [3.8](#sec-3-8) / [8](#sec-8) |
| `include` | Pull other Compose files in as sub-projects. | [3.9](#sec-3-9) |
| `x-*` | User-defined extension fields. | [3.10](#sec-3-10) |

<a id="sec-3-2"></a>
### 3.2 `version`
A leftover from the Compose v1/v2 file-schema era, when the document declared which schema revision it targeted (`version: "3.8"`). The current Compose Specification is versionless: features are negotiated by the tool, not by a declared number.

```yaml
version: "3.8"   # obsolete; harmless but meaningless
services:
  web:
    image: nginx
```

Modern tooling ignores the value, and many tools warn about it. New files should simply omit it.

<a id="sec-3-3"></a>
### 3.3 `name`
Sets the **project name**, the identifier that scopes every container, network, and volume the application creates. It must be lowercase and may contain letters, digits, hyphens, and underscores.

```yaml
name: billing-stack
services:
  api:
    image: billing/api:1.4
```

Precedence, highest first: `-p` / `--project-name` on the command line, the `COMPOSE_PROJECT_NAME` environment variable, `name:` in the file, then the sanitized name of the project directory. The resolved value is available for interpolation as `${COMPOSE_PROJECT_NAME}`. See [cli-mapping.md — project name](cli-mapping.md#project-name-and-connection) and [§10.1](#sec-10-1) for how it shapes resource names.

<a id="sec-3-4"></a>
### 3.4 `services`
A map of service name → service definition. The service name is the key; it is the DNS name other services use to reach it, and it feeds the default container name. Service names should be lowercase and DNS-safe.

```yaml
services:
  web:
    image: nginx:1.27
  worker:
    build: ./worker
```

Each service must resolve to an image, either directly through `image:` ([§4.2.1](#sec-4-2-1)) or by building one through `build:` ([§4.2.2](#sec-4-2-2)). The full attribute list is [§4](#sec-4).

<a id="sec-3-5"></a>
### 3.5 `networks`
Declares the networks the application uses, so that services can join them by name. An empty value means “create a network with default settings”. Full attribute list: [§5](#sec-5).

```yaml
networks:
  frontend:
  backend:
    driver: bridge
  corp-shared:
    external: true
```

<a id="sec-3-6"></a>
### 3.6 `volumes`
Declares named volumes — storage managed by the runtime whose lifetime is independent of any single container. A service references one by name in its `volumes:` list ([§4.7.1](#sec-4-7-1)). Full attribute list: [§6](#sec-6).

```yaml
volumes:
  db-data:
  cache:
    driver: local
  shared-data:
    external: true
```

<a id="sec-3-7"></a>
### 3.7 `configs`
Declares non-sensitive files (or literal content) that services can mount, so the same image can be configured differently per environment without rebuilding. Services opt in through their own `configs:` list ([§4.15.1](#sec-4-15-1)). Fields and syntax: [§7](#sec-7).

<a id="sec-3-8"></a>
### 3.8 `secrets`
Declares sensitive data — credentials, tokens, certificates — granted to individual services. Mechanically similar to `configs`, but intended for values that must not land in the image or in `environment:`. Services opt in through their own `secrets:` list ([§4.15.2](#sec-4-15-2)). Fields and syntax: [§8](#sec-8).

<a id="sec-3-9"></a>
### 3.9 `include`
Pulls another Compose file into the current application as a sub-project, so a team can consume a stack that another team owns without copying its YAML or knowing its internal file layout.

```yaml
include:
  - ../shared/database/compose.yaml          # short form: a path
  - path: ../shared/telemetry/compose.yaml   # long form
    project_directory: ../shared/telemetry
    env_file: ../shared/telemetry/.env
```

Long-form fields:

| Field | Meaning |
| --- | --- |
| `path` | One path, or a list of paths merged together as the included model |
| `project_directory` | Base directory for the included file's relative paths; defaults to the directory of the first `path` |
| `env_file` | `.env` file(s) used to interpolate the included file; defaults to `.env` in `project_directory` |

The key difference from `-f` is scoping: an included file resolves its own relative paths and its own variables against its own directory, whereas `-f` files are merged into a single document sharing one project directory. Included services join the parent project and must not collide with names already defined there.

***WSLCC note:*** to compose across directories today, list the files with repeated `-f` and set `--project-directory` explicitly ([§2.2](#sec-2-2)).

<a id="sec-3-10"></a>
### 3.10 Extension fields (`x-*`)
Any key beginning with `x-` is reserved for users and tools; the format guarantees it will never be given meaning. Extension fields are valid at the document root and inside `services`, `networks`, `volumes`, `configs`, and `secrets`.

```yaml
x-logging: &default-logging
  driver: json-file
  options:
    max-size: "10m"

services:
  web:
    image: nginx
    logging: *default-logging
  api:
    image: api:1.0
    logging: *default-logging
```

Their most common use is as anchor holders for YAML aliases and merge keys ([§9.1](#sec-9-1)), which lets you factor out repeated blocks without a preprocessor.

<a id="sec-4"></a>
<a id="service-reference"></a>
## 4 Services
<a id="sec-4-1"></a>
### 4.1 Service key index
Alphabetical index of every service attribute. Follow the detail link for syntax and nested fields. For support status, see [compatibility.md](compatibility.md).

| Key | Summary | Detail |
| --- | --- | --- |
| `annotations` | OCI annotations attached to the container. | [4.10.4](#sec-4-10-4) |
| `attach` | Whether the tool streams this service's output. | [4.3.10](#sec-4-3-10) |
| `blkio_config` | Block IO weight and per-device IO limits. | [4.12.17](#sec-4-12-17) |
| `build` | How to build the image instead of pulling it. | [4.2.2](#sec-4-2-2) |
| `cap_add` | Add Linux kernel capabilities. | [4.11.2](#sec-4-11-2) |
| `cap_drop` | Remove Linux kernel capabilities. | [4.11.3](#sec-4-11-3) |
| `cgroup` | Cgroup namespace mode (`host` / `private`). | [4.12.23](#sec-4-12-23) |
| `cgroup_parent` | Parent cgroup for the container. | [4.12.24](#sec-4-12-24) |
| `command` | Override the image's default command. | [4.3.1](#sec-4-3-1) |
| `configs` | Mount configs declared at the top level. | [4.15.1](#sec-4-15-1) |
| `container_name` | Fixed container name instead of the generated one. | [4.10.1](#sec-4-10-1) |
| `cpu_count` | Number of usable CPUs (Windows containers). | [4.12.7](#sec-4-12-7) |
| `cpu_percent` | Percentage of CPU the container may use (Windows). | [4.12.8](#sec-4-12-8) |
| `cpu_period` | Length of the CFS scheduling period. | [4.12.4](#sec-4-12-4) |
| `cpu_quota` | CPU time allowed per CFS period. | [4.12.3](#sec-4-12-3) |
| `cpu_rt_period` | Real-time scheduler period. | [4.12.6](#sec-4-12-6) |
| `cpu_rt_runtime` | Real-time CPU time per period. | [4.12.5](#sec-4-12-5) |
| `cpu_shares` | Relative CPU weight under contention. | [4.12.2](#sec-4-12-2) |
| `cpus` | Fractional CPU limit. | [4.12.1](#sec-4-12-1) |
| `cpuset` | Explicit CPU cores the container may run on. | [4.12.9](#sec-4-12-9) |
| `credential_spec` | gMSA credential spec (Windows). | [4.11.7](#sec-4-11-7) |
| `depends_on` | Startup order and readiness conditions. | [4.8.1](#sec-4-8-1) |
| `deploy` | Replicas, resources, placement, rollout policy. | [4.16.1](#sec-4-16-1) |
| `develop` | File-watch / hot-reload configuration. | [4.16.2](#sec-4-16-2) |
| `device_cgroup_rules` | Raw cgroup rules for device access. | [4.12.19](#sec-4-12-19) |
| `devices` | Expose host devices inside the container. | [4.12.18](#sec-4-12-18) |
| `dns` | Custom DNS resolvers. | [4.6.4](#sec-4-6-4) |
| `dns_opt` | Resolver options written to `/etc/resolv.conf`. | [4.6.5](#sec-4-6-5) |
| `dns_search` | DNS search domains. | [4.6.6](#sec-4-6-6) |
| `domainname` | NIS domain name of the container. | [4.3.6](#sec-4-3-6) |
| `entrypoint` | Override the image's entrypoint. | [4.3.2](#sec-4-3-2) |
| `env_file` | Load container environment from files. | [4.4.2](#sec-4-4-2) |
| `environment` | Environment variables set on the container. | [4.4.1](#sec-4-4-1) |
| `expose` | Document ports reachable from other services. | [4.5.2](#sec-4-5-2) |
| `extends` | Inherit and override another service definition. | [4.10.6](#sec-4-10-6) |
| `external_links` | Reach containers outside this project by alias. | [4.6.8](#sec-4-6-8) |
| `extra_hosts` | Extra `/etc/hosts` entries. | [4.6.3](#sec-4-6-3) |
| `gpus` | Request GPU devices. | [4.12.20](#sec-4-12-20) |
| `group_add` | Extra supplementary groups for the process. | [4.11.8](#sec-4-11-8) |
| `healthcheck` | How to test that the container is healthy. | [4.8.2](#sec-4-8-2) |
| `hostname` | Hostname inside the container. | [4.3.5](#sec-4-3-5) |
| `image` | Image reference to run. | [4.2.1](#sec-4-2-1) |
| `init` | Run PID 1 under a minimal init process. | [4.3.11](#sec-4-3-11) |
| `ipc` | IPC namespace mode. | [4.14.1](#sec-4-14-1) |
| `isolation` | Isolation technology (Windows). | [4.14.4](#sec-4-14-4) |
| `label_file` | Load container labels from files. | [4.10.3](#sec-4-10-3) |
| `labels` | Container labels. | [4.10.2](#sec-4-10-2) |
| `links` | Legacy container-to-container aliases. | [4.6.7](#sec-4-6-7) |
| `logging` | Logging driver and its options. | [4.13.1](#sec-4-13-1) |
| `mac_address` | MAC address of the container interface. | [4.3.7](#sec-4-3-7) |
| `mem_limit` | Hard memory limit. | [4.12.10](#sec-4-12-10) |
| `mem_reservation` | Soft memory reservation. | [4.12.11](#sec-4-12-11) |
| `mem_swappiness` | Anonymous-page swap tendency (0–100). | [4.12.13](#sec-4-12-13) |
| `memswap_limit` | Combined memory + swap limit. | [4.12.12](#sec-4-12-12) |
| `models` | Attach AI models declared at the top level. | [4.16.5](#sec-4-16-5) |
| `network_mode` | Replace network attachment with a namespace mode. | [4.6.2](#sec-4-6-2) |
| `networks` | Networks the service joins, plus per-network options. | [4.6.1](#sec-4-6-1) |
| `oom_kill_disable` | Prevent the kernel OOM killer from acting. | [4.12.14](#sec-4-12-14) |
| `oom_score_adj` | Bias the OOM killer's victim choice. | [4.12.15](#sec-4-12-15) |
| `pid` | PID namespace mode. | [4.14.2](#sec-4-14-2) |
| `pids_limit` | Maximum number of processes. | [4.12.16](#sec-4-12-16) |
| `platform` | Target OS/architecture for the image. | [4.2.4](#sec-4-2-4) |
| `ports` | Publish container ports to the host. | [4.5.1](#sec-4-5-1) |
| `post_start` | Hook run after the container starts. | [4.9.5](#sec-4-9-5) |
| `pre_start` | Hook run before the container starts. | [4.9.4](#sec-4-9-4) |
| `pre_stop` | Hook run before the container is stopped. | [4.9.6](#sec-4-9-6) |
| `privileged` | Grant the container full host privileges. | [4.11.1](#sec-4-11-1) |
| `profiles` | Activate the service only under named profiles. | [4.10.5](#sec-4-10-5) |
| `provider` | Delegate the service to an external provider plugin. | [4.16.4](#sec-4-16-4) |
| `pull_policy` | When to pull the image. | [4.2.3](#sec-4-2-3) |
| `read_only` | Mount the container root filesystem read-only. | [4.11.5](#sec-4-11-5) |
| `restart` | Restart policy after exit. | [4.9.1](#sec-4-9-1) |
| `runtime` | Alternative container runtime. | [4.14.5](#sec-4-14-5) |
| `scale` | Number of container replicas. | [4.16.3](#sec-4-16-3) |
| `secrets` | Mount secrets declared at the top level. | [4.15.2](#sec-4-15-2) |
| `security_opt` | Override default labeling / seccomp / no-new-privileges. | [4.11.4](#sec-4-11-4) |
| `shm_size` | Size of `/dev/shm`. | [4.12.21](#sec-4-12-21) |
| `stdin_open` | Keep the container's stdin open. | [4.3.8](#sec-4-3-8) |
| `stop_grace_period` | Wait before escalating stop to SIGKILL. | [4.9.2](#sec-4-9-2) |
| `stop_signal` | Signal used to stop the container. | [4.9.3](#sec-4-9-3) |
| `storage_opt` | Per-container storage driver options. | [4.17.1](#sec-4-17-1) |
| `sysctls` | Kernel parameters set in the container namespace. | [4.17.2](#sec-4-17-2) |
| `tmpfs` | Mount in-memory temporary filesystems. | [4.7.3](#sec-4-7-3) |
| `tty` | Allocate a pseudo-TTY. | [4.3.9](#sec-4-3-9) |
| `ulimits` | Per-process resource limits. | [4.12.22](#sec-4-12-22) |
| `use_api_socket` | Expose the engine API socket to the container. | [4.17.3](#sec-4-17-3) |
| `user` | User (and group) the process runs as. | [4.3.4](#sec-4-3-4) |
| `userns_mode` | User namespace mode. | [4.11.6](#sec-4-11-6) |
| `uts` | UTS namespace mode. | [4.14.3](#sec-4-14-3) |
| `volumes` | Mount volumes, binds, and tmpfs into the container. | [4.7.1](#sec-4-7-1) |
| `volumes_from` | Reuse another container's mounts wholesale. | [4.7.2](#sec-4-7-2) |
| `working_dir` | Working directory for the process. | [4.3.3](#sec-4-3-3) |

<a id="sec-4-2"></a>
### 4.2 Image and build
<a id="sec-4-2-1"></a>
#### 4.2.1 `image`
The image reference to run, in the usual `[registry/][namespace/]name[:tag|@digest]` form. Omitting the tag means `latest`, which makes deployments non-reproducible; pin a tag or a digest in anything you care about.

```yaml
services:
  db:
    image: postgres:16.2
  api:
    image: ghcr.io/acme/api@sha256:5f2c…
```

`image` is optional when `build:` is present — the built image is tagged and used automatically. When **both** are present, the build result is tagged with the `image` value, which is how you build locally and push under a real name.

<a id="sec-4-2-2"></a>
#### 4.2.2 `build`
Describes how to produce the service's image from source instead of (or in addition to) pulling it. The short form is just a build context path; the long form is a map.

```yaml
services:
  web:
    build: .                     # short form: context only
  api:
    build:
      context: ./api             # directory or Git URL sent to the builder
      dockerfile: Dockerfile.prod
      target: runtime            # stop at this multi-stage build stage
      args:
        NODE_ENV: production     # values for ARG in the Dockerfile
```

Core fields:

| Field | Meaning |
| --- | --- |
| `context` | Build context: a path relative to the project directory, or a Git repository URL |
| `dockerfile` | Alternative Dockerfile path, relative to `context` |
| `dockerfile_inline` | Dockerfile content written inline instead of read from disk (mutually exclusive with `dockerfile`) |
| `target` | Build only up to the named stage of a multi-stage Dockerfile |
| `args` | Build arguments; map or `KEY=VALUE` list. A bare key takes its value from the environment at build time |

Advanced fields, all optional:

| Field | Meaning |
| --- | --- |
| `additional_contexts` | Named extra contexts (`name: path\|URL\|service:xxx`) usable as `--from=name` |
| `cache_from` / `cache_to` | Cache sources and destinations, e.g. `type=registry,ref=…`, `type=gha`, `type=local,dest=…` |
| `no_cache` | Build without reusing any cache layers |
| `pull` | Always re-pull base images referenced by `FROM` |
| `tags` | Extra tags to apply to the built image, beyond `image:` |
| `platforms` | Target platforms to build for (`linux/amd64`, `linux/arm64`, …) |
| `labels` | Labels baked into the **image** (distinct from container `labels:`, [§4.10.2](#sec-4-10-2)) |
| `network` | Network the build containers use (`host`, `none`, or a network name) |
| `extra_hosts` | Extra `/etc/hosts` entries during the build |
| `shm_size` | `/dev/shm` size for build containers |
| `ulimits` | ulimits for build containers, same shape as [§4.12.22](#sec-4-12-22) |
| `isolation` | Build isolation technology (Windows) |
| `privileged` | Run build steps with elevated privileges |
| `secrets` | Build-time secrets, referencing top-level `secrets` ([§8](#sec-8)); exposed to `RUN --mount=type=secret` and never stored in a layer |
| `ssh` | SSH agent sockets or keys exposed to `RUN --mount=type=ssh` (`default` or `id=path`) |
| `entitlements` | Extra builder entitlements such as `network.host` or `security.insecure` |

```yaml
    build:
      context: .
      cache_from:
        - type=registry,ref=ghcr.io/acme/api:cache
      platforms: ["linux/amd64", "linux/arm64"]
      secrets:
        - npm_token
      ssh: ["default"]
```

***WSLCC note:*** relative `context` resolves against the compose file directory, and the build tag defaults to `<project>-<service>` unless `image:` is also set.

<a id="sec-4-2-3"></a>
#### 4.2.3 `pull_policy`
Controls when the runtime fetches the image rather than using a local copy.

```yaml
services:
  api:
    image: acme/api:1.4
    pull_policy: always
```

| Value | Behavior |
| --- | --- |
| `always` | Pull every time the container is created |
| `never` | Never pull; fail if the image is not present locally |
| `missing` (default) | Pull only when the image is absent locally |
| `if_not_present` | Synonym for `missing` |
| `build` | Always rebuild from `build:` instead of pulling |
| `refresh` / `daily` / `weekly` / `every_<duration>` | Pull when the local copy is older than the given interval |

`build` requires a `build:` section. Note that `always` and digest-pinned images interact predictably: a digest reference can never change, so the pull is a no-op after the first fetch.

<a id="sec-4-2-4"></a>
#### 4.2.4 `platform`
The OS/architecture the service's container should run as, in `os[/arch[/variant]]` form. Useful when a host can emulate other architectures and you need a specific one, for example running an `amd64`-only image on an ARM machine.

```yaml
services:
  legacy:
    image: acme/legacy:1.0
    platform: linux/amd64
```

If the value is also relevant to building, it must be one of the `build.platforms` entries. Common values are `linux/amd64`, `linux/arm64`, `linux/arm/v7`, and `windows/amd64`.

<a id="sec-4-3"></a>
### 4.3 Process identity and I/O
<a id="sec-4-3-1"></a>
#### 4.3.1 `command`
Overrides the image's default command (`CMD`). Two forms with different semantics:

```yaml
command: ["npm", "run", "worker"]   # exec form: argv tokens, no shell
command: npm start                  # shell form → /bin/sh -c "npm start"
```

The **exec form** (a YAML list) passes the tokens straight to `execve`, so there is no shell: no globbing, no `&&`, no `$VAR` expansion by the shell, and signals reach your process directly. The **shell form** (a string) wraps the value in `/bin/sh -c`, giving you shell features at the cost of running your process as a child of `sh`.

An empty list (`command: []`) resets the image's `CMD` to nothing.

<a id="sec-4-3-2"></a>
#### 4.3.2 `entrypoint`
Overrides the image's `ENTRYPOINT`, using the same list/string forms and the same exec-versus-shell rules as [§4.3.1](#sec-4-3-1). The entrypoint is the executable; `command:` supplies its arguments.

```yaml
entrypoint: /usr/local/bin/wait-for-db.sh
command: ["./server", "--port", "8080"]
```

Setting `entrypoint` resets any `CMD` baked into the image unless you also set `command:`. Use `entrypoint: []` to clear the image entrypoint entirely, which is the standard trick for debugging an image whose entrypoint gets in the way.

<a id="sec-4-3-3"></a>
#### 4.3.3 `working_dir`
The directory the container's process starts in, overriding the image's `WORKDIR`. It should be an absolute path inside the container; it is created if missing.

```yaml
working_dir: /srv/app
```

It also anchors relative paths in `command:` and `entrypoint:`.

<a id="sec-4-3-4"></a>
#### 4.3.4 `user`
The user the container process runs as, overriding the image's `USER`. Accepts a name or numeric ID, optionally with a group: `user`, `uid`, `user:group`, `uid:gid`.

```yaml
user: "1000:1000"
# or
user: appuser
```

Numeric IDs always work; names must exist in the image's `/etc/passwd`. Numeric form is the safer choice for bind mounts, because file ownership on the host is expressed in IDs and not names. For extra groups, see [§4.11.8](#sec-4-11-8).

<a id="sec-4-3-5"></a>
#### 4.3.5 `hostname`
Sets the container's own hostname — what `hostname` and `uname -n` report inside it, and the name written into its `/etc/hosts`.

```yaml
hostname: api-node-1
```

This is distinct from network discovery: other services still reach this one by its service name and network aliases ([§4.6.1](#sec-4-6-1)), not by this hostname. Set it when software inside the container derives identity from the hostname, such as clustered databases or license checks.

***WSLCC note:*** the value is passed through as `--hostname` on container create.

<a id="sec-4-3-6"></a>
#### 4.3.6 `domainname`
Sets the NIS/DNS domain part of the container's fully qualified name, complementing `hostname:`.

```yaml
hostname: api
domainname: internal.example.com
```

The container then reports `api.internal.example.com` as its FQDN. It affects only the container's self-identity; it does not create resolvable DNS records.

<a id="sec-4-3-7"></a>
#### 4.3.7 `mac_address`
Assigns a fixed hardware address to the container's network interface, in the usual colon-separated form.

```yaml
mac_address: 02:42:ac:11:00:42
```

Use it when something outside the container keys off the MAC: DHCP reservations, license dongles, or network appliances that filter by hardware address. Use locally administered addresses (second-least-significant bit of the first octet set, e.g. the `02:` prefix) to avoid clashing with real vendor ranges. For per-network addresses, prefer the `mac_address` field under `networks.<name>` ([§4.6.1](#sec-4-6-1)).

<a id="sec-4-3-8"></a>
#### 4.3.8 `stdin_open`
Keeps the container's standard input open even when nothing is attached, the equivalent of `docker run -i`.

```yaml
stdin_open: true
tty: true
```

Without it, a process that reads stdin sees EOF immediately and typically exits. Combine with `tty: true` to get an interactive shell-like session; on its own it is enough for a process that expects piped input.

<a id="sec-4-3-9"></a>
#### 4.3.9 `tty`
Allocates a pseudo-terminal for the container, the equivalent of `docker run -t`.

```yaml
tty: true
```

Programs detect a TTY and change behavior: colored output, progress bars, line-buffered instead of block-buffered writes, and interactive prompts. It is also a common workaround for keeping an otherwise idle container alive during debugging. Do not enable it for production services whose logs are collected by machine — the TTY layer mangles stream separation between stdout and stderr.

<a id="sec-4-3-10"></a>
#### 4.3.10 `attach`
Controls whether the tool streams this service's output when running in the foreground or following logs.

```yaml
services:
  noisy-sidecar:
    image: fluent/fluent-bit
    attach: false
```

Defaults to `true`. Setting it to `false` keeps the container running normally but hides its output from the aggregated stream — useful for chatty sidecars that would drown out the service you actually care about. You can still read those logs explicitly by naming the service.

<a id="sec-4-3-11"></a>
#### 4.3.11 `init`
Runs the container's process under a minimal init program that becomes PID 1.

```yaml
init: true
```

PID 1 in a container has two unusual duties: reaping orphaned child processes and forwarding signals. Most application runtimes do neither, which produces zombie processes and containers that ignore `SIGTERM` and have to be killed. An init shim handles both. Enable it whenever your entrypoint spawns children or the image was not written with PID 1 in mind.

<a id="sec-4-4"></a>
### 4.4 Environment
<a id="sec-4-4-1"></a>
#### 4.4.1 `environment`
Environment variables set on the container. Two interchangeable shapes:

```yaml
environment:                 # map form
  POSTGRES_USER: app
  LOG_LEVEL: debug
  DEBUG:                     # bare key: inherit value from the host environment

environment:                 # list form
  - POSTGRES_USER=app
  - LOG_LEVEL=debug
  - DEBUG                    # bare key
```

Values are strings; quote anything YAML would otherwise coerce, such as `"true"`, `"1.10"`, or `"0755"`. Variables set here override the same names loaded through `env_file:` ([§4.4.2](#sec-4-4-2)). Interpolation (`${VAR}`) is applied to values before the container is created, so `LOG_LEVEL: ${LOG_LEVEL:-info}` reads from your shell or `.env` at resolution time.

A **bare key** (no `=`, or an explicitly null value) is a pass-through: the variable is set on the container using whatever value the tool's own process environment holds.

***WSLCC note:*** that pass-through value comes from **`wslccd`'s** process environment, not from your client shell and not from the `.env` used for interpolation.

<a id="sec-4-4-2"></a>
#### 4.4.2 `env_file`
Loads container environment variables from one or more files, keeping bulk configuration out of the YAML.

```yaml
env_file: ./default.env        # single path

env_file:                      # list, applied in order
  - ./common.env
  - path: ./optional.env       # long form
    required: false
  - path: ./secrets.env
    format: raw
```

| Field | Meaning |
| --- | --- |
| `path` | File path, relative to the project directory |
| `required` | When `false`, a missing file is skipped instead of failing (default `true`) |
| `format` | `raw` disables quote/escape processing and treats each line literally |

File grammar: `KEY=VALUE` per line, `#` comments, blank lines ignored, optional quotes around values. Later files in the list win over earlier ones, and `environment:` wins over all of them.

This key is unrelated to the `--env-file` command-line flag and the project `.env` file, which feed `${VAR}` **interpolation** of the YAML itself ([§2.3](#sec-2-3)) rather than the container's environment.

<a id="sec-4-5"></a>
### 4.5 Ports
<a id="sec-4-5-1"></a>
#### 4.5.1 `ports`
Publishes container ports so they are reachable from the host (and, depending on the bind address, from the wider network).

**Short syntax** — `[[HOST_IP:]HOST_PORT:]CONTAINER_PORT[/PROTOCOL]`:

```yaml
ports:
  - "8080:80"                  # host 8080 → container 80
  - "127.0.0.1:443:443/tcp"    # bind to loopback only
  - "3000"                     # container 3000 → an ephemeral host port
  - "9090-9095:9090-9095"      # contiguous range
```

Always quote these values: unquoted `22:22` is parsed by YAML as a sexagesimal number, not a string.

**Long syntax** is the map form of the same idea, spelling each part out as its own field:

```yaml
ports:
  - target: 80            # port inside the container (required)
    published: "8080"     # host port or range, as a string
    host_ip: 127.0.0.1    # bind address; default all interfaces
    protocol: tcp         # tcp (default) or udp
    app_protocol: http    # hint for tooling; no runtime effect
    mode: host            # host = publish on each node; ingress = load-balanced
    name: web             # human-readable label for the mapping
```

The long form exists because the short form runs out of room: it is the only way to set `app_protocol`, `name`, or `mode`, and it is far easier to read in review. Both forms describe the same mapping — pick one per entry, not both.

Publishing is not required for service-to-service traffic: containers on the same network reach each other on the container port directly ([§4.5.2](#sec-4-5-2)).

***WSLCC note:*** only the short syntax is accepted today; a long-form entry fails the load with an explicit error rather than being ignored.

<a id="sec-4-5-2"></a>
#### 4.5.2 `expose`
Documents which ports the service listens on for other containers on the same network. It publishes nothing to the host.

```yaml
expose:
  - "8080"
  - "9000-9010"
```

Because containers sharing a network can already reach every port on each other, `expose` is essentially metadata: it tells readers and tooling what the contract is. Use `ports:` ([§4.5.1](#sec-4-5-1)) when you need host access.

<a id="sec-4-6"></a>
### 4.6 Networks (service attachment)
<a id="sec-4-6-1"></a>
#### 4.6.1 `networks`
The networks this service joins. Services on a shared network resolve each other by service name; services with no network in common cannot talk at all, which is the usual way to isolate a database from a public-facing tier.

```yaml
services:
  api:
    networks:                    # list form: just join these
      - frontend
      - backend

  db:
    networks:                    # map form: join with per-network options
      backend:
        aliases:
          - postgres
          - primary-db
        ipv4_address: 172.20.0.10
        priority: 100
```

Per-network fields:

| Field | Meaning |
| --- | --- |
| `aliases` | Extra DNS names for this service on that network |
| `ipv4_address` / `ipv6_address` | Static address; requires the network to declare a matching IPAM subnet ([§5](#sec-5)) |
| `link_local_ips` | Additional link-local addresses |
| `mac_address` | MAC address on this specific network |
| `priority` | Attachment order; the highest-priority network becomes the default route |
| `gw_priority` | Explicit control over which network provides the default gateway |
| `driver_opts` | Driver-specific options for this attachment |
| `interface_name` | Name of the interface inside the container |

A service with no `networks:` key joins the application's implicit default network. An empty value under a name (`backend:`) means “join it with no special options”.

***WSLCC note:*** membership is applied, but the per-network option map is not; every service is reachable by its service name, and no `networks:` key means the implicit `<project>_default` network ([§10.1](#sec-10-1)).

<a id="sec-4-6-2"></a>
#### 4.6.2 `network_mode`
Replaces normal network attachment with a namespace mode. It is mutually exclusive with `networks:`.

```yaml
network_mode: host              # share the host's network stack
network_mode: none              # no networking at all
network_mode: service:proxy     # share another service's network namespace
network_mode: container:some-id # share an existing container's namespace
```

| Value | Effect |
| --- | --- |
| `bridge` | The default private network |
| `host` | No isolation: the container binds host ports directly; `ports:` becomes meaningless |
| `none` | Only a loopback interface |
| `service:<name>` | Share the namespace of another service in this project — same IP, same localhost |
| `container:<name\|id>` | Same, for a container outside the project |

The `service:` form is how sidecar patterns work: a proxy and an app that reach each other over `127.0.0.1`. Note that namespace sharing also means shared port space, so the two containers cannot both bind the same port.

<a id="sec-4-6-3"></a>
#### 4.6.3 `extra_hosts`
Adds entries to the container's `/etc/hosts`, mapping names to addresses that DNS would not otherwise resolve.

```yaml
extra_hosts:
  - "legacy-api:192.168.1.50"       # list form
  - "host.docker.internal:host-gateway"

extra_hosts:                        # map form
  legacy-api: 192.168.1.50
```

Both IPv4 and IPv6 are allowed, and the same name may appear more than once for multiple addresses. The special value `host-gateway` resolves to the host machine's address, which is the standard way to reach a service running on the host from inside a container.

<a id="sec-4-6-4"></a>
#### 4.6.4 `dns`
Sets the DNS resolvers written into the container's `/etc/resolv.conf`, replacing the defaults inherited from the runtime.

```yaml
dns: 8.8.8.8            # single value

dns:                    # list, tried in order
  - 10.0.0.53
  - 1.1.1.1
```

Set this when the container must resolve names through a corporate or split-horizon resolver. Be aware that overriding the runtime's own resolver can break service-name discovery on user-defined networks, so include the embedded resolver or keep the override to search domains only.

<a id="sec-4-6-5"></a>
#### 4.6.5 `dns_opt`
Resolver tuning options, written to the `options` line of `/etc/resolv.conf`.

```yaml
dns_opt:
  - use-vc          # force TCP instead of UDP
  - timeout:2       # seconds to wait per resolver
  - attempts:3      # retries before giving up
  - ndots:1         # dots required before a name is tried as absolute
```

`ndots` is the one that most often matters: a high value makes every short name go through the search domains first, adding a round trip to each external lookup.

<a id="sec-4-6-6"></a>
#### 4.6.6 `dns_search`
Search domains appended to unqualified hostnames during resolution.

```yaml
dns_search:
  - internal.example.com
  - example.com
```

With the above, a lookup for `billing` tries `billing.internal.example.com`, then `billing.example.com`, then `billing` itself. A single string is accepted as shorthand for a one-element list.

<a id="sec-4-6-7"></a>
#### 4.6.7 `links`
A legacy mechanism that gives this service an alias for another container and creates the corresponding `/etc/hosts` entry.

```yaml
links:
  - db                # reachable as "db"
  - db:database       # also reachable as "database"
```

Modern networks make this redundant: any two services on a shared network already resolve each other by service name, and `networks.<name>.aliases` ([§4.6.1](#sec-4-6-1)) covers the alias case more cleanly. A `links:` entry does still imply a startup-order dependency, which is the only reason you still see it in old files — express that with `depends_on:` instead ([§4.8.1](#sec-4-8-1)).

<a id="sec-4-6-8"></a>
#### 4.6.8 `external_links`
The same aliasing idea as `links:`, but pointing at containers that were created outside this Compose application.

```yaml
external_links:
  - shared-redis
  - legacy_project_db_1:db
```

The target container must already exist and share a network with this service. As with `links:`, the modern alternative is to declare the network as `external: true` ([§5](#sec-5)) and rely on ordinary name resolution.

<a id="sec-4-7"></a>
### 4.7 Volumes and mounts (service)
<a id="sec-4-7-1"></a>
#### 4.7.1 `volumes`
Mounts persistent or shared storage into the container. Three kinds of source appear here: **named volumes** (managed by the runtime, declared at [§6](#sec-6)), **bind mounts** (a host path), and **anonymous volumes** (a target with no source).

**Short syntax** — `[SOURCE:]TARGET[:MODE]`:

```yaml
volumes:
  - db-data:/var/lib/postgresql/data   # named volume
  - ./config:/etc/app:ro               # bind mount, read-only
  - /var/log                           # anonymous volume
  - ~/.ssh:/root/.ssh:ro               # host home directory
```

A source starting with `.` or `/` (or `~`) is a bind mount; anything else is a named volume. Relative host paths resolve against the project directory. `MODE` is a comma-separated list, most commonly `ro` or `rw`, and may include SELinux relabeling flags `z` (shared) or `Z` (private).

**Long syntax** spells the same mount out as a map, which is the only way to reach the type-specific option groups:

```yaml
volumes:
  - type: volume                # volume | bind | tmpfs | npipe | cluster | image
    source: db-data
    target: /var/lib/postgresql/data
    read_only: false
    volume:
      nocopy: true              # don't copy existing image content into a fresh volume
      subpath: pgdata           # mount only this path from within the volume

  - type: bind
    source: ./config
    target: /etc/app
    bind:
      propagation: rshared      # rprivate | private | rshared | shared | rslave | slave
      create_host_path: true    # create the source directory if missing
      selinux: z
      recursive: enabled

  - type: tmpfs
    target: /run
    tmpfs:
      size: 64mb                # bytes or a size string
      mode: 0o1777              # octal permission bits
```

Common fields are `type`, `source` (not used by `tmpfs`), `target`, `read_only`, and `consistency`; then exactly one of the `volume` / `bind` / `tmpfs` / `image` option blocks, matching `type`.

***WSLCC note:*** short syntax and long-form `type: volume` / `bind` / `tmpfs` are applied (`-v`, `--mount`, or `--tmpfs`). Long-form `type: npipe`, `cluster`, and `image` fail the load with an explicit error. Declared named volumes are created as `<project>_<name>`, while `external: true` volumes keep their bare name ([§10.1](#sec-10-1)).

<a id="sec-4-7-2"></a>
#### 4.7.2 `volumes_from`
Mounts every volume already attached to another container, inheriting its mount points wholesale.

```yaml
volumes_from:
  - data-container            # another service in this project
  - service_name:ro           # inherit, but force read-only
  - container:legacy_data     # a container outside the project
```

This is the old "data container" pattern from before named volumes existed. It is blunt — you get all of the source's mounts, with no way to select — so prefer declaring a named volume and mounting it in both services explicitly.

<a id="sec-4-7-3"></a>
#### 4.7.3 `tmpfs`
Mounts an in-memory filesystem at the given path. Contents live in RAM and disappear when the container stops, and nothing is ever written to disk.

```yaml
tmpfs: /run                   # single path

tmpfs:                        # list, with optional per-mount options
  - /tmp
  - /run:size=64m,mode=1777
```

The two usual reasons are speed (scratch space for a build or a cache) and secrecy (never persisting decrypted material). It pairs naturally with `read_only: true` ([§4.11.5](#sec-4-11-5)), which otherwise breaks images that expect to write to `/tmp` or `/run`. For finer control, use the long-form `type: tmpfs` mount in [§4.7.1](#sec-4-7-1).

***WSLCC note:*** applied as `run --tmpfs` (including `size=` / `mode=` suffixes). Long-form `volumes:` with `type: tmpfs` is also applied.

<a id="sec-4-8"></a>
<a id="startup-order-and-health"></a>
### 4.8 Dependencies and health
<a id="sec-4-8-1"></a>
#### 4.8.1 `depends_on`
Declares that this service needs other services, which both orders startup and — in the long form — makes the dependent wait until the dependency is genuinely ready.

```yaml
depends_on:
  - redis                            # short form → service_started, required

depends_on:                          # long form
  db:
    condition: service_healthy
    required: true
  migrations:
    condition: service_completed_successfully
  metrics:
    condition: service_started
    required: false
  config-provider:
    condition: service_healthy
    restart: true
```

| Condition | Waits for |
| --- | --- |
| `service_started` (default) | The container to have been started; no readiness guarantee |
| `service_healthy` | The dependency's healthcheck to report healthy — it must define one ([§4.8.2](#sec-4-8-2)) or inherit one from its image |
| `service_completed_successfully` | The dependency to run to completion and exit `0`, the usual shape for migrations and seed jobs |

Long-form fields:

- **`condition`** — one of the three above.
- **`required`** — when `false`, a dependency that is missing or fails to reach its condition produces a warning instead of aborting this service. Defaults to `true`.
- **`restart`** — when `true`, restarting the dependency also restarts this service. This is what you want for a service that reads configuration or connection state from another at startup and cannot pick up changes on its own.

Shutdown walks the same graph in reverse: dependents stop before the services they depend on.

***WSLCC note:*** condition waits are capped at **5 minutes**, after which the dependent fails; an unrecognized `condition` value is treated as `service_started`; `start` / `restart` / `logs` follow dependency order and `stop` / `down` reverse it.

<a id="sec-4-8-2"></a>
#### 4.8.2 `healthcheck`
Defines how the runtime decides whether the container is *working*, as opposed to merely *running*. The result drives `service_healthy` dependencies ([§4.8.1](#sec-4-8-1)) and shows up in status output.

```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U postgres || exit 1"]
  interval: 5s
  timeout: 3s
  retries: 5
  start_period: 30s
  start_interval: 2s
```

`test` accepts three forms:

| Form | Meaning |
| --- | --- |
| `["CMD", "curl", "-f", "http://localhost/health"]` | Run the argv directly, no shell |
| `["CMD-SHELL", "curl -f http://localhost/health"]` | Run the string through the container's shell |
| `curl -f http://localhost/health` (plain string) | Shorthand for `CMD-SHELL` |
| `["NONE"]` | Disable a healthcheck inherited from the image |

Timing fields, all accepting durations like `10s`, `1m30s`, `500ms`:

- **`interval`** — how often the check runs once the container is past its start period.
- **`timeout`** — how long a single check may take before it counts as a failure.
- **`retries`** — consecutive failures required before the container flips to unhealthy.
- **`start_period`** — an initial grace window during which failures do not count toward `retries`, meant for slow-booting services. The container becomes healthy as soon as one check passes, so a generous value costs nothing.
- **`start_interval`** — a shorter probe interval used *during* `start_period`, so a service that boots in three seconds is marked healthy in three seconds rather than waiting for the first full `interval`.

`disable: true` is an alternative to `test: ["NONE"]` for switching off an inherited check.

***WSLCC note:*** the `test` argv is flattened to a single shell command; `start_interval` is not applied.

<a id="sec-4-9"></a>
### 4.9 Restart, stop, and lifecycle hooks
<a id="sec-4-9-1"></a>
#### 4.9.1 `restart`
What the runtime does when the container exits.

```yaml
restart: unless-stopped
```

| Value | Behavior |
| --- | --- |
| `no` (default) | Never restart automatically |
| `always` | Always restart, including after a host or daemon restart |
| `on-failure` | Restart only on a non-zero exit code |
| `on-failure:3` | Same, giving up after 3 attempts |
| `unless-stopped` | Like `always`, except a container you stopped by hand stays stopped |

Quote `no` — bare `no` is parsed by YAML as the boolean `false`. For one-shot jobs, keep the default so a successful run does not loop. `unless-stopped` is usually the right choice for long-lived services on a developer machine.

***WSLCC note:*** the value is passed straight through as `--restart`; the preview `wslc` backend may reject the flag ([providers.md](providers.md)).

<a id="sec-4-9-2"></a>
#### 4.9.2 `stop_grace_period`
How long to wait after the stop signal before the container is killed outright.

```yaml
stop_grace_period: 1m30s
```

On stop, the runtime sends `stop_signal` ([§4.9.3](#sec-4-9-3)) and starts this timer; if the process has not exited when it expires, it receives `SIGKILL` and loses any chance to clean up. The default is 10 seconds, which is too short for services that must drain connections, flush buffers, or finish an in-flight transaction. Accepts duration strings such as `30s`, `2m`, `1m30s`.

<a id="sec-4-9-3"></a>
#### 4.9.3 `stop_signal`
The signal sent to PID 1 when the container is asked to stop, overriding the default `SIGTERM`.

```yaml
stop_signal: SIGQUIT
```

Some servers assign their own meaning to signals — nginx treats `SIGQUIT` as graceful shutdown while `SIGTERM` is abrupt, and Apache uses `SIGWINCH`. Give the name (`SIGINT`) or the number (`2`). Whichever signal you choose only helps if PID 1 actually forwards it, which is where `init: true` ([§4.3.11](#sec-4-3-11)) comes in.

<a id="sec-4-9-4"></a>
#### 4.9.4 `pre_start`
A hook that runs before the service's container starts, letting you prepare state that the container itself should not be responsible for.

```yaml
pre_start:
  - command: ["/bin/sh", "-c", "mkdir -p /data/uploads && chown 1000 /data/uploads"]
    user: root
    privileged: true
    environment:
      - SETUP=1
    working_dir: /data
```

Each entry is a map with `command` (exec or shell form), plus optional `user`, `privileged`, `environment`, and `working_dir`. Hooks run in the order listed, and a failing hook prevents the container from starting.

<a id="sec-4-9-5"></a>
#### 4.9.5 `post_start`
A hook that runs immediately after the container starts, using the same fields as `pre_start` ([§4.9.4](#sec-4-9-4)).

```yaml
post_start:
  - command: ["chown", "-R", "app:app", "/var/lib/data"]
    user: root
```

The classic use is fixing ownership or permissions on a freshly created volume mount, which cannot be done before the mount exists. The hook runs concurrently with the container's main process, so do not treat it as an initialization barrier — for that, use a dependency on a job service with `service_completed_successfully` ([§4.8.1](#sec-4-8-1)).

<a id="sec-4-9-6"></a>
#### 4.9.6 `pre_stop`
A hook that runs before the container is stopped, with the same fields as `pre_start` ([§4.9.4](#sec-4-9-4)).

```yaml
pre_stop:
  - command: ["/usr/local/bin/deregister-from-lb.sh"]
```

It gives you a place to hang graceful-shutdown work that belongs outside the application: deregistering from a load balancer, flushing a cache to disk, or signalling a peer. It does not run when the container exits on its own or is killed.

<a id="sec-4-10"></a>
### 4.10 Identity, metadata, profiles, extends
<a id="sec-4-10-1"></a>
#### 4.10.1 `container_name`
A fixed name for the container, replacing the generated one.

```yaml
container_name: billing-postgres
```

Names must be unique across the whole host, so a service with `container_name` cannot be scaled beyond one replica, and two projects using the same value cannot run side by side. Set it only when something external must address the container by an exact name.

***WSLCC note:*** without it the container is named `<project>-<service>`; duplicates across services fail `up`; the network alias remains the service key regardless.

<a id="sec-4-10-2"></a>
#### 4.10.2 `labels`
Arbitrary key/value metadata attached to the container, readable by tooling and usable as a filter.

```yaml
labels:                                        # map form
  com.example.description: "Billing API"
  com.example.team: payments

labels:                                        # list form
  - "com.example.description=Billing API"
  - "com.example.team=payments"
```

Reverse proxies, log shippers, and backup tools are commonly configured entirely through labels. Use reverse-DNS keys to avoid collisions, and remember these are **container** labels — image labels are set under `build.labels` ([§4.2.2](#sec-4-2-2)).

***WSLCC note:*** WSLCC sets `wslcc.project`, `wslcc.service`, and `wslcc.config-hash` itself, and those win on a key clash.

<a id="sec-4-10-3"></a>
#### 4.10.3 `label_file`
Loads container labels from files, using the same `KEY=VALUE` line format as `env_file:` ([§4.4.2](#sec-4-4-2)).

```yaml
label_file: ./labels/common.labels

label_file:
  - ./labels/common.labels
  - ./labels/team.labels
```

Useful when labels are generated by a build step or shared across many services. Later files override earlier ones, and inline `labels:` override everything loaded from files.

<a id="sec-4-10-4"></a>
#### 4.10.4 `annotations`
OCI annotations on the container — metadata that travels through the OCI runtime rather than sitting in the engine's own label store.

```yaml
annotations:                                   # map form
  org.opencontainers.image.source: https://github.com/acme/api

annotations:                                   # list form
  - org.opencontainers.image.source=https://github.com/acme/api
```

Runtimes and sandboxing layers read annotations to make policy decisions, so this is where you put hints intended for the runtime rather than for your own tooling. Where labels and annotations overlap, labels are the more widely supported choice.

<a id="sec-4-10-5"></a>
<a id="profiles"></a>
#### 4.10.5 `profiles`
Marks a service as belonging to one or more named profiles, so it starts only when one of them is active. This is how a single file can hold optional pieces — debuggers, seeders, admin UIs — without starting them every time.

```yaml
services:
  web:
    image: nginx                # no profiles: always started

  debug-proxy:
    image: mitmproxy/mitmproxy
    profiles: ["debug"]

  seed:
    image: acme/seeder
    profiles: ["debug", "tools"]
```

A service with no `profiles:` key is always enabled. A service with profiles is enabled when at least one of them is activated, via `--profile`, the `COMPOSE_PROFILES` environment variable, or by naming the service directly on the command line. `--profile "*"` activates everything.

Dependencies deserve care: a service in an inactive profile is removed from the model entirely, so anything depending on it must either be in the same profile or tolerate its absence.

***WSLCC note:*** the list form is required — a scalar `profiles: debug` is read as “no profiles”, so the service is always on. Filtering happens client-side, `depends_on` references to disabled services are pruned, and dependencies are not auto-activated.

<a id="sec-4-10-6"></a>
#### 4.10.6 `extends`
Builds a service on top of another definition, in the same file or a different one. Use it to share a common base — image, environment, healthcheck — across several similar services.

```yaml
services:
  base-app:
    image: acme/app:1.4
    environment:
      LOG_FORMAT: json

  worker:
    extends:
      file: common/base.yaml     # optional; defaults to the current file
      service: base-app
    command: ["./worker"]
```

`extends: base-app` is shorthand for the same-file case. The base is loaded, this service's own keys are merged on top using the standard merge rules ([§2.2](#sec-2-2)), and the result replaces the `extends` block. Chains are allowed; cycles are an error.

Some keys are deliberately **not inheritable**, because they name other services and would silently mean something different in the new context: `depends_on`, `volumes_from`, `links`, and the `service:`/`container:` forms of `network_mode`, `ipc`, `pid`, and `uts`. Extending a service that declares any of them is rejected.

`extends` differs from multi-file merge in intent: merge combines whole documents, while `extends` composes one service from another and is resolved before anything else sees the model.

<a id="sec-4-11"></a>
### 4.11 Security and privileges
<a id="sec-4-11-1"></a>
#### 4.11.1 `privileged`
Gives the container essentially unrestricted access to the host: all capabilities, all devices, and no seccomp or AppArmor confinement.

```yaml
privileged: true
```

A privileged container can load kernel modules, reconfigure the host network, and mount host filesystems — the isolation boundary is effectively gone, so treat it as running as root on the host. Legitimate uses are narrow (Docker-in-Docker, some hardware tooling), and nearly every other case is better served by a targeted `cap_add` ([§4.11.2](#sec-4-11-2)) or `devices` ([§4.12.18](#sec-4-12-18)) entry.

<a id="sec-4-11-2"></a>
#### 4.11.2 `cap_add`
Grants specific Linux capabilities beyond the default set, without going all the way to `privileged`.

```yaml
cap_add:
  - NET_ADMIN        # configure interfaces, routes, firewall rules
  - SYS_TIME         # set the system clock
  - SYS_PTRACE       # attach a debugger to other processes
```

Capabilities split root's power into individually grantable pieces. Names may be given with or without the `CAP_` prefix. Grant only what a specific tool needs, and be aware that some capabilities (`SYS_ADMIN`, `SYS_MODULE`) are close to full root on their own.

<a id="sec-4-11-3"></a>
#### 4.11.3 `cap_drop`
Removes capabilities from the default set, shrinking what a compromised process could do.

```yaml
cap_drop:
  - ALL
cap_add:
  - NET_BIND_SERVICE     # still allowed to bind ports below 1024
```

Dropping `ALL` and adding back the handful you need is the standard hardening pattern. `cap_drop` is applied after `cap_add`, so a capability listed in both is dropped.

<a id="sec-4-11-4"></a>
#### 4.11.4 `security_opt`
Overrides the container's default security labeling and confinement profiles.

```yaml
security_opt:
  - no-new-privileges:true                 # block privilege escalation via setuid
  - seccomp:./profiles/custom-seccomp.json # custom syscall filter
  - seccomp:unconfined                     # disable syscall filtering
  - apparmor:my-profile
  - label:user:USER                        # SELinux labeling
```

`no-new-privileges:true` is cheap and worth setting almost everywhere: it stops a process inside the container from gaining privileges through setuid binaries. The `unconfined` values go the other way and remove protection — sometimes necessary for debuggers or unusual runtimes, but never a default.

<a id="sec-4-11-5"></a>
#### 4.11.5 `read_only`
Mounts the container's root filesystem read-only.

```yaml
read_only: true
tmpfs:
  - /tmp
  - /run
volumes:
  - app-data:/var/lib/app
```

This turns "the attacker wrote a binary into the image" into an immediate failure, and it makes the container's writable surface explicit. Most images need a few writable paths anyway, so pair it with `tmpfs:` ([§4.7.3](#sec-4-7-3)) for scratch space and named volumes for anything that must persist.

***WSLCC note:*** `read_only: true` is emitted as `--read-only`. Writable paths still need bind or named volumes, or `tmpfs:` / long-form `type: tmpfs` (both applied as `--tmpfs`).

<a id="sec-4-11-6"></a>
#### 4.11.6 `userns_mode`
Controls user-namespace remapping for the container.

```yaml
userns_mode: host      # disable remapping; container UIDs are host UIDs
```

When the daemon is configured with user-namespace remapping, root inside the container maps to an unprivileged host user, so a container escape lands as nobody rather than as root. `host` opts this service out of that mapping, which is occasionally required for bind mounts whose ownership must line up exactly, or for containers that need to see real host UIDs.

<a id="sec-4-11-7"></a>
#### 4.11.7 `credential_spec`
Supplies a group Managed Service Account credential specification for Windows containers, so the containerized process can authenticate to Active Directory without a machine account.

```yaml
credential_spec:
  file: my-credential-spec.json     # relative to the project directory

credential_spec:
  registry: my-credential-spec      # from the Windows registry on the host

credential_spec:
  config: my-cred-config            # a top-level config object
```

Exactly one of `file`, `registry`, or `config` is used. This applies to Windows containers only.

<a id="sec-4-11-8"></a>
#### 4.11.8 `group_add`
Adds supplementary groups to the container process, on top of the primary group implied by `user:` ([§4.3.4](#sec-4-3-4)).

```yaml
user: "1000:1000"
group_add:
  - "1001"          # numeric GID
  - docker          # group name from the image's /etc/group
```

The usual reason is access to a mounted device or socket whose group ownership is what grants permission — mounting the engine socket and adding the `docker` group, for instance. Numeric GIDs are safer because they do not depend on the image's group database.

<a id="sec-4-12"></a>
### 4.12 Resources and devices
<a id="sec-4-12-1"></a>
#### 4.12.1 `cpus`
A hard ceiling on CPU time, expressed in fractional cores.

```yaml
cpus: 1.5      # at most one and a half cores' worth of CPU time
```

The container may still be scheduled across many cores; the limit is on total time consumed per period, not on which cores are used. Implemented as a CFS quota, so `cpus: 1.5` is equivalent to `cpu_quota: 150000` with the default `cpu_period: 100000`. For core pinning use `cpuset` ([§4.12.9](#sec-4-12-9)); for a relative rather than absolute share use `cpu_shares` ([§4.12.2](#sec-4-12-2)).

<a id="sec-4-12-2"></a>
#### 4.12.2 `cpu_shares`
A *relative* weight used only when CPUs are contended.

```yaml
cpu_shares: 2048      # twice the default weight of 1024
```

With two busy containers weighted 2048 and 1024, the first gets roughly two thirds of the available time. When the host is idle, weights are irrelevant and either container can use everything. Use this to express priority; use `cpus:` when you need a genuine cap.

<a id="sec-4-12-3"></a>
#### 4.12.3 `cpu_quota`
The maximum CPU time, in microseconds, the container may consume per `cpu_period`.

```yaml
cpu_quota: 50000      # with the default 100000µs period → half a core
```

This is the low-level knob behind `cpus:` ([§4.12.1](#sec-4-12-1)). Set it directly only when you also need a non-default period; otherwise `cpus:` says the same thing more legibly.

<a id="sec-4-12-4"></a>
#### 4.12.4 `cpu_period`
The length of the CFS accounting window, in microseconds, over which `cpu_quota` is measured.

```yaml
cpu_period: 50000
cpu_quota: 25000      # still half a core, but re-evaluated twice as often
```

The default is 100000 (100 ms). A shorter period smooths out latency spikes for interactive workloads because throttling is applied in finer slices; a longer one reduces scheduling overhead for batch work.

<a id="sec-4-12-5"></a>
#### 4.12.5 `cpu_rt_runtime`
CPU time reserved for real-time scheduled tasks in the container, per real-time period.

```yaml
cpu_rt_runtime: 400000     # microseconds, or "400ms"
```

Only relevant to processes using the `SCHED_FIFO` / `SCHED_RR` policies, and only on kernels and daemon configurations that permit real-time scheduling in containers. Misconfiguring it can starve the host, which is why it is off by default.

<a id="sec-4-12-6"></a>
#### 4.12.6 `cpu_rt_period`
The accounting period for real-time scheduling, the denominator to `cpu_rt_runtime`'s numerator.

```yaml
cpu_rt_period: 1000000     # 1s
cpu_rt_runtime: 400000     # 40% of each second available to RT tasks
```

Same caveats as [§4.12.5](#sec-4-12-5): real-time scheduling is a specialist feature that needs host-level enablement.

<a id="sec-4-12-7"></a>
#### 4.12.7 `cpu_count`
The number of logical processors the container may use. A Windows-container setting, with no effect on Linux.

```yaml
cpu_count: 2
```

<a id="sec-4-12-8"></a>
#### 4.12.8 `cpu_percent`
The percentage of available CPU the container may use, another Windows-container setting.

```yaml
cpu_percent: 75
```

Overlaps with `cpu_count`; on Windows hosts the runtime applies whichever of the CPU controls are set, so pick one rather than combining them.

<a id="sec-4-12-9"></a>
#### 4.12.9 `cpuset`
Pins the container to specific CPU cores, given as a comma-separated list with optional ranges.

```yaml
cpuset: "0,1"
cpuset: "0-3"
```

Unlike `cpus:`, this restricts *which* cores may be used rather than how much time is consumed. It matters for NUMA locality and cache affinity in latency-sensitive workloads, and for keeping a noisy container away from cores you have reserved for something else. Pinning too aggressively can hurt: an idle pinned core cannot be borrowed.

<a id="sec-4-12-10"></a>
#### 4.12.10 `mem_limit`
A hard memory ceiling. A container that exceeds it is killed by the kernel OOM killer.

```yaml
mem_limit: 512m
```

Accepts a byte count or a suffixed string: `b`, `k`, `m`, `g` (`512m`, `2g`). Setting a limit is how you stop a leaking service from taking the host down with it — but set it too low and you get restart loops with `OOMKilled` status and no obvious error in the logs. Some runtimes also read this to size their own heaps.

<a id="sec-4-12-11"></a>
#### 4.12.11 `mem_reservation`
A soft memory target, below `mem_limit`, used by the kernel when it needs to reclaim memory.

```yaml
mem_reservation: 256m
mem_limit: 512m
```

The container may exceed the reservation freely while the host has spare memory; under pressure, the kernel pushes containers back toward their reservations first. Think of it as a hint about the working set rather than an enforced boundary.

<a id="sec-4-12-12"></a>
#### 4.12.12 `memswap_limit`
The combined limit on memory **plus** swap.

```yaml
mem_limit: 512m
memswap_limit: 1g      # 512m RAM + 512m swap
```

The swap allowance is the difference between the two values. Setting `memswap_limit` equal to `mem_limit` disables swap for the container entirely, which is what you usually want for databases and latency-sensitive services. `-1` means unlimited swap.

<a id="sec-4-12-13"></a>
#### 4.12.13 `mem_swappiness`
How eagerly the kernel swaps out this container's anonymous pages, from `0` to `100`.

```yaml
mem_swappiness: 0
```

`0` avoids swapping until it is unavoidable; `100` swaps aggressively in favor of page cache. Use `0` for anything that manages its own memory and hates latency spikes.

<a id="sec-4-12-14"></a>
#### 4.12.14 `oom_kill_disable`
Prevents the kernel from killing the container when it hits its memory limit.

```yaml
mem_limit: 1g
oom_kill_disable: true
```

Instead of being killed, processes are frozen when they cannot allocate — which usually means a hung container rather than a healthy one. Only meaningful together with `mem_limit`, and dangerous without careful monitoring; on a host without a limit set it can wedge the whole machine.

<a id="sec-4-12-15"></a>
#### 4.12.15 `oom_score_adj`
Biases the kernel's choice of victim when the *host* runs out of memory, from `-1000` (kill last) to `1000` (kill first).

```yaml
oom_score_adj: -500      # protect this service
```

This is about relative priority across everything on the host, not about the container's own limit. Give critical services a negative value and disposable batch workloads a positive one.

<a id="sec-4-12-16"></a>
#### 4.12.16 `pids_limit`
Caps the number of processes and threads the container may create.

```yaml
pids_limit: 512
```

It is the cheapest defense against a fork bomb or a runaway thread pool exhausting the host's global PID space, which would prevent *any* new process from starting anywhere on the machine. `-1` means unlimited. Remember that threads count, so JVM and Go services need more headroom than the process count suggests.

<a id="sec-4-12-17"></a>
#### 4.12.17 `blkio_config`
Block-IO limits: a relative weight for contention, plus absolute per-device caps.

```yaml
blkio_config:
  weight: 300                # 10–1000, default 500; relative IO priority
  weight_device:
    - path: /dev/sda
      weight: 400
  device_read_bps:
    - path: /dev/sdb
      rate: '12mb'
  device_write_bps:
    - path: /dev/sdb
      rate: 1024k
  device_read_iops:
    - path: /dev/sdb
      rate: 120
  device_write_iops:
    - path: /dev/sdb
      rate: 30
```

| Field | Meaning |
| --- | --- |
| `weight` | Relative share of IO bandwidth when devices are contended (10–1000) |
| `weight_device` | Per-device overrides of `weight`, each `{path, weight}` |
| `device_read_bps` / `device_write_bps` | Absolute throughput caps, each `{path, rate}`; rate as bytes or a suffixed string |
| `device_read_iops` / `device_write_iops` | Absolute operations-per-second caps, each `{path, rate}` |

`path` is a device on the **host**. Weights only bite under contention; the bps/iops caps apply always, which makes them the right tool for keeping a backup job from starving a database.

<a id="sec-4-12-18"></a>
#### 4.12.18 `devices`
Maps host devices into the container, using `HOST_PATH:CONTAINER_PATH[:PERMISSIONS]`.

```yaml
devices:
  - "/dev/ttyUSB0:/dev/ttyUSB0"
  - "/dev/sda:/dev/xvda:rwm"
  - "/dev/kvm"
```

Permissions are any combination of `r` (read), `w` (write), and `m` (mknod), defaulting to all three. The container path may differ from the host path, which is handy when an application insists on a fixed device name. This is the targeted alternative to `privileged: true` for serial ports, GPUs, and other hardware.

<a id="sec-4-12-19"></a>
#### 4.12.19 `device_cgroup_rules`
Raw device cgroup rules, for access that cannot be expressed as a fixed path mapping.

```yaml
device_cgroup_rules:
  - 'c 1:3 mr'       # char device 1:3 (/dev/null), read + mknod
  - 'b 8:* rmw'      # all block devices with major 8
```

Format: `TYPE MAJOR:MINOR PERMISSIONS`, where type is `c` (character), `b` (block), or `a` (all), the numbers accept `*` as a wildcard, and permissions are `r`/`w`/`m`. Use this when device nodes appear dynamically and you must grant a whole class of them.

<a id="sec-4-12-20"></a>
#### 4.12.20 `gpus`
Requests GPU devices for the container.

```yaml
gpus: all                # every GPU the host exposes

gpus:                    # long form
  - driver: nvidia
    count: 2
    capabilities: ["compute", "utility"]
  - driver: nvidia
    device_ids: ["GPU-abcd1234"]
    capabilities: ["gpu"]
```

Long-form fields are `driver`, `count` (a number or `all`), `device_ids` (specific devices, mutually exclusive with `count`), `capabilities`, and `options`. This requires the host to have the vendor's container toolkit installed. It is the modern spelling of the same request that older files expressed through `deploy.resources.reservations.devices`.

<a id="sec-4-12-21"></a>
#### 4.12.21 `shm_size`
The size of the container's `/dev/shm` shared-memory filesystem.

```yaml
shm_size: 1gb
```

The default of 64 MB is small, and several widely used systems exceed it: PostgreSQL with large parallel queries, Chromium-based browsers in test harnesses, and PyTorch data loaders all fail in confusing ways when shared memory runs out. Accepts a byte count or a suffixed string.

<a id="sec-4-12-22"></a>
#### 4.12.22 `ulimits`
Per-process resource limits inside the container, overriding the daemon's defaults.

```yaml
ulimits:
  nproc: 65535                # single value → sets both soft and hard limits
  nofile:
    soft: 20000               # enforced limit, raisable up to hard
    hard: 40000               # ceiling the process may not exceed
  core:
    soft: 0
    hard: 0                   # disable core dumps
```

Each key is a limit name (`nofile`, `nproc`, `core`, `memlock`, `stack`, …) whose value is either a single number applied to both limits, or a `{soft, hard}` map. `nofile` is by far the most commonly raised: servers holding many concurrent connections exhaust the default file-descriptor limit and fail with "too many open files".

<a id="sec-4-12-23"></a>
#### 4.12.23 `cgroup`
Selects the cgroup namespace the container runs in.

```yaml
cgroup: private     # own cgroup namespace (default)
cgroup: host        # join the host's cgroup namespace
```

With `private`, the container sees its own cgroup as the root, so tools inside it report container-scoped resource usage. With `host`, it sees the host's full cgroup hierarchy — required by monitoring agents and container-management tooling that need to inspect other cgroups, and a meaningful loss of isolation otherwise.

<a id="sec-4-12-24"></a>
#### 4.12.24 `cgroup_parent`
Places the container's cgroup under a specific parent, rather than the runtime's default location.

```yaml
cgroup_parent: /application-tier.slice
```

This lets you apply limits to a *group* of containers at once: create the parent cgroup with a memory or CPU limit, then put several services under it so they share that budget. The parent must already exist, and the value's shape depends on the host's cgroup driver (`systemd` slices versus cgroupfs paths).

<a id="sec-4-13"></a>
### 4.13 Logging
<a id="sec-4-13-1"></a>
#### 4.13.1 `logging`
Chooses where the container's stdout and stderr go, and how that destination is configured.

```yaml
logging:
  driver: json-file
  options:
    max-size: "10m"       # rotate after 10 MB
    max-file: "3"         # keep 3 rotated files
    labels: "service,env" # labels copied into each log entry
```

```yaml
logging:
  driver: syslog
  options:
    syslog-address: "tcp://192.168.0.42:123"
    tag: "{{.Name}}"
```

`driver` names the logging backend — `json-file` (the default), `local`, `none`, `syslog`, `journald`, `fluentd`, `gelf`, `awslogs`, and others provided by plugins. `options` is a free-form map whose valid keys depend entirely on the chosen driver; values must be strings, so quote numbers.

The rotation options are worth setting even in development: the default `json-file` driver grows without bound, and a chatty container can quietly fill a disk. `driver: none` discards output entirely, which also disables reading the container's logs.

<a id="sec-4-14"></a>
### 4.14 Namespaces and runtime
<a id="sec-4-14-1"></a>
#### 4.14.1 `ipc`
The IPC (inter-process communication) namespace the container uses, which governs POSIX shared memory and System V IPC objects.

```yaml
ipc: shareable            # allow other containers to join this namespace
ipc: service:database     # join another service's IPC namespace
ipc: container:some-id    # join an existing container's namespace
ipc: host                 # use the host's IPC namespace
ipc: none                 # private, with no /dev/shm mounted
```

The default is a private namespace. Sharing matters for workloads that communicate through shared memory rather than sockets — a database and its helper process, for instance. The `service:` form requires the target to be declared `shareable`.

<a id="sec-4-14-2"></a>
#### 4.14.2 `pid`
The PID namespace, which determines which processes the container can see and signal.

```yaml
pid: host                 # see and signal every process on the host
pid: service:app          # share another service's process view
pid: container:some-id
```

By default a container sees only its own processes, with its entrypoint as PID 1. `host` removes that isolation and is what monitoring and debugging tools need; it also means a process in the container can signal host processes, so treat it as a privileged setting.

<a id="sec-4-14-3"></a>
#### 4.14.3 `uts`
The UTS namespace, which owns the hostname and domain name.

```yaml
uts: host
```

The default is a private namespace, letting the container set its own hostname ([§4.3.5](#sec-4-3-5)). `host` shares the host's, so the container reports the host's name and cannot change it — occasionally required by software that cross-checks its hostname against a license or a cluster registration.

<a id="sec-4-14-4"></a>
#### 4.14.4 `isolation`
The isolation technology used to run the container. Values are platform-specific.

```yaml
isolation: hyperv
```

On Windows the choices are `process` (containers share the host kernel; faster, weaker isolation) and `hyperv` (each container gets a lightweight VM; slower, stronger, and required to run images built for a different Windows version). On Linux the only value is `default`.

<a id="sec-4-14-5"></a>
#### 4.14.5 `runtime`
Selects an alternative OCI runtime for the container instead of the daemon's default.

```yaml
runtime: runsc          # gVisor: syscall interception in userspace
runtime: kata-runtime   # Kata: lightweight VM per container
runtime: nvidia         # legacy NVIDIA runtime
```

The named runtime must be registered with the daemon beforehand. Sandboxed runtimes trade some performance and syscall compatibility for a much stronger boundary, which is a reasonable deal when running untrusted code.

<a id="sec-4-15"></a>
### 4.15 Configs and secrets (service attachment)
<a id="sec-4-15-1"></a>
#### 4.15.1 `configs`
Grants the service access to configs declared at the top level ([§7](#sec-7)), mounting each one as a file inside the container.

```yaml
services:
  web:
    configs:
      - nginx-conf                       # short form → /nginx-conf, mode 0444
      - source: app-settings             # long form
        target: /etc/app/settings.json
        uid: "1000"
        gid: "1000"
        mode: 0440

configs:
  nginx-conf:
    file: ./nginx.conf
  app-settings:
    file: ./settings.json
```

The short form mounts the config at `/<config-name>` with default ownership and mode `0444`. Long-form fields: `source` (the top-level config name), `target` (path in the container), `uid` / `gid` (numeric owner, as strings), and `mode` (octal permission bits — note that YAML reads a leading `0` as decimal unless you write `0o440` or quote it).

The point of configs over bind mounts is that content is declared in the application model rather than tied to a host path, so the same file works on any machine.

<a id="sec-4-15-2"></a>
#### 4.15.2 `secrets`
Grants the service access to secrets declared at the top level ([§8](#sec-8)). Syntactically identical to `configs:`, with a different default location and stricter intent.

```yaml
services:
  db:
    environment:
      POSTGRES_PASSWORD_FILE: /run/secrets/db_password
    secrets:
      - db_password                      # short form → /run/secrets/db_password
      - source: tls_key                  # long form
        target: /etc/ssl/private/tls.key
        uid: "0"
        gid: "0"
        mode: 0400

secrets:
  db_password:
    file: ./secrets/db_password.txt
  tls_key:
    file: ./certs/server.key
```

Short form mounts at `/run/secrets/<name>` with mode `0444`; long-form fields are `source`, `target`, `uid`, `gid`, and `mode`. Set a restrictive mode like `0400` for anything genuinely sensitive.

The reason to use secrets rather than `environment:` is exposure: environment variables show up in process listings, container inspection output, crash dumps, and child processes. A file read once at startup does not. Many images accept a `*_FILE` variable specifically to support this pattern.

<a id="sec-4-16"></a>
### 4.16 Deploy, develop, scale, provider, models
<a id="sec-4-16-1"></a>
#### 4.16.1 `deploy`
Describes how the service should be deployed at scale: how many replicas, what resources they may use, where they may be placed, and how updates and failures are handled. It originated with Swarm and is the section orchestrators read.

```yaml
deploy:
  mode: replicated
  replicas: 3
  endpoint_mode: vip
  labels:
    com.example.tier: frontend
  placement:
    constraints:
      - node.role == worker
    preferences:
      - spread: node.labels.zone
    max_replicas_per_node: 2
  resources:
    limits:
      cpus: "0.50"
      memory: 512M
      pids: 200
    reservations:
      cpus: "0.25"
      memory: 256M
      devices:
        - capabilities: ["gpu"]
          count: 1
  restart_policy:
    condition: on-failure     # none | on-failure | any
    delay: 5s
    max_attempts: 3
    window: 120s
  update_config:
    parallelism: 2
    delay: 10s
    order: start-first        # stop-first | start-first
    failure_action: rollback  # continue | rollback | pause
    monitor: 60s
    max_failure_ratio: 0.1
  rollback_config:
    parallelism: 1
    order: stop-first
```

The main blocks:

| Block | Purpose |
| --- | --- |
| `mode` / `replicas` | `replicated` runs `replicas` copies; `global` runs exactly one per node |
| `endpoint_mode` | `vip` (a single virtual IP load-balances) or `dnsrr` (DNS returns each replica) |
| `labels` | Labels on the **service** object, distinct from container `labels:` ([§4.10.2](#sec-4-10-2)) |
| `placement` | Where replicas may run: `constraints`, `preferences`, `max_replicas_per_node` |
| `resources.limits` | Hard caps (`cpus`, `memory`, `pids`) — the orchestrator-level twin of [§4.12](#sec-4-12) |
| `resources.reservations` | Guaranteed minimums, plus `devices` requests such as GPUs |
| `restart_policy` | Restart behavior with `condition`, `delay`, `max_attempts`, `window` |
| `update_config` / `rollback_config` | Rolling-update and rollback pacing and failure handling |

Note the overlap with single-host keys: `deploy.resources.limits.memory` and `mem_limit` ([§4.12.10](#sec-4-12-10)) express the same intent for different audiences, and `deploy.restart_policy` overlaps `restart:` ([§4.9.1](#sec-4-9-1)). On a single-host runtime the plain keys are the ones that take effect.

<a id="sec-4-16-2"></a>
#### 4.16.2 `develop`
Development-time behavior: what to do when files in your working tree change, so you can iterate without a manual rebuild cycle.

```yaml
develop:
  watch:
    - action: sync
      path: ./src
      target: /app/src
      ignore:
        - node_modules/
    - action: rebuild
      path: ./package.json
    - action: sync+restart
      path: ./config
      target: /app/config
```

Each `watch` rule has:

| Field | Meaning |
| --- | --- |
| `path` | Host path to watch, relative to the project directory |
| `action` | `sync` (copy changed files in), `rebuild` (rebuild the image and recreate), `restart` (restart the container), `sync+restart`, or `sync+exec` |
| `target` | Where changed files land in the container; required for `sync` actions |
| `ignore` | Glob patterns to exclude, layered on top of any `.dockerignore` |
| `exec` | For `sync+exec`, the command to run after syncing |

The usual layout pairs `sync` for source files your runtime already hot-reloads with `rebuild` for dependency manifests, where a reload is not enough.

<a id="sec-4-16-3"></a>
#### 4.16.3 `scale`
The number of containers to run for this service.

```yaml
scale: 3
```

The older, single-host spelling of `deploy.replicas` ([§4.16.1](#sec-4-16-1)); do not set both. Scaling beyond one is incompatible with `container_name:` ([§4.10.1](#sec-4-10-1)) and with fixed host ports in `ports:` ([§4.5.1](#sec-4-5-1)), since replicas would collide — use an ephemeral or ranged host port instead.

<a id="sec-4-16-4"></a>
#### 4.16.4 `provider`
Declares that the service is not a container at all, but a resource managed by an external plugin. It lets a compose file describe a managed database or cloud queue alongside ordinary services.

```yaml
services:
  database:
    provider:
      type: awesomecloud       # the plugin that manages this resource
      options:
        type: mysql
        size: 256
        name: my-db

  app:
    image: acme/app
    depends_on:
      - database
```

`type` names the provider plugin and `options` is a free-form map passed to it. A provider service has no image and no container; the plugin is responsible for creating and destroying the resource and for reporting readiness, and it may expose connection details to dependents as environment variables.

<a id="sec-4-16-5"></a>
#### 4.16.5 `models`
Attaches AI models declared in a top-level `models` section to the service.

```yaml
models:
  - llm                                # short form

models:                                # long form: bind variable names
  llm:
    endpoint_var: AI_MODEL_URL
    model_var: AI_MODEL_NAME
```

The short form injects conventionally named variables; the long form lets you choose them, so the container learns where to reach the model and which model to ask for. The model itself is declared at the top level with fields such as `model` (the image or reference), `context_size`, and `runtime_flags`.

<a id="sec-4-17"></a>
### 4.17 Miscellaneous
<a id="sec-4-17-1"></a>
#### 4.17.1 `storage_opt`
Per-container storage-driver options, most usefully a size limit on the container's writable layer.

```yaml
storage_opt:
  size: '1G'
```

Support depends entirely on the host's storage driver — `overlay2` on certain filesystems and `devicemapper` accept `size`, others reject it outright. Note that this bounds only the writable layer; volumes and bind mounts are unaffected.

<a id="sec-4-17-2"></a>
#### 4.17.2 `sysctls`
Kernel parameters set inside the container's namespaces.

```yaml
sysctls:                                   # map form
  net.core.somaxconn: 1024
  net.ipv4.tcp_syncookies: 0
  net.ipv4.ip_unprivileged_port_start: 0

sysctls:                                   # list form
  - net.core.somaxconn=1024
```

Only namespaced parameters may be set — in practice `net.*` and some `kernel.*`/`fs.*` entries — because anything else would change the host. Raising `net.core.somaxconn` for a busy server, and lowering `ip_unprivileged_port_start` so a non-root user can bind port 80, are the two you will meet most often.

<a id="sec-4-17-3"></a>
#### 4.17.3 `use_api_socket`
Makes the engine's API socket available inside the container, along with the caller's registry credentials.

```yaml
use_api_socket: true
```

This is for tools that legitimately need to drive the container engine — builders, test harnesses that spin up containers, CI agents. It is also a full privilege escalation: anything that can reach that socket can start a privileged container and own the host. Treat it exactly as you would `privileged: true` ([§4.11.1](#sec-4-11-1)).

<a id="sec-5"></a>
<a id="networks-and-volumes"></a>
## 5 Networks (top-level)
<a id="sec-5-1"></a>
### 5.1 Overview
The top-level `networks:` section declares the networks the application uses. Services join them through their own `networks:` key ([§4.6.1](#sec-4-6-1)).

```yaml
networks:
  frontend:
  backend:
    driver: bridge
  shared:
    external: true
```

An entry with an empty value gets the default driver and settings. If no network is declared and no service names one, an implicit default network is created and every service joins it — which is why service-to-service DNS "just works" in a minimal file.

***WSLCC note:*** networks are created as `<project>_<name>` and labelled so `down` can remove them; `external: true` networks keep their bare name and are never created or removed ([§10.1](#sec-10-1)).

<a id="sec-5-2"></a>
### 5.2 `driver` / `external`
**`driver`** selects the network implementation. `bridge` is the default on a single host: a private virtual network with NAT to the outside world. `host` removes network isolation. `none` disables networking. `overlay` spans multiple hosts in a cluster, and `macvlan` gives each container an address on the physical LAN. Third-party drivers add their own names.

```yaml
networks:
  backend:
    driver: bridge
```

**`external: true`** says the network is not managed by this application: it must already exist, and it will never be created or removed. This is how two separate Compose projects share a network.

```yaml
networks:
  corp-shared:
    external: true
```

<a id="sec-5-3"></a>
### 5.3 Other network attributes
The remaining attributes tune addressing, visibility, and driver behavior.

```yaml
networks:
  backend:
    name: legacy-backend-net       # exact runtime name, no project prefix
    driver: bridge
    driver_opts:
      com.docker.network.bridge.name: br-backend
      com.docker.network.driver.mtu: 1400
    attachable: true               # allow standalone containers to join
    internal: true                 # no external connectivity
    enable_ipv4: true
    enable_ipv6: true
    labels:
      com.example.owner: platform
    ipam:
      driver: default
      config:
        - subnet: 172.28.0.0/16
          ip_range: 172.28.5.0/24
          gateway: 172.28.5.254
          aux_addresses:
            router: 172.28.5.1
      options:
        foo: bar
```

| Attribute | Meaning |
| --- | --- |
| `name` | The literal runtime name, bypassing the project prefix |
| `driver_opts` | Free-form options passed to the driver (bridge name, MTU, …) |
| `attachable` | Lets containers outside the application join the network |
| `internal` | Removes the external route — containers can talk to each other but not out |
| `enable_ipv4` / `enable_ipv6` | Turn each address family on or off |
| `labels` | Metadata on the network object |
| `ipam` | Address management: `driver`, `options`, and a `config` list of `{subnet, ip_range, gateway, aux_addresses}` |

`internal: true` is the clean way to isolate a database tier: give the database an internal network plus a shared one with the API, and it becomes unreachable from outside without any firewall rules. Declaring an `ipam.config.subnet` is a prerequisite for pinning a service to a fixed `ipv4_address` ([§4.6.1](#sec-4-6-1)).

***WSLCC note:*** only `driver` and `external` are modeled; the attributes above are accepted by the parser and have no effect. Support detail: [compatibility.md](compatibility.md).

<a id="sec-6"></a>
## 6 Volumes (top-level)
<a id="sec-6-1"></a>
### 6.1 Overview
The top-level `volumes:` section declares named volumes — runtime-managed storage that outlives any individual container. Services mount them by name ([§4.7.1](#sec-4-7-1)).

```yaml
volumes:
  db-data:
  cache:
    driver: local
  shared-data:
    external: true
```

An empty value means the default driver with default options. Named volumes are the right answer for anything that must survive a recreate — database files, uploaded content, caches worth keeping — while bind mounts are for source code and configuration you edit on the host.

***WSLCC note:*** volumes are created as `<project>_<name>`; `down` keeps them unless you pass `-v` / `--volumes`; `external: true` volumes keep their bare name and are never removed ([§10.1](#sec-10-1)).

<a id="sec-6-2"></a>
### 6.2 Volume attributes
```yaml
volumes:
  db-data:
    driver: local
    driver_opts:
      type: none
      device: /mnt/fast-ssd/db
      o: bind
    labels:
      com.example.backup: nightly
    name: legacy-db-data          # exact runtime name, no project prefix

  nfs-data:
    driver: local
    driver_opts:
      type: nfs
      o: "addr=10.0.0.10,rw,nfsvers=4"
      device: ":/exports/data"

  shared-data:
    external: true

  other-project-data:
    external: true
    name: otherproject_data       # external volume under a different name
```

| Attribute | Meaning |
| --- | --- |
| `driver` | Volume plugin; `local` is the default |
| `driver_opts` | Driver-specific options — with `local`, these map onto `mount` semantics (`type`, `device`, `o`) |
| `labels` | Metadata on the volume object, useful for backup and cleanup tooling |
| `name` | The literal runtime name, bypassing the project prefix |
| `external` | The volume already exists and must not be created or removed |

The `local` driver plus `driver_opts` is more capable than it looks: as shown above it can bind a specific host directory or mount an NFS export, giving you a named volume backed by storage you control.

***WSLCC note:*** only `driver` and `external` are modeled. Support detail: [compatibility.md](compatibility.md).

<a id="sec-7"></a>
## 7 Configs (top-level)
Configs hold non-sensitive content — an nginx site file, a JSON settings blob, a feature-flag list — that services mount as files. Declaring them here means the content belongs to the application model instead of to a host path, so the same file works on every machine and can be versioned with the compose file.

```yaml
configs:
  nginx-conf:
    file: ./nginx/site.conf              # read from a file on disk

  banner:
    content: |                           # written inline in the YAML
      Welcome to the staging environment.
      Data here is reset nightly.

  build-info:
    environment: BUILD_SHA               # value taken from an environment variable

  corp-policy:
    external: true                       # already exists in the runtime
    name: corporate-policy-v3

  app-settings:
    file: ./settings.yaml
    labels:
      com.example.owner: platform
```

| Field | Meaning |
| --- | --- |
| `file` | Path to a file whose contents become the config, relative to the project directory |
| `content` | Literal content written inline; mutually exclusive with `file` |
| `environment` | Take the content from the named environment variable |
| `external` | The config already exists in the runtime and is not created by this application |
| `name` | The literal runtime name, bypassing the project prefix |
| `labels` | Metadata on the config object |

Exactly one content source (`file`, `content`, or `environment`) applies, and `external: true` replaces all of them. Services opt in through their own `configs:` list, choosing the mount path, owner, and mode ([§4.15.1](#sec-4-15-1)).

<a id="sec-8"></a>
## 8 Secrets (top-level)
Secrets are the same mechanism as configs, reserved for sensitive values: passwords, API tokens, private keys. Keeping them here rather than in `environment:` means they arrive as files with controlled permissions, and never appear in process listings or container inspection output.

```yaml
secrets:
  db_password:
    file: ./secrets/db_password.txt      # contents of the file

  api_token:
    environment: API_TOKEN               # value of an environment variable

  tls_key:
    file: ./certs/server.key

  corp_signing_key:
    external: true                       # managed outside this application
    name: signing-key-2026
```

| Field | Meaning |
| --- | --- |
| `file` | Path to a file whose contents are the secret |
| `environment` | Take the secret's value from the named environment variable |
| `external` | The secret already exists in the runtime |
| `name` | The literal runtime name, bypassing the project prefix |
| `labels` | Metadata on the secret object |

Services grant themselves access through their own `secrets:` list ([§4.15.2](#sec-4-15-2)), where the default mount point is `/run/secrets/<name>`. Two practical notes: files referenced with `file:` are still plaintext on disk, so keep them out of version control; and `environment:` is only as private as the environment it reads from.

<a id="sec-9"></a>
## 9 Fragments, merge keys, and includes
<a id="sec-9-1"></a>
### 9.1 YAML anchors and aliases
Compose files are plain YAML, so the language's own reuse features work: an **anchor** (`&name`) labels a node, an **alias** (`*name`) repeats it, and a **merge key** (`<<:`) splices a mapping into another so you can override individual fields.

```yaml
x-common: &common
  restart: unless-stopped
  environment:
    LOG_FORMAT: json
  logging:
    driver: json-file
    options:
      max-size: "10m"

services:
  api:
    <<: *common                # inherit everything from the anchor
    image: acme/api:1.4
    environment:               # note: replaces the whole mapping above
      LOG_FORMAT: json
      LOG_LEVEL: debug

  worker:
    <<: *common
    image: acme/worker:1.4
```

Anchors are resolved by the YAML parser before Compose sees the document, which has two consequences. They cannot cross file boundaries — an anchor defined in one `-f` file is invisible to the next. And a merge key replaces whole values rather than merging nested mappings, so redefining `environment:` in the child discards the parent's entries instead of adding to them; spell out both keys when you need the union.

Anchors are conventionally parked in an `x-` extension field ([§3.10](#sec-3-10)) so they are not mistaken for real services. For cross-file reuse, prefer `extends` ([§4.10.6](#sec-4-10-6)) or multi-file merge ([§2.2](#sec-2-2)), which understand Compose's per-attribute merge rules.

<a id="sec-9-2"></a>
### 9.2 Extension fields
See [§3.10](#sec-3-10).

<a id="sec-9-3"></a>
### 9.3 `include`
See [§3.9](#sec-3-9). Under WSLCC today, compose across directories with repeated `-f` plus an explicit `--project-directory`.

<a id="sec-10"></a>
## 10 WSLCC runtime behavior
<a id="sec-10-1"></a>
### 10.1 Naming, default network, teardown
| Resource | Name |
| --- | --- |
| Container (default) | `<project>-<service>` |
| Container (`container_name`) | exact value |
| Network | `<project>_<network>` |
| Volume | `<project>_<volume>` |
| Implicit network | `<project>_default` |

`down` removes project networks after the containers; named volumes are removed only with `-v` / `--volumes`. Resources declared `external: true` are never created or removed.

<a id="sec-10-2"></a>
<a id="change-detection-up"></a>
### 10.2 Change detection (`up`)
Each container carries a `wslcc.config-hash` label holding the same digest that `wslcc compose config --hash` prints. On a later `up`, a **running** container whose hash still matches is left alone and reported as `running`. A changed hash, a stopped or missing container, or `--pull` / `--build` forces a recreate.

<a id="sec-10-3"></a>
### 10.3 Progress streaming
Lifecycle RPCs stream per-service progress events (`pulling`, `building`, `creating`, `waiting`, …) followed by a completion response — see [architecture.md](architecture.md) and [daemon.md](daemon.md).

<a id="sec-10-4"></a>
### 10.4 Example
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

Runnable samples: [../examples](../examples).
