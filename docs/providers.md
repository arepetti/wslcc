# Providers

A provider is an implementation of `IContainerProvider` (in `Wslcc.Abstractions`). The core engine is
provider-agnostic; the daemon registers the available providers and picks a default.

Select a provider per command with `--provider <name>`; otherwise the daemon's configured
`DefaultProvider` is used.

## `wslc` — WSL containers

`Wslcc.Providers.Wslc` targets Microsoft's WSL containers feature. It is designed around a thin seam,
`IWslcClient`, with two implementations:

- `WslcSdkClient` — uses the managed `Microsoft.WSL.Containers` SDK. **Gated** behind the `WSLC_SDK`
  compile constant, because the package is a preview and may not restore everywhere.
- `WslcCliClient` — shells out to `wslc.exe`. Used as the fallback today.

`WslcProvider.CreateDefaultClient()` chooses the SDK client when compiled with `WSLC_SDK`, otherwise
the CLI client. As the SDK reaches API parity, flip the constant on and remove the CLI fallback. This
keeps the "easy to fix later" requirement localized to one file.

If `wslc` is not installed, the provider reports `IsAvailable = false` with guidance
(`wsl --update --pre-release`) rather than throwing.

## `docker` — Docker Compose

`Wslcc.Providers.DockerCompose` shells out to the `docker` CLI and its `compose` plugin. It is useful
for testing WSLCC today and for a single unified interface across both backends. If `docker` is not
found, the provider reports `IsAvailable = false`.

## Adding a provider

1. Implement `IContainerProvider` (at minimum `Name` and `GetProviderInfoAsync`).
2. Never throw from `GetProviderInfoAsync` when tooling is missing — return `IsAvailable = false`.
3. Register it in the daemon composition root (`Wslccd/Program.cs`).
