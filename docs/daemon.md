# The daemon (`wslccd`)

`wslccd` hosts the gRPC service that the CLI (and, later, the GUI) talk to. It runs as a per-user process in your session — started on demand or automatically at logon — and listens on a named pipe locally with an optional remote HTTP/2 endpoint. If the CLI reports the daemon is not reachable, see [troubleshooting.md](troubleshooting.md#daemon-not-reachable).

## Running

### Per-user (default)

```powershell
wslcc daemon start   # launches wslccd in the background and waits for readiness
wslcc daemon status
wslcc daemon stop
```

`wslcc daemon start` locates `wslccd` in this order: the `WSLCCD_PATH` environment variable; `wslccd.exe` next to `wslcc.exe` (published/installed layout); then, during development, the sibling project output under the .NET artifacts layout (`…/bin/Wslcc.Cli/<config>` → `…/bin/Wslccd/<config>`). You can also run `wslccd` directly for debugging (it logs to the console).

### Autostart at logon

To have the daemon start every time you sign in, register a per-user autostart:

```powershell
wslcc daemon install            # start wslccd at each logon (no elevation)
wslcc daemon install --start    # register it and start it right now too
wslcc daemon install --provider docker   # also make 'docker' the daemon's default
wslcc daemon uninstall          # remove the autostart entry
```

This is a **per-user autostart** (an HKCU Run value), not a Windows Service and not a Scheduled Task: `install` writes under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (via `reg.exe`), so it needs **no elevation** and the daemon runs in *your* session — where it can reach your WSL distros and Docker. `install` locates `wslccd` for the entry preferring the stable winget alias (`%LOCALAPPDATA%\Microsoft\WinGet\Links\wslccd.exe`) when wslcc was installed via winget, so autostart keeps working across package upgrades; otherwise it uses the same resolution as `daemon start` (`WSLCCD_PATH`, next to `wslcc.exe`, then the sibling artifacts layout in a dev checkout). `--provider` persists a default provider the same way `daemon start --provider` does. `uninstall` removes the entry but leaves a running daemon alone (stop it with `wslcc daemon stop`). CLI flags for `install`/`uninstall` are in [cli-mapping.md](cli-mapping.md#wslcc-daemon-install).

> Prefer a machine-wide service that runs without a signed-in user? The daemon still calls `UseWindowsService` (service name `WSLCC Daemon`), so an administrator can register it manually with `sc.exe create`. Note it would then run as LocalSystem in session 0, which generally cannot see a user's WSL/Docker context — the per-user autostart above is the intended model. A future MSI-based machine-wide option is sketched in [roadmap.md](roadmap.md).

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

- `PipeName` — local named pipe name (client connects with `npipe://<name>`). The default is a fixed name shared by every process on the machine; see [SECURITY.md](../SECURITY.md) for the same-user / same-elevation implications and how to use a custom name.
- `Http.Enabled` / `Http.Url` — optional remote HTTP/2 endpoint. **Unauthenticated and unencrypted** — only for trusted networks (see [SECURITY.md](../SECURITY.md)). Only the **port** from `Http.Url` is honored; the daemon always binds all interfaces (`ListenAnyIP`). You cannot restrict the endpoint to loopback via this setting today.
- `DefaultProvider` — provider used when a request does not specify one. Can be overridden at launch with `wslcc daemon start --provider <name>` (passed through as `--Wslcc:DefaultProvider`).
- `Providers` — which providers to register.

## RPCs

| RPC | Purpose |
| --- | --- |
| `Ping` | Fast readiness/liveness check (no provider calls). Used by `daemon start`/`status`. |
| `GetVersion` | Daemon version + per-provider tool versions. Used by `wslcc version` / `compose version`. |
| `Shutdown` | Graceful stop. Used by `daemon stop`. |
| `Up` | Create and start a project's containers. |
| `Down` | Stop and remove a project's containers (and optionally volumes). |
| `Ps` | List a project's containers (or every wslcc-managed container when no project is specified). |
| `Start` / `Stop` / `Restart` | Lifecycle for existing containers. |
| `Pull` | Pull service images. |
| `Build` | Build images for services with a `build:` section. |
| `Logs` | Server-streaming log lines from the project's containers. |

`wslcc compose config` is intentionally **not** an RPC: it runs entirely client-side (resolution needs the caller's files and environment). See [cli-mapping.md](cli-mapping.md#wslcc-compose-config).

## Transport details

Locally, Kestrel listens on a named pipe (`ListenNamedPipe`) speaking HTTP/2 (h2c). The client builds a `GrpcChannel` whose `SocketsHttpHandler.ConnectCallback` opens a `NamedPipeClientStream`. For remote use, Kestrel additionally binds an HTTP/2 TCP endpoint on all interfaces (see `Http.Url` above) and the client uses an `http(s)://` endpoint (`--host` for `version`/`daemon` commands, `--wslcc-host` for `compose` commands).
