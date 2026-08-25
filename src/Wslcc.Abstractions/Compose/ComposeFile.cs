namespace Wslcc.Abstractions.Compose;

/// <summary>
/// In-memory representation of a Compose file (the subset WSLCC currently understands).
/// </summary>
/// <example>
/// Typically produced by the parser from an already-resolved document, then handed to the engine:
/// <code>
/// ComposeFile file = new ComposeFileParser().Parse(resolvedYaml);
/// var results = await engine.UpAsync(
///     projectName: "myproject",
///     file: file,
///     providerName: null,
///     pull: false,
///     buildPolicy: BuildPolicy.Auto,
///     baseDirectory: projectDirectory,
///     cancellationToken: cancellationToken);
/// </code>
/// </example>
public sealed class ComposeFile
{
    /// <summary>Optional top-level project name (Compose <c>name:</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Services from the <c>services:</c> section, keyed by service name.</summary>
    public Dictionary<string, ServiceSpec> Services { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Networks from the top-level <c>networks:</c> section, keyed by network name.</summary>
    public Dictionary<string, NetworkSpec> Networks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Named volumes from the top-level <c>volumes:</c> section, keyed by volume name.</summary>
    public Dictionary<string, VolumeSpec> Volumes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Secrets from the top-level <c>secrets:</c> section, keyed by secret name.</summary>
    public Dictionary<string, SecretSpec> Secrets { get; set; } = new(StringComparer.Ordinal);
}
