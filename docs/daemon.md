# The daemon (`wslccd`)

`wslccd` hosts the gRPC service that the CLI (and, later, the GUI) talk to. It runs as a per-user process in your session — started on demand or automatically at logon — and listens on a named pipe locally with an optional remote **HTTPS** endpoint. If the CLI reports the daemon is not reachable, see [troubleshooting.md](troubleshooting.md#daemon-not-reachable).

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
    "Http": {
      "Enabled": false,
      "Url": "https://0.0.0.0:5211",
      "CertificatePath": "",
      "CertificateKeyPath": "",
      "Token": "",
      "TokenFile": ""
    },
    "Providers": { "Wslc": true, "Docker": true }
  }
}
```

- `PipeName` — local named pipe name (client connects with `npipe://<name>`). The default is a fixed name shared by every process on the machine; see [SECURITY.md](../SECURITY.md) for the same-user / same-elevation implications and how to use a custom name.
- `Http` — optional **HTTPS** remote endpoint. Plain `http://` is refused. When `Enabled` is true the daemon requires a PEM certificate pair and a bearer token (see [Enable remote HTTPS](#enable-remote-https)). The host in `Url` is honored: `127.0.0.1`/`localhost` bind loopback only; `0.0.0.0`/`::` bind all interfaces; a specific IP binds that address.
- `DefaultProvider` — provider used when a request does not specify one. Can be overridden at launch with `wslcc daemon start --provider <name>` (passed through as `--Wslcc:DefaultProvider`).
- `Providers` — which providers to register.

<a id="enable-remote-https"></a>
## Enable remote HTTPS

Local use should stay on the named pipe (`npipe://wslccd`). Enable HTTP only when you need another machine to talk to this daemon. The listener is always TLS plus a shared bearer token; there is no unauthenticated or cleartext mode.

### 1. Create a certificate

On the machine that will run `wslccd`, include every name or IP clients will put in `-H`/`--wslcc-host`:

```powershell
wslcc daemon cert --hostname myserver.example.com --hostname 192.0.2.10
```

This writes `server.pem` and `server.key` under `%LOCALAPPDATA%\wslcc\certs\` (override with `--out`). It does **not** turn HTTP on and does **not** create a token. `--force` overwrites an existing pair. `--write-config` copies the cert/key paths into `appsettings.json` next to `wslccd.exe` when that file is writable (typical next to a zip/dev build; a winget layout may be read-only — then paste the printed JSON snippet yourself).

Copy `server.pem` to each client (the private key stays on the daemon host). The cert is self-signed, so it is **not** in the Windows trust store.

Rotate by running `daemon cert --force` and restarting the daemon. Clients keep using the new `server.pem` as `--tls-ca` / `WSLCC_TLS_CA`.

### 2. Create a bearer token

Pick a long random secret. Do not commit it next to the compose files.

```powershell
# example only — use a real secret
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

Put it in a file the daemon can read (for example `%LOCALAPPDATA%\wslcc\http.token`) with ACLs limited to your user.

### 3. Enable HTTP in `appsettings.json`

Edit the `Wslcc:Http` section next to `wslccd.exe` (or the paths `--write-config` already filled in):

```json
"Http": {
  "Enabled": true,
  "Url": "https://0.0.0.0:5211",
  "CertificatePath": "C:\\Users\\you\\AppData\\Local\\wslcc\\certs\\server.pem",
  "CertificateKeyPath": "C:\\Users\\you\\AppData\\Local\\wslcc\\certs\\server.key",
  "TokenFile": "C:\\Users\\you\\AppData\\Local\\wslcc\\http.token"
}
```

Prefer `TokenFile` or the environment variable `WSLCC_HTTP_TOKEN` over putting `Token` in JSON. `Url` must be `https://`. Restart the daemon (`wslcc daemon stop` then `wslcc daemon start`). If cert, key, or token is missing, `wslccd` refuses to start with a clear error.

Open the port on the Windows firewall if clients are remote.

### 4. Call from a client

On the client (same or another machine):

```powershell
$env:WSLCC_TOKEN = (Get-Content -Raw .\http.token).Trim()
$env:WSLCC_TLS_CA = "C:\path\to\server.pem"

wslcc version -H https://myserver.example.com:5211
wslcc compose ps --wslcc-host https://myserver.example.com:5211 --project-directory .
```

Equivalent flags: `--token` / `--tls-ca` on `version` and `daemon` commands; `--wslcc-token` / `--wslcc-tls-ca` on compose commands. `http://` is rejected. Named-pipe commands do not send the token.

`daemon start` still only manages a **local** pipe; you cannot use it to launch a remote process.

## RPCs

| RPC | Purpose |
| --- | --- |
| `Ping` | Fast readiness/liveness check (no provider calls). Used by `daemon start`/`status`. |
| `GetVersion` | Daemon version + per-provider tool versions. Used by `wslcc version` / `compose version`. |
| `Shutdown` | Graceful stop. Used by `daemon stop`. |
| `Up` / `Down` / `Ps` / `Start` / `Stop` / `Restart` / `Pull` / `Build` | Server-streaming lifecycle: zero or more `ServiceProgress` events, then one completed response (`UpResponse`, …). Progress carries per-service `phase`/`status` while work runs. |
| `Logs` | Server-streaming log lines from the project's containers. |

`wslcc compose config` is intentionally **not** an RPC: it runs entirely client-side (resolution needs the caller's files and environment). See [cli-mapping.md](cli-mapping.md#wslcc-compose-config).

## Transport details

Locally, Kestrel listens on a named pipe (`ListenNamedPipe`) speaking HTTP/2 (h2c). The client builds a `GrpcChannel` whose `SocketsHttpHandler.ConnectCallback` opens a `NamedPipeClientStream`. For remote use, Kestrel additionally binds **HTTPS** (HTTP/2) on the address in `Http.Url`; the client uses `https://host:port` plus a bearer token (`--host` for `version`/`daemon` commands, `--wslcc-host` for `compose` commands). See [Enable remote HTTPS](#enable-remote-https).
