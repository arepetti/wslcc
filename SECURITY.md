# Security Policy

## Status and threat model

WSLCC is early, preview-era software built on top of the WSL containers public preview. Be aware:

- **No authentication on the named pipe beyond the OS ACL.** The `wslccd` named-pipe transport does not check a bearer token. Any process that can connect to the pipe can invoke every RPC (`Up`, `Down`, `Shutdown`, log streaming, …). That is limited to the **same Windows user and elevation** (see below). The optional remote HTTP endpoint requires **TLS and a bearer token**; it will not start without them ([docs/daemon.md — Enable remote HTTPS](docs/daemon.md#enable-remote-https)).
- **Local named pipe (default).** Kestrel's named-pipe transport defaults to `CurrentUserOnly = true`: only clients running as the **same Windows user account and the same elevation level** as the daemon can connect. Practical consequences:
  - An elevated `wslcc` (Administrator) cannot talk to a non-elevated `wslccd`, and vice versa. The CLI reports "Daemon not reachable" even when `wslcc daemon status` from a matching shell shows it running — match elevation on both sides, or restart the daemon from the shell you will use. Step-by-step: [docs/troubleshooting.md](docs/troubleshooting.md#daemon-not-reachable).
  - The default pipe name is the fixed string `wslccd`. Two signed-in users cannot both run a daemon on that name; the second fails to bind. To run a second instance, set a distinct `Wslcc:PipeName` in the daemon's `appsettings.json` and pass `--wslcc-host npipe://<name>` (or `-H` on daemon/version commands) from the CLI.
  - Any other process running as the same user (and elevation) can connect — the pipe is not a hardened cross-process security boundary beyond that.
- **Optional HTTPS endpoint.** When `Wslcc:Http:Enabled` is true, the daemon binds the host in `Http.Url` (loopback, a specific IP, or all interfaces) with **TLS**. Callers must present `Authorization: Bearer`. Plain `http://` is refused at startup. Keep `Enabled` false unless you need remote access; treat the token and private key like passwords.

## Supported versions

While the project is pre-1.0 (no 1.0 exit criteria met; see [docs/roadmap.md](docs/roadmap.md)), only `main` receives fixes. There is no tagged release yet.

## Reporting a vulnerability

Please report suspected vulnerabilities privately via GitHub Security Advisories on the repository ("Report a vulnerability"), rather than opening a public issue. Include reproduction steps and impact. As a single-maintainer, spare-time project, response times are best-effort.
