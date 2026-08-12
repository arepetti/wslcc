namespace Wslcc.Abstractions.Compose;

/// <summary>
/// One <c>env_file:</c> entry — a path string, or the long form <c>{ path, required }</c>.
/// </summary>
public sealed class EnvFileSpec
{
    /// <summary>Path to the env file (relative paths resolve against the project / base directory).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c> (Compose default), a missing file fails the service. When <c>false</c>, a
    /// missing file is skipped.
    /// </summary>
    public bool Required { get; set; } = true;
}
