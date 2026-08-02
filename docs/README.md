# Documentation

Reading order by audience. Prefer the specialist page over copying the same paragraph into two places — CLI flags live in [cli-mapping.md](cli-mapping.md), daemon internals in [daemon.md](daemon.md), Compose file semantics in [compose-file.md](compose-file.md).

## New users

1. [../README.md](../README.md) — install and quick start  
2. [troubleshooting.md](troubleshooting.md) — daemon unreachable, hung `up`, discovery  
3. [cli-mapping.md](cli-mapping.md) — command reference  
4. [compatibility.md](compatibility.md) — if you already know `docker compose`

## Migrating from Docker Compose

1. [compatibility.md](compatibility.md)  
2. [compose-file.md](compose-file.md) — per-key “Applied?” table  
3. [providers.md](providers.md) — `wslc` vs `docker` backends  

## Operators / daemon

1. [daemon.md](daemon.md) — run, autostart, config, RPCs, transport  
2. [../SECURITY.md](../SECURITY.md) — pipe identity, HTTP exposure  
3. [troubleshooting.md](troubleshooting.md)  

## Contributors

1. [../CONTRIBUTING.md](../CONTRIBUTING.md)  
2. [architecture.md](architecture.md)  
3. [todo.md](todo.md) — deferred work  
4. [roadmap.md](roadmap.md) — milestones  

## Index

| Doc | Role |
| --- | --- |
| [architecture.md](architecture.md) | Project layout, dependency direction, resolution flow |
| [cli-mapping.md](cli-mapping.md) | Canonical CLI reference (`wslcc` commands and flags) |
| [compose-file.md](compose-file.md) | Canonical Compose YAML reference |
| [compatibility.md](compatibility.md) | Docker Compose differences / migration |
| [daemon.md](daemon.md) | Canonical `wslccd` internals (config, RPCs, autostart) |
| [providers.md](providers.md) | Provider backends |
| [troubleshooting.md](troubleshooting.md) | Common failures |
| [roadmap.md](roadmap.md) | Milestones |
| [todo.md](todo.md) | Fine-grained deferred work |
| [../CHANGELOG.md](../CHANGELOG.md) | User-facing changes (`[Unreleased]` until the first tag) |
| [../SECURITY.md](../SECURITY.md) | Threat model and reporting |
