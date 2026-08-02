# Security Policy

## Status and threat model

WSLCC is early, preview-era software built on top of the WSL containers public preview. Be aware:

- **No authentication yet.** The `wslccd` daemon does not authenticate callers. Any process that can connect to the transport can invoke every RPC (`Up`, `Down`, `Shutdown`, log streaming, …). Authentication and TLS for the remote HTTP endpoint are a **P0 exit criterion for milestone 0.2** ([docs/roadmap.md](docs/roadmap.md#milestone-02-hardening-and-first-publish), backlog row in [docs/todo.md](docs/todo.md#daemon--remote)). Until that lands, keep `Http.Enabled` false.
- **Local named pipe (default).** Kestrel's named-pipe transport defaults to `CurrentUserOnly = true`: only clients running as the **same Windows user account and the same elevation level** as the daemon can connect. Practical consequences:
  - An elevated `wslcc` (Administrator) cannot talk to a non-elevated `wslccd`, and vice versa. The CLI reports "Daemon not reachable" even when `wslcc daemon status` from a matching shell shows it running — match elevation on both sides, or restart the daemon from the shell you will use. Step-by-step: [docs/troubleshooting.md](docs/troubleshooting.md#daemon-not-reachable).
  - The default pipe name is the fixed string `wslccd`. Two signed-in users cannot both run a daemon on that name; the second fails to bind. To run a second instance, set a distinct `Wslcc:PipeName` in the daemon's `appsettings.json` and pass `--wslcc-host npipe://<name>` (or `-H` on daemon/version commands) from the CLI.
  - Any other process running as the same user (and elevation) can connect — the pipe is not a hardened cross-process security boundary beyond that.
- **Optional HTTP/2 endpoint.** When `Wslcc:Http:Enabled` is true, the daemon binds **all interfaces** on the configured port (`ListenAnyIP`); only the port from `Http.Url` is used — the host is ignored. The endpoint is **unencrypted and unauthenticated**. Do not enable it on untrusted networks; prefer leaving it disabled until authentication lands.

## Supported versions

While the project is pre-1.0 (no 1.0 exit criteria met; see [docs/roadmap.md](docs/roadmap.md)), only `main` receives fixes. There is no tagged release yet.

## Reporting a vulnerability

Please report suspected vulnerabilities privately via GitHub Security Advisories on the repository ("Report a vulnerability"), rather than opening a public issue. Include reproduction steps and impact. As a single-maintainer, spare-time project, response times are best-effort.
