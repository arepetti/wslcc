namespace Wslcc.Abstractions.Compose;

/// <summary>
/// A network definition from the <c>networks:</c> section.
/// </summary>
public sealed class NetworkSpec
{
    public string Name { get; set; } = string.Empty;

    public string? Driver { get; set; }

    public bool External { get; set; }
}
