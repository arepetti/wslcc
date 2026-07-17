namespace Wslcc.Abstractions;

/// <summary>Well-known label keys and naming conventions used to group containers by project.</summary>
public static class WslccLabels
{
    public const string Project = "wslcc.project";

    public const string Service = "wslcc.service";

    /// <summary>Container name for a service, e.g. "myproject-web".</summary>
    public static string ContainerName(string project, string service) => $"{project}-{service}";
}
