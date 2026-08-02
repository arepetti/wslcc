# Roadmap

High-level direction and **milestones**. Fine-grained deferred work lives only in [todo.md](todo.md) — do not duplicate items here. How work is tracked in GitHub is in [CONTRIBUTING.md](../CONTRIBUTING.md#planning-and-issues).

**Maturity:** public-preview / pre-1.0 on `main`. No tagged GitHub Release or winget publication yet. Expect breaking changes until 1.0. Not production-ready.

Effort labels used below: **S** ≤ ~1 day, **M** ~few days, **L** multi-week.

## Milestone: CLI and daemon on main (complete)

Feature set on `main` (see [CHANGELOG.md](../CHANGELOG.md) `[Unreleased]`). Not a published release.

- Scaffold, core library, providers, gRPC daemon, and `wslcc` CLI. **Done.**
- Vertical slice: `daemon start` → `version` → `daemon stop`. **Done.**
- Compose lifecycle: `up`, `down`, `ps`, `logs`, `build`, `pull`, `start`/`stop`/`restart`, client-side `config`. **Done.**
- Networks/volumes, `depends_on` conditions/healthchecks, change detection, client-side resolution. **Done.**
- Per-user autostart (`daemon install` / HKCU Run). **Done.**

Remaining Compose fidelity is [todo.md](todo.md) / [compatibility.md](compatibility.md), not an open-ended “full Compose spec” gate on this milestone.

## Milestone 0.2 (hardening and first publish)

**Goal:** safe-enough defaults for local use, honest docs, and a real tagged release.

**Ordered exit criteria** (do in this order unless a dependency forces otherwise):

| # | Criterion | Size | Tracking |
| --- | --- | --- | --- |
| 1 | Auth + TLS for optional HTTP/2, or refuse to enable HTTP without them | **L** | [todo.md § Daemon / remote](todo.md#daemon--remote) — **blocks recommending remote**; security-sensitive |
| 2 | Fail loudly or apply parsed-but-ignored keys users treat as security/behavior (`user`, `env_file`, …) | **M** | [todo.md § Compose file fidelity](todo.md#compose-file-fidelity) |
| 3 | Streaming progress for long unary ops (`up`/`pull`/`build`/…) | **L** | [todo.md § Compose engine](todo.md#compose-engine) |
| 4 | Docs/CI hygiene (link check, keep `[Unreleased]`) | **S** | process |
| 5 | First tagged release (`v0.2.x`) + GitHub Release asset; winget optional | **M** | release workflow in `.github/workflows/release.yml`, `scripts/publish.ps1` |

**Definition of done for 0.2:** all rows above met or explicitly deferred in the release notes with rationale; `CHANGELOG.md` has a dated `[0.2.x]` section cut from `[Unreleased]`; a `v0.2.x` tag exists.

## Milestone 1.0 (stable surface)

**Goal:** a subset of Compose + CLI that is documented, tested, and safe enough to depend on.

**Exit criteria (draft):**

| Criterion | Size | Notes |
| --- | --- | --- |
| Documented stability for the CLI/compose subset in [compatibility.md](compatibility.md) / [compose-file.md](compose-file.md) | **M** | Breaking changes need migration notes |
| Remote endpoint safe by default (auth+TLS) **or** remote disabled in release builds | **M** | Continues 0.2 #1 |
| Install path works (`winget` and/or GitHub Release) and matches tags | **S** | |
| Production-readiness statement updated (or still explicitly “not for production”) | **S** | README + SECURITY |
| Issue milestones/`0.2`/`1.0` labels used for open work (see CONTRIBUTING) | **S** | process |

## Later (after 1.0 or opportunistic)

Not sequenced against each other; each should be an issue (or epic issue) before substantial work:

| Item | Size | Tracking |
| --- | --- | --- |
| WinUI3 GUI over `wslccd` gRPC | **L** | [todo.md § GUI](todo.md#gui) |
| Managed API NuGet (`Wslcc.Api`) | **M** | [todo.md § Managed API](todo.md#managed-api-nuget-package) |
| Machine-wide service via MSI | **L** | today: per-user HKCU Run only |
| WSL provider on `Microsoft.WSL.Containers` (`WSLC_SDK`) | **L** | [todo.md § WSL provider](todo.md#wsl-provider) |
