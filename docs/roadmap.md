# Roadmap

High-level direction. Fine-grained tasks live in [todo.md](todo.md).

## Step 1 — CLI + daemon (in progress)

- Scaffold, core library, providers, gRPC daemon, and `wslcc` CLI. **Done.**
- Vertical slice: `daemon start` → `version` → `daemon stop`. **Done.**
- Implement the real Compose lifecycle: `up`, `down`, `ps`, `logs`, `build`, `pull`, `config`.
- Full Compose file specification support.

## Step 2 — GUI

- A WinUI3 application to visualize images/containers, status, metrics, and logs. It talks to the same
  `wslccd` daemon over gRPC — no direct provider access.

## Cross-cutting

- Managed API NuGet package (`Wslcc.Api`) so other .NET apps consume WSLCC without using gRPC directly.
- Remote support hardening: authentication and TLS for the HTTP/2 endpoint.
- Windows Service install/uninstall commands.
- Move the WSL provider from the `wslc.exe` fallback to the managed `Microsoft.WSL.Containers` SDK as
  it reaches API parity.
