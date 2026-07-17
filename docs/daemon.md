# The daemon (`wslccd`)

`wslccd` hosts the gRPC service that the CLI (and, later, the GUI) talk to. It can run either as a
per-user process or as a Windows Service, and listens on a named pipe locally with an optional remote
HTTP/2 endpoint.

## Running

### Per-user (default)

```powershell
wslcc daemon start   # launches wslccd in the background and waits for readiness
wslcc daemon status
wslcc daemon stop
```

`wslcc daemon start` locates `wslccd` via the `WSLCCD_PATH` environment variable, otherwise next to
`wslcc.exe`. You can also run `wslccd` directly for debugging (it logs to the console).

### Windows Service

The daemon uses `AddWindowsService`, so it can be registered with the Service Control Manager. Service
install/uninstall helper commands are planned (see [todo.md](todo.md)); until then you can register the
built executable manually with `sc.exe create`.

## Configuration

Bound from the `Wslcc` section of `appsettings.json`:

```json
{
  "Wslcc": {
    "PipeName": "wslccd",
    "DefaultProvider": "wslc",
    "Http": { "Enabled": false, "Url": "http://0.0.0.0:5211" },
    "Providers": { "Wslc": true, "Docker": true }
  }
}
```

- `PipeName` — local named pipe name (client connects with `npipe://<name>`).
- `Http.Enabled` / `Http.Url` — optional remote HTTP/2 endpoint. **Unauthenticated and unencrypted** —
  only for trusted networks (see [SECURITY.md](../SECURITY.md)).
- `DefaultProvider` — provider used when a request does not specify one. Can be overridden at launch
  with `wslcc daemon start --provider <name>` (passed through as `--Wslcc:DefaultProvider`).
- `Providers` — which providers to register.

## RPCs

| RPC | Purpose |
| --- | --- |
| `Ping` | Fast readiness/liveness check (no provider calls). Used by `daemon start`/`status`. |
| `GetVersion` | Daemon version + per-provider tool versions. Used by `wslcc version` / `compose version`. |
| `Shutdown` | Graceful stop. Used by `daemon stop`. |
| `Up` / `Down` / `Ps` | Create/start, stop/remove, and list a project's containers. Used by `wslcc compose up`/`down`/`ps`. |

Remaining compose operations (logs, build, pull, config, start/stop/restart) will be added to the same
service; see [todo.md](todo.md).

## Transport details

Locally, Kestrel listens on a named pipe (`ListenNamedPipe`) speaking HTTP/2 (h2c). The client builds a
`GrpcChannel` whose `SocketsHttpHandler.ConnectCallback` opens a `NamedPipeClientStream`. For remote
use, Kestrel additionally binds an HTTP/2 TCP endpoint and the client uses an `http(s)://` `--host`.
