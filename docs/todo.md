# TODO / Deferred work

Tracked, intentionally-deferred work. See [roadmap.md](roadmap.md) for the big picture.

## Managed API NuGet package

- Publish a managed API package (proposed: `Wslcc.Api`) that exposes WSLCC's capabilities to other
  .NET applications as a clean, idiomatic client library — so consumers do not have to talk to the
  daemon's gRPC surface directly.
- It should wrap `Wslcc.Client` (channel + generated contracts) behind friendly async types and hide
  the transport (named pipe vs HTTP).

## Compose engine

- `up`, `down`, and `ps` are implemented (container-level orchestration driven by the engine, so the
  same code path works for both `docker` and `wslc`). Remaining stubbed verbs: `logs`, `build`,
  `pull`, `config`, `start`, `stop`, `restart`.
- `up` runs detached only; add foreground/attached mode with streamed logs, and stream operation
  progress over gRPC (currently unary `Up`/`Down`/`Ps`).
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

- Windows Service registration commands: `wslcc daemon install` / `uninstall`.
- Authentication and TLS for the remote HTTP/2 endpoint (currently unauthenticated/unencrypted).

## GUI

- WinUI3 app to visualize images/containers, status, metrics, and logs, talking to `wslccd` over gRPC.
