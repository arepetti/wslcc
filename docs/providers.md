# Providers

A provider is an implementation of `IContainerProvider` (in `Wslcc.Abstractions`). The core engine is provider-agnostic; the daemon registers the available providers and picks a default.

Select a provider per compose command with `--wslcc-provider <name>`; otherwise the daemon's configured `DefaultProvider` is used. Set that default once with `wslcc daemon start --provider <name>` (or `wslcc daemon install --provider <name>`), where `--provider` configures the daemon rather than a single call.

## `wslc` — WSL containers

`Wslcc.Providers.Wslc` targets Microsoft's WSL containers feature. It is designed around a thin seam, `IWslcClient`, with two implementations:

- `WslcSdkClient` — uses the managed `Microsoft.WSL.Containers` SDK. **Gated** behind the `WSLC_SDK`
  compile constant, because the package is a preview and may not restore everywhere.
- `WslcCliClient` — shells out to `wslc.exe`. Used as the fallback today.

`WslcProvider.CreateDefaultClient()` chooses the SDK client when compiled with `WSLC_SDK`, otherwise the CLI client. As the SDK reaches API parity, flip the constant on and remove the CLI fallback. This keeps the "easy to fix later" requirement localized to one file.

If `wslc` is not installed, the provider reports `IsAvailable = false` with guidance (`wsl --update --pre-release`) rather than throwing.

## `docker` — Docker CLI

`Wslcc.Providers.DockerCompose` shells out to the plain `docker` CLI for container lifecycle (`run`, `ps`, `stop`, …). Orchestration (dependency order, change detection, networks/volumes) lives in WSLCC's own `ComposeEngine`, not in Docker Compose. The provider uses `docker compose version --short` only to report a version string in `wslcc version` / `compose version`. Containers are labelled `wslcc.project` / `wslcc.service`, so they are **not** visible to `docker compose ps` (and existing Compose projects are not visible to `wslcc compose ps`). See [compatibility.md](compatibility.md) for the full Compose migration picture. If `docker` is not found, the provider reports `IsAvailable = false`.

## Adding a provider

1. Implement `IContainerProvider` (at minimum `Name` and `GetProviderInfoAsync`).
2. Never throw from `GetProviderInfoAsync` when tooling is missing — return `IsAvailable = false`.
3. Register it in the daemon composition root (`Wslccd/Program.cs`).
