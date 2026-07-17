using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// <see cref="IWslcClient"/> backed by the <c>wslc.exe</c> command-line tool. Used until the
/// managed SDK covers every operation.
/// </summary>
public sealed class WslcCliClient : IWslcClient
{
    public async Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.TryRunAsync("wslc", "--version", cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return new ProviderInfo(
                WslcProvider.ProviderName,
                "WSL Containers",
                IsAvailable: false,
                Version: null,
                Details: "The 'wslc' executable was not found. Install the WSL pre-release with 'wsl --update --pre-release'.");
        }

        if (!result.Success)
        {
            var detail = result.StandardError.Trim();
            return new ProviderInfo(
                WslcProvider.ProviderName,
                "WSL Containers",
                IsAvailable: false,
                Version: null,
                Details: string.IsNullOrEmpty(detail) ? "'wslc --version' failed." : detail);
        }

        return new ProviderInfo(
            WslcProvider.ProviderName,
            "WSL Containers",
            IsAvailable: true,
            Version: ExtractVersion(result.StandardOutput));
    }

    private static string ExtractVersion(string output)
    {
        var text = output.Trim();
        if (text.Length == 0)
        {
            return "unknown";
        }

        // Typical output looks like "wslc 1.2.3" or similar; keep the first line.
        var firstLine = text.Split('\n')[0].Trim();
        return firstLine;
    }
}
