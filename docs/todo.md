# TODO / Deferred work

Intentionally deferred work. **Milestones and sequencing** live in [roadmap.md](roadmap.md); this file is the backlog detail. User-facing Compose gaps: [compatibility.md](compatibility.md).

**How to use this list:** open a GitHub issue before starting non-trivial items, then put the issue number in the **Issue** column (see [CONTRIBUTING.md](../CONTRIBUTING.md#planning-and-issues)). Prefer one issue per row. Do not copy roadmap exit criteria here verbatim. Owner is the maintainer until the project has multiple committers.

| Column | Meaning |
| --- | --- |
| Priority | **P0** blocks recommending a feature / next milestone; **P1** should land in the named milestone; **P2** later / nice-to-have |
| Size | **S** / **M** / **L** (same scale as the roadmap) |
| Milestone | Target from [roadmap.md](roadmap.md) |
| Issue | GitHub issue once filed (`#N`); leave blank until work is scheduled |

## Daemon / remote

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| mTLS / Windows cert store / installing the HTTPS cert into Root | **P2** | **M** | **1.0** | | HTTPS + bearer token shipped; this is extra trust UX |

## Compose file fidelity

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| `configs` | **P2** | **M** | Later | | Same file-grant model as secrets; default target `/<name>`; `content:` source |
| `deploy` | **P2** | **L** | Later | | Not read |
| Multi-file unique-key merge for list attributes (Compose long-form ports/volumes by target) | **P2** | **M** | Later | | Exact-dedup only today |

## Compose engine

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| `config --resolve-image-digests` | **P2** | **M** | Later | | Needs registry access; `config` is offline |
| `logs --follow` global ordering (bounded reorder / watermark) | **P2** | **M** | Later | | Non-follow dumps already sort by timestamp |
| Explicit network/volume resource `name:` | **P2** | **S** | Later | | Driver options and network IPAM/static IPv4 now ship |

## WSL provider

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| Migrate CLI-backed operations when the managed API gains parity | **P2** | **M** | Later | | The 3.0.1 hybrid provider already uses the SDK wherever its metadata and discovery model is sufficient |

### WSLc 3.0.1 CLI-only capabilities

These are intentionally tracked so a future SDK release can replace each CLI call independently:

- Image builds, including build arguments, targets, labels, no-cache, pull, and secrets.
- Container labels and label-based container discovery/config-hash lookup.
- Container, network, and volume listing/filtering.
- Retained log retrieval (`follow`, `tail`, `since`, and timestamps); SDK process streams are live-only.
- Healthcheck configuration and health-state discovery.
- User selection, DNS, per-container CPU/memory limits, shared-memory size, TTY, ulimits, tmpfs/advanced mounts, and persistent stop defaults.
- Named/custom network lifecycle, multiple networks, aliases, static IPv4, IPAM, internal networks, and driver options.
- Generic volume lifecycle, labels, drivers, and driver options.

All fallback calls must remain scoped to the SDK-owned `wslcc` session with the global `--session` option. WSLc output must use the dedicated JSON parser; Docker Go templates are not compatible.

### Blocked by WSLc 3.0.1

Neither the managed API nor CLI can currently provide these faithfully:

- OCI annotations, restart policies, or a read-only root filesystem.
- Capability/security/user-namespace controls.
- Generic devices, block-I/O and extended resource controls, OOM/PID limits, sysctls, alternate runtimes, or custom logging drivers.
- Host networking, static IPv6, custom MAC addresses, extra host entries, and attachable-network semantics.
- PID/IPC/UTS namespace controls, init shims, supplementary groups, and `volumes_from`.
- `npipe`, `cluster`, and image mounts, plus external Compose config/secret stores.

## Managed API NuGet package

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| Publish `Wslcc.Api` wrapping `Wslcc.Client` | **P2** | **M** | Later | | gRPC progress streaming is in place; freeze surface before publishing |

## GUI

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| WinUI3 app over `wslccd` gRPC | **P2** | **L** | Later | | No direct provider access |
