using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// Thin seam over the WSL containers tooling. Two implementations exist: one backed by the
/// <c>Microsoft.WSL.Containers</c> SDK (gated behind the <c>WSLC_SDK</c> compile constant) and one
/// that shells out to <c>wslc.exe</c>. This split keeps the CLI fallback isolated so it can be
/// removed once the SDK reaches API parity.
/// </summary>
public interface IWslcClient
{
    Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default);
}
