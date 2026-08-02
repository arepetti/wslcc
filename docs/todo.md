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
| Authentication and TLS for the remote HTTP/2 endpoint (currently unauthenticated/unencrypted) | **P0** | **L** | **0.2** | | Security; also cited from [SECURITY.md](../SECURITY.md). Until done, leave `Http.Enabled` false. |

## Compose file fidelity

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| Apply or reject `container_name`, `user`, `working_dir`, `labels`, `entrypoint`; load `env_file` into the container (today: parsed, silent no-op — see `ComposeEngine.ToRunSpec`) | **P0** | **M** | **0.2** | | Trust / security-relevant for `user` etc. |
| Shell-form `command:` string (Compose runs via `/bin/sh -c`; we pass one argv token) | **P1** | **S** | **0.2** | | [compose-file.md#command-and-entrypoint](compose-file.md#command-and-entrypoint) |
| Structured `ports`/`volumes` (long map form) instead of short strings only | **P2** | **M** | Later | | Long form is rejected today |
| `configs` / `secrets` / `deploy` | **P2** | **L** | Later | | Not read |
| Multi-file unique-key merge for list attributes (Compose long-form ports/volumes by target) | **P2** | **M** | Later | | Exact-dedup only today |

## Compose engine

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| Stream per-service progress for unary RPCs (`Up`/`Down`/`Ps`/`Start`/`Stop`/`Restart`/`Pull`/`Build`) | **P1** | **L** | **0.2** | | Prefer before GUI / public API freeze the proto |
| `config --resolve-image-digests` | **P2** | **M** | Later | | Needs registry access; `config` is offline |
| `logs --follow` global ordering (bounded reorder / watermark) | **P2** | **M** | Later | | Non-follow dumps already sort by timestamp |
| Networks/volumes: IPAM, `ipv4_address`, `driver_opts`, explicit resource `name:` | **P2** | **M** | Later | | Only `driver` / `external` today |

## WSL provider

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| Enable `WSLC_SDK` and move to `Microsoft.WSL.Containers`; remove CLI fallback | **P2** | **L** | Later | | Gated on SDK API parity |

## Managed API NuGet package

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| Publish `Wslcc.Api` wrapping `Wslcc.Client` | **P2** | **M** | Later | | After gRPC surface settles (progress streaming) |

## GUI

| Item | Priority | Size | Milestone | Issue | Notes |
| --- | --- | --- | --- | --- | --- |
| WinUI3 app over `wslccd` gRPC | **P2** | **L** | Later | | No direct provider access |
