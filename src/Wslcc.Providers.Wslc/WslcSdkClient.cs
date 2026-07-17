using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc;

#if WSLC_SDK
using Microsoft.WSL.Containers;

/// <summary>
/// <see cref="IWslcClient"/> backed by the <c>Microsoft.WSL.Containers</c> managed SDK.
/// Enabled by defining the <c>WSLC_SDK</c> compile constant once the preview package restores
/// reliably on the build machine. As the SDK gains parity, prefer this client and drop
/// <see cref="WslcCliClient"/>.
/// </summary>
public sealed class WslcSdkClient : IWslcClient
{
    public Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
    {
        // NOTE: API surface is illustrative and must be validated against the shipped SDK.
        var available = WslcService.CheckPrerequisites();
        var version = WslcService.Version;

        return Task.FromResult(new ProviderInfo(
            WslcProvider.ProviderName,
            "WSL Containers",
            IsAvailable: available,
            Version: version));
    }
}
#endif
