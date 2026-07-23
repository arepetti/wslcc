# Architecture

WSLCC is split into small libraries with a clear dependency direction, two executables (the CLI and
the daemon), and pluggable providers.

## Projects

All projects target `net10.0` (see [CONTRIBUTING.md](../CONTRIBUTING.md) for the rationale).

| Project | Role |
| --- | --- |
| `Wslcc.Abstractions` | Contracts and models: `IContainerProvider`, `IComposeEngine`, `ProviderInfo`, Compose model types, `ProcessRunner` (incl. streaming process output as `IAsyncEnumerable<string>`). |
| `Wslcc.Core` | Provider-agnostic logic: Compose YAML parser and the orchestration engine (`ComposeEngine`). |
| `Wslcc.Providers.Common` | Shared base (`CliContainerProviderBase`, `CliCommandBuilder`) for providers that drive a standard container CLI. |
| `Wslcc.Providers.Wslc` | Provider for WSL containers. Container ops from the shared CLI base (`wslc`); version/availability via the SDK path (`Microsoft.WSL.Containers`) gated behind `WSLC_SDK`, with a `wslc.exe` fallback. |
| `Wslcc.Providers.DockerCompose` | Provider that drives the `docker` CLI (container ops) and `docker compose version` (version). |
| `Wslcc.Grpc.Contracts` | `.proto` definitions and generated gRPC types shared by server and clients. |
| `Wslcc.Grpc.Server` | gRPC service implementation calling the core engine. |
| `Wslcc.Client` | Typed gRPC client + transport selection (named pipe / HTTP). Shared by the CLI and future GUI. |
| `Wslccd` (exe) | The daemon: Kestrel + gRPC, Windows Service support, composition root. |
| `Wslcc.Cli` (exe) | The `wslcc` command-line tool (Spectre.Console.Cli). |

## Dependency direction

```mermaid
graph TD
  Abstractions
  Core --> Abstractions
  ProvidersCommon[Providers.Common] --> Abstractions
  ProvidersWslc[Providers.Wslc] --> ProvidersCommon
  ProvidersDocker[Providers.DockerCompose] --> ProvidersCommon
  Contracts[Grpc.Contracts]
  Server[Grpc.Server] --> Core
  Server --> Contracts
  Client --> Contracts
  Daemon[Wslccd] --> Server
  Daemon --> Core
  Daemon --> ProvidersWslc
  Daemon --> ProvidersDocker
  Cli[Wslcc.Cli] --> Client
```

Providers depend only on `Abstractions`. The daemon is the composition root: it registers the
providers and constructs the `ComposeEngine`, keeping `Core` provider-agnostic.

## Runtime flow (version slice)

```mermaid
sequenceDiagram
  participant U as wslcc CLI
  participant W as wslccd process
  participant C as Grpc client
  participant D as wslccd gRPC (named pipe)
  participant E as Core engine
  participant P as Providers
  U->>W: daemon start (launch detached wslccd, poll Ping)
  U->>C: version
  C->>D: GetVersion
  D->>E: collect versions
  E->>P: GetProviderInfoAsync()
  P-->>E: name + tool version / availability
  E-->>D: daemon + provider infos
  D-->>C: VersionResponse
  C-->>U: Spectre table
  U->>D: daemon stop -> Shutdown RPC
```

`Ping` is a fast readiness RPC that does not touch providers (used by `daemon start`/`status`), while
`GetVersion` resolves each provider's underlying tool version and may shell out to `docker`/`wslc`.

Every compose lifecycle RPC (`Up`, `Down`, `Ps`, `Start`, `Stop`, `Restart`, `Pull`, `Build`) is unary
except `Logs`, which is server-streaming: the daemon fans in one `{exe} logs [--follow]` process per
matching container (via `IContainerProvider.GetLogsAsync`, an `IAsyncEnumerable<string>`) into a single
tagged stream, and the client reads it as an `IAsyncEnumerable<LogLine>`. Cancelling the call (client
disconnect, or Ctrl+C in the CLI) kills the underlying processes so a `--follow` invocation stops
promptly instead of leaking.

## Transport

The daemon hosts gRPC over HTTP/2. Locally it listens on a Windows named pipe via Kestrel's
`ListenNamedPipe`; the client connects with a `SocketsHttpHandler.ConnectCallback` backed by
`NamedPipeClientStream`. A remote HTTP/2 endpoint can be enabled for cross-machine use (no auth yet).
See [daemon.md](daemon.md).
