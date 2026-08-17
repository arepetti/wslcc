namespace Wslcc.Abstractions;

/// <summary>
/// A container as reported by a provider.
/// </summary>
/// <param name="Id">Provider-assigned container id.</param>
/// <param name="Name">Container name (for wslcc-created containers, <c>project-service</c>).</param>
/// <param name="Image">Image the container was created from.</param>
/// <param name="State">Lifecycle state (e.g. <c>running</c>, <c>exited</c>).</param>
/// <param name="Status">Human-readable status detail (e.g. <c>Up 3 minutes</c>), when the provider reports one.</param>
/// <param name="Service">Compose service name, read from the <see cref="WslccLabels.Service"/> label.</param>
/// <param name="Ports">Published port mappings as the provider formats them.</param>
/// <param name="Project">Compose project name, read from the <see cref="WslccLabels.Project"/> label.</param>
/// <param name="ConfigHash">
/// Resolved config hash the container was created with (<see cref="WslccLabels.ConfigHash"/>), used by
/// <c>up</c> to decide whether the container can be left in place.
/// </param>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string State,
    string? Status = null,
    string? Service = null,
    string? Ports = null,
    string? Project = null,
    string? ConfigHash = null);
