namespace Wslccd;

/// <summary>Daemon configuration bound from the <c>Wslcc</c> section of appsettings.json.</summary>
public sealed class DaemonOptions
{
    public const string SectionName = "Wslcc";

    /// <summary>Local named pipe the daemon listens on.</summary>
    public string PipeName { get; set; } = "wslccd";

    /// <summary>Optional remote HTTP/2 endpoint.</summary>
    public HttpOptions Http { get; set; } = new();

    /// <summary>Default provider used when a request does not specify one.</summary>
    public string DefaultProvider { get; set; } = "wslc";

    /// <summary>Which providers to register.</summary>
    public ProviderOptions Providers { get; set; } = new();

    public sealed class HttpOptions
    {
        public bool Enabled { get; set; }

        public string Url { get; set; } = "https://0.0.0.0:5211";

        /// <summary>PEM certificate path (required when <see cref="Enabled"/> is true).</summary>
        public string? CertificatePath { get; set; }

        /// <summary>PEM private-key path (required when <see cref="Enabled"/> is true).</summary>
        public string? CertificateKeyPath { get; set; }

        /// <summary>Bearer token. Prefer <see cref="TokenFile"/> or <c>WSLCC_HTTP_TOKEN</c> over committing this.</summary>
        public string? Token { get; set; }

        /// <summary>Path to a file whose entire contents (trimmed) are the bearer token.</summary>
        public string? TokenFile { get; set; }
    }

    public sealed class ProviderOptions
    {
        public bool Wslc { get; set; } = true;

        public bool Docker { get; set; } = true;
    }
}
