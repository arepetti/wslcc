using Wslcc.Abstractions;
using Wslcc.Providers.Common;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// Provider for Microsoft's WSL containers feature. Container operations come from
/// <see cref="CliContainerProviderBase"/> (the <c>wslc</c> CLI mirrors standard container tooling).
/// Version/availability prefers the managed SDK when compiled with <c>WSLC_SDK</c>, otherwise the CLI.
/// </summary>
public sealed class WslcProvider : CliContainerProviderBase
{
    public const string ProviderName = "wslc";

    private readonly IWslcClient _client;

    public WslcProvider()
        : this(CreateDefaultClient())
    {
    }

    public WslcProvider(IWslcClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    protected override string Executable => "wslc";

    public override string Name => ProviderName;

    public override Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
        => _client.GetProviderInfoAsync(cancellationToken);

    private static IWslcClient CreateDefaultClient()
    {
#if WSLC_SDK
        return new WslcSdkClient();
#else
        return new WslcCliClient();
#endif
    }
}
