namespace Wslcc.Abstractions;

/// <summary>Well-known label keys and naming conventions used to group containers by project.</summary>
public static class WslccLabels
{
    /// <summary>Name of the Compose project a container, network or volume belongs to.</summary>
    public const string Project = "wslcc.project";

    /// <summary>Name of the Compose service a container was created for.</summary>
    public const string Service = "wslcc.service";

    /// <summary>Hash of the service's resolved configuration, used by <c>up</c> for change detection.</summary>
    public const string ConfigHash = "wslcc.config-hash";

    /// <summary>Key of the compose <c>networks:</c> entry a project network was created from.</summary>
    public const string Network = "wslcc.network";

    /// <summary>Key of the compose <c>volumes:</c> entry a project volume was created from.</summary>
    public const string Volume = "wslcc.volume";

    /// <summary>Container name for a service, e.g. "myproject-web".</summary>
    /// <param name="project">Project the container belongs to.</param>
    /// <param name="service">Compose service the container runs.</param>
    /// <returns>The conventional container name.</returns>
    /// <exception cref="ArgumentException">Either name is null, empty or whitespace.</exception>
    public static string ContainerName(string project, string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        return $"{project}-{service}";
    }

    /// <summary>Project network name for a declared network, e.g. "myproject_backend" (Compose convention).</summary>
    /// <param name="project">Project the network belongs to.</param>
    /// <param name="network">Key of the compose <c>networks:</c> entry.</param>
    /// <returns>The project-prefixed network name.</returns>
    /// <exception cref="ArgumentException">Either name is null, empty or whitespace.</exception>
    public static string NetworkName(string project, string network)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(network);

        return $"{project}_{network}";
    }

    /// <summary>The implicit per-project network every service joins when it declares no networks.</summary>
    /// <param name="project">Project the network belongs to.</param>
    /// <returns>The project's default network name.</returns>
    /// <exception cref="ArgumentException"><paramref name="project"/> is null, empty or whitespace.</exception>
    public static string DefaultNetworkName(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        return $"{project}_default";
    }

    /// <summary>Project volume name for a declared named volume, e.g. "myproject_data" (Compose convention).</summary>
    /// <param name="project">Project the volume belongs to.</param>
    /// <param name="volume">Key of the compose <c>volumes:</c> entry.</param>
    /// <returns>The project-prefixed volume name.</returns>
    /// <exception cref="ArgumentException">Either name is null, empty or whitespace.</exception>
    public static string VolumeName(string project, string volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(volume);

        return $"{project}_{volume}";
    }
}
