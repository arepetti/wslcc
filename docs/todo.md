# TODO / Deferred work

Tracked, intentionally-deferred work. See [roadmap.md](roadmap.md) for the big picture.

## Managed API NuGet package

- Publish a managed API package (proposed: `Wslcc.Api`) that exposes WSLCC's capabilities to other
  .NET applications as a clean, idiomatic client library — so consumers do not have to talk to the
  daemon's gRPC surface directly.
- It should wrap `Wslcc.Client` (channel + generated contracts) behind friendly async types and hide
  the transport (named pipe vs HTTP).

## Compose engine

- `config --resolve-image-digests`: pin each service image to its `repo@sha256:...` digest. Deferred
  because `config` runs offline/client-side and resolving digests needs registry access (would require a
  registry client or routing through the daemon/provider).
- `start`/`stop`/`restart`/`logs` operate on already-created containers only and do not honor
  `depends_on` ordering the way `up`/`down` do; requesting an unknown service name is silently a no-op
  rather than an error (`pull`/`build` share the "unknown service name is ignored" gap). Honor ordering
  and reject unknown service names.
- Auto-build for `up`: `up` requires services to specify an `image:` and fails on `build:`-only services
  ("building images is not supported yet"). Build automatically when an image is missing and a `build:`
  section is present (today you must run `wslcc compose build` first).
- Foreground/attached `up`: `up` runs detached only; add an attached mode with streamed logs. Also
  consider streaming per-service progress for the remaining unary RPCs
  (`Up`/`Down`/`Ps`/`Start`/`Stop`/`Restart`/`Pull`/`Build`) instead of returning a single batch result.
- `logs`: add `--timestamps`, `--no-color`, and `--since`, and sort the merged multi-container output by
  timestamp (today lines are interleaved as they arrive).
- Recreate policy: `up` always recreates existing containers by name; add change-detection so unchanged
  services are left running.
- Honor `depends_on` conditions (`service_healthy`, `service_completed_successfully`) and healthchecks;
  today ordering is start-order only.
- Networks and volumes: create/attach project networks and named volumes (currently ignored).

## Compose file fidelity

- `.env` parsing is a small subset (no multiline values or in-value expansion).
- Multi-file merge dedups exact-duplicate sequence entries; Compose's per-resource unique-key merge for
  a few list attributes (e.g. long-form `ports`/`volumes` by target) is not modeled.
- Still unsupported: `configs`/`secrets`, healthchecks, and `deploy` settings.
- Structured `ports`/`volumes` models instead of raw strings.

## WSL provider

- Enable the `WSLC_SDK` path and move from the `wslc.exe` fallback to the managed
  `Microsoft.WSL.Containers` SDK as it reaches API parity; then remove the CLI fallback.

## Daemon / remote

- Authentication and TLS for the remote HTTP/2 endpoint (currently unauthenticated/unencrypted).

## GUI

- WinUI3 app to visualize images/containers, status, metrics, and logs, talking to `wslccd` over gRPC.
