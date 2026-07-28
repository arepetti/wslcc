# Security Policy

## Status and threat model

WSLCC is early, preview-era software built on top of the WSL containers public preview. Be aware:

- **No authentication yet.** The `wslccd` daemon does not authenticate callers. Locally it listens on a Windows named pipe (access is governed by the pipe's ACL / the current user session). The optional remote HTTP/2 endpoint is **unencrypted and unauthenticated** and must only be used on trusted, isolated networks (or, preferably, not at all until authentication lands). See [docs/todo.md](docs/todo.md).
- Do not expose the daemon's HTTP endpoint to untrusted networks.

## Supported versions

While the project is pre-1.0, only the latest `main` / most recent release receives fixes.

## Reporting a vulnerability

Please report suspected vulnerabilities privately via GitHub Security Advisories on the repository ("Report a vulnerability"), rather than opening a public issue. Include reproduction steps and impact. As a single-maintainer, spare-time project, response times are best-effort.
