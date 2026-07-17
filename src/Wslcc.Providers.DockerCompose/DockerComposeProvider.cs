using Wslcc.Abstractions;
using Wslcc.Providers.Common;

namespace Wslcc.Providers.DockerCompose;

/// <summary>
/// Provider that delegates to the local <c>docker</c> CLI. Container operations come from
/// <see cref="CliContainerProviderBase"/>; version detection uses the compose plugin.
/// </summary>
public sealed class DockerComposeProvider : CliContainerProviderBase
{
    public const string ProviderName = "docker";

    protected override string Executable => "docker";

    public override string Name => ProviderName;

    public override async Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.TryRunAsync("docker", "compose version --short", cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return new ProviderInfo(
                ProviderName,
                "Docker Compose",
                IsAvailable: false,
                Version: null,
                Details: "The 'docker' executable was not found on PATH.");
        }

        if (!result.Success)
        {
            var detail = result.StandardError.Trim();
            return new ProviderInfo(
                ProviderName,
                "Docker Compose",
                IsAvailable: false,
                Version: null,
                Details: string.IsNullOrEmpty(detail) ? "'docker compose' is not available." : detail);
        }

        return new ProviderInfo(
            ProviderName,
            "Docker Compose",
            IsAvailable: true,
            Version: result.StandardOutput.Trim());
    }
}
