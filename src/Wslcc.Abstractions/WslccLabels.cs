namespace Wslcc.Abstractions;

/// <summary>Well-known label keys and naming conventions used to group containers by project.</summary>
public static class WslccLabels
{
    public const string Project = "wslcc.project";

    public const string Service = "wslcc.service";

    /// <summary>Hash of the service's resolved configuration, used by <c>up</c> for change detection.</summary>
    public const string ConfigHash = "wslcc.config-hash";

    /// <summary>Key of the compose <c>networks:</c> entry a project network was created from.</summary>
    public const string Network = "wslcc.network";

    /// <summary>Key of the compose <c>volumes:</c> entry a project volume was created from.</summary>
    public const string Volume = "wslcc.volume";

    /// <summary>Container name for a service, e.g. "myproject-web".</summary>
    public static string ContainerName(string project, string service) => $"{project}-{service}";

    /// <summary>Project network name for a declared network, e.g. "myproject_backend" (Compose convention).</summary>
    public static string NetworkName(string project, string network) => $"{project}_{network}";

    /// <summary>The implicit per-project network every service joins when it declares no networks.</summary>
    public static string DefaultNetworkName(string project) => $"{project}_default";

    /// <summary>Project volume name for a declared named volume, e.g. "myproject_data" (Compose convention).</summary>
    public static string VolumeName(string project, string volume) => $"{project}_{volume}";
}
