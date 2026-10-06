# Providers

A provider is an implementation of `IContainerProvider` (in `Wslcc.Abstractions`). The core engine is provider-agnostic; the daemon registers the available providers and picks a default.

Select a provider per compose command with `--wslcc-provider <name>`; otherwise the daemon's configured `DefaultProvider` is used. Set that default once with `wslcc daemon start --provider <name>` (or `wslcc daemon install --provider <name>`), where `--provider` configures the daemon rather than a single call.

## `wslc` — WSL containers

`Wslcc.Providers.Wslc` targets WSLc 3.0.1 GA and references `Microsoft.WSL.Containers` 3.0.1. It is intentionally hybrid because the managed API and CLI expose different capabilities:

- `WslcSdkClient` owns a long-lived `wslcc` session under `%LOCALAPPDATA%\wslcc\containers`. It handles prerequisites/version, image lookup and pull, and basic container inspect/start/stop/delete operations.
- `WslcCliClient` is a dedicated GA CLI dialect used only for capabilities missing from the SDK: builds, label-backed creation/discovery, retained logs, healthchecks, custom networks, generic volumes, and resource enumeration.

Every fallback command includes `wslc --session wslcc`, so SDK and CLI operations see the same images and containers. Do not use Docker Go-template formats for WSLc: WSLc listings use JSON, and options such as network aliases have WSLc-specific spellings.

Container creation remains CLI-backed because WSLCC's project/service/config-hash labels are required for change detection and teardown, while the managed API cannot set container labels. The managed API also has no image-build API; even the NuGet package's MSBuild integration invokes `wslc image build`.

WSLc 3.0.1 supports healthchecks, DNS, per-container CPU/memory limits, GPU requests, terminal/stdin options, custom network IPAM, and advanced mounts through its CLI. It has a restart **command** but no restart policy, and it exposes neither OCI annotations nor a read-only-rootfs setting; WSLCC rejects those provider-specific requests. See [compatibility.md](compatibility.md) and [todo.md](todo.md#wslc-301-cli-only-capabilities).

If the API prerequisites are missing, the provider reports `IsAvailable = false` with `wsl --update` guidance rather than throwing.

Authoritative references: [WSL container overview](https://learn.microsoft.com/windows/wsl/wsl-container), [Microsoft.WSL.Containers 3.0.1](https://www.nuget.org/packages/Microsoft.WSL.Containers/3.0.1), and the [3.0.1 API definition](https://github.com/microsoft/WSL/blob/3.0.1/src/windows/WslcSDK/winrt/wslcsdk.idl).

## `docker` — Docker CLI

`Wslcc.Providers.DockerCompose` shells out to the plain `docker` CLI for container lifecycle (`run`, `ps`, `stop`, …). Orchestration (dependency order, change detection, networks/volumes) lives in WSLCC's own `ComposeEngine`, not in Docker Compose. The provider uses `docker compose version --short` only to report a version string in `wslcc version` / `compose version`. Containers are labelled `wslcc.project` / `wslcc.service`, so they are **not** visible to `docker compose ps` (and existing Compose projects are not visible to `wslcc compose ps`). See [compatibility.md](compatibility.md) for the full Compose migration picture. If `docker` is not found, the provider reports `IsAvailable = false`.

OCI annotations (`--annotation` on `docker run`) require Docker Engine 25+ (API 1.44). Older engines fail `run` rather than silently dropping the metadata.

## Adding a provider

1. Implement `IContainerProvider` (at minimum `Name` and `GetProviderInfoAsync`).
2. Never throw from `GetProviderInfoAsync` when tooling is missing — return `IsAvailable = false`.
3. Register it in the daemon composition root (`Wslccd/Program.cs`).
