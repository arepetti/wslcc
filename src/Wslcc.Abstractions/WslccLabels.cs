namespace Wslcc.Abstractions;

/// <summary>Well-known label keys and naming conventions used to group containers by project.</summary>
public static class WslccLabels
{
    public const string Project = "wslcc.project";

    public const string Service = "wslcc.service";

    /// <summary>Hash of the service's resolved configuration, used by <c>up</c> for change detection.</summary>
    public const string ConfigHash = "wslcc.config-hash";

    /// <summary>Container name for a service, e.g. "myproject-web".</summary>
    public static string ContainerName(string project, string service) => $"{project}-{service}";
}
