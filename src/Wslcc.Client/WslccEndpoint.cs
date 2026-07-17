namespace Wslcc.Client;

/// <summary>
/// A parsed connection target. A single <c>--host</c> URI selects the transport by scheme:
/// <c>npipe://&lt;name&gt;</c> (or <c>npipe://&lt;server&gt;/&lt;name&gt;</c>) for a local named pipe,
/// and <c>http(s)://host:port</c> for a remote daemon over HTTP/2.
/// </summary>
public sealed class WslccEndpoint
{
    public const string DefaultPipeName = "wslccd";
    public const string DefaultHost = "npipe://" + DefaultPipeName;

    private WslccEndpoint()
    {
    }

    public bool IsNamedPipe { get; private init; }

    public string ServerName { get; private init; } = ".";

    public string PipeName { get; private init; } = DefaultPipeName;

    public Uri? HttpUri { get; private init; }

    /// <summary>Human-readable representation for logs and CLI output.</summary>
    public string Display { get; private init; } = DefaultHost;

    public static WslccEndpoint Parse(string? host)
    {
        var value = string.IsNullOrWhiteSpace(host) ? DefaultHost : host!.Trim();

        if (TryStripScheme(value, "npipe://", out var pipeRest) ||
            TryStripScheme(value, "pipe://", out pipeRest))
        {
            var (server, name) = SplitPipe(pipeRest);
            return new WslccEndpoint
            {
                IsNamedPipe = true,
                ServerName = server,
                PipeName = name,
                Display = $"npipe://{(server == "." ? string.Empty : server + "/")}{name}",
            };
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(value, UriKind.Absolute);
            return new WslccEndpoint
            {
                IsNamedPipe = false,
                HttpUri = uri,
                Display = uri.ToString(),
            };
        }

        // Bare value: treat as a local pipe name for convenience.
        return new WslccEndpoint
        {
            IsNamedPipe = true,
            ServerName = ".",
            PipeName = value,
            Display = $"npipe://{value}",
        };
    }

    private static bool TryStripScheme(string value, string scheme, out string rest)
    {
        if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            rest = value.Substring(scheme.Length);
            return true;
        }

        rest = string.Empty;
        return false;
    }

    private static (string Server, string Name) SplitPipe(string rest)
    {
        rest = rest.Trim('/');
        if (rest.Length == 0)
        {
            return (".", DefaultPipeName);
        }

        var slash = rest.IndexOf('/');
        if (slash < 0)
        {
            return (".", rest);
        }

        var server = rest.Substring(0, slash);
        var name = rest.Substring(slash + 1);
        return (string.IsNullOrEmpty(server) ? "." : server, string.IsNullOrEmpty(name) ? DefaultPipeName : name);
    }
}
