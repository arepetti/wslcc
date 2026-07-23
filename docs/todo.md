# TODO / Deferred work

Tracked, intentionally-deferred work. See [roadmap.md](roadmap.md) for the big picture.

## Managed API NuGet package

- Publish a managed API package (proposed: `Wslcc.Api`) that exposes WSLCC's capabilities to other
  .NET applications as a clean, idiomatic client library — so consumers do not have to talk to the
  daemon's gRPC surface directly.
- It should wrap `Wslcc.Client` (channel + generated contracts) behind friendly async types and hide
  the transport (named pipe vs HTTP).

## Compose engine

- `up`, `down`, `ps`, `start`, `stop`, `restart`, `pull`, `build`, and `logs` are implemented
  (container-level orchestration driven by the engine, so the same code path works for both `docker`
  and `wslc`). Remaining stubbed verb: `config`.
- `start`/`stop`/`restart`/`logs` operate on already-created containers only (via
  `ListContainersAsync`); they do not honor `depends_on` ordering the way `up`/`down` do, and requesting
  an unknown service name is silently a no-op rather than an error. `pull`/`build` have the same
  "unknown service name is ignored" limitation, but read from the compose file directly rather than
  from existing containers.
- `up` still requires services to specify an `image:`; it does not auto-build `build:`-only services
  (that always fails with "building images is not supported yet"). Run `wslcc compose build` first, or
  teach `up` to build automatically when an image is missing and a `build:` section is present.
- `up` runs detached only; add foreground/attached mode with streamed logs (`Logs` is now
  server-streaming, so `Up`/`Down`/`Ps`/`Start`/`Stop`/`Restart`/`Pull`/`Build` are the remaining unary
  RPCs — consider streaming their per-service progress too instead of returning a single batch result).
- `logs --tail`/`--follow` map directly to the provider CLI's own flags; there is no `--timestamps`,
  `--no-color`, or `--since` yet, and lines from multiple containers are interleaved as they arrive
  rather than sorted by timestamp.
- Recreate policy: `up` currently always recreates existing containers by name; add change-detection
  so unchanged services are left running.
- Honor `depends_on` conditions (`service_healthy`, `service_completed_successfully`) and healthchecks;
  today ordering is start-order only.
- Networks and volumes: create/attach project networks and named volumes (currently ignored).

## Compose file fidelity

- Full Compose specification support: `profiles`, `extends`, `configs`/`secrets`, healthchecks,
  `deploy`, variable interpolation (`${VAR}` and `.env`), and multi-file merge/override.
- Structured `ports`/`volumes` models instead of raw strings.

## WSL provider

- Enable the `WSLC_SDK` path and move from the `wslc.exe` fallback to the managed
  `Microsoft.WSL.Containers` SDK as it reaches API parity; then remove the CLI fallback.

## Daemon / remote

- Authentication and TLS for the remote HTTP/2 endpoint (currently unauthenticated/unencrypted).

## GUI

- WinUI3 app to visualize images/containers, status, metrics, and logs, talking to `wslccd` over gRPC.
