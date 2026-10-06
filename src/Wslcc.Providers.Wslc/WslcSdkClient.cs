using Wslcc.Abstractions;
using System.Text.Json;
using Microsoft.WSL.Containers;

namespace Wslcc.Providers.Wslc;

/// <summary>
/// <see cref="IWslcClient"/> backed by the <c>Microsoft.WSL.Containers</c> managed SDK.
/// Owns the named session shared with the provider's CLI fallback.
/// </summary>
public sealed class WslcSdkClient : IWslcClient
{
    public const string SessionName = "wslcc";

    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly string _storagePath;
    private Session? _session;
    private bool _disposed;

    public WslcSdkClient()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wslcc",
            "containers"))
    {
    }

    internal WslcSdkClient(string storagePath)
    {
        _storagePath = storagePath;
    }

    public Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var missing = WslcService.GetMissingComponents();
            var version = WslcService.GetVersion();
            var versionText = $"{version.Major}.{version.Minor}.{version.Revision}";
            var available = missing.Count == 0;
            var details = available
                ? null
                : $"Missing WSL container components: {string.Join(", ", missing)}. Run 'wsl --update'.";

            return Task.FromResult(new ProviderInfo(
                WslcProvider.ProviderName,
                "WSL Containers",
                available,
                versionText,
                details));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProviderInfo(
                WslcProvider.ProviderName,
                "WSL Containers",
                IsAvailable: false,
                Version: null,
                Details: $"The WSL containers API is unavailable: {ex.Message}"));
        }
    }

    public async Task EnsureSessionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session is not null)
            return;

        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null)
                return;

            Directory.CreateDirectory(_storagePath);
            var session = new Session(new SessionSettings(SessionName, _storagePath));
            try
            {
                session.Start();
                _session = session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            throw Wrap("start the WSL container session", ex);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task EnsureImageAsync(
        string image,
        bool alwaysPull,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        if (!alwaysPull && ImageExists(image))
            return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _session!.PullImageAsync(new PullImageOptions(image));
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Wrap($"pull image '{image}'", ex);
        }
    }

    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        return ImageExists(image);
    }

    public async Task StartContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var opened = _session!.OpenContainer(container, ProcessOutputMode.Discard);
            opened.Start();
        }
        catch (Exception ex)
        {
            throw Wrap($"start container '{container}'", ex);
        }
    }

    public async Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var opened = _session!.OpenContainer(container, ProcessOutputMode.Discard);
            var (signal, timeout) = ParseStopSettings(opened.Inspect());
            opened.Stop(signal, timeout);
        }
        catch (Exception ex)
        {
            throw Wrap($"stop container '{container}'", ex);
        }
    }

    public async Task RemoveContainerAsync(
        string container,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var opened = _session!.OpenContainer(container, ProcessOutputMode.Discard);
            opened.Delete(force ? DeleteContainerOption.Force : DeleteContainerOption.None);
        }
        catch (Exception ex)
        {
            throw Wrap($"remove container '{container}'", ex);
        }
    }

    public async Task<ContainerRuntimeState?> GetContainerStateAsync(
        string container,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var opened = _session!.OpenContainer(container, ProcessOutputMode.Discard);
            return ParseState(opened.Inspect());
        }
        catch (Exception ex) when (IsContainerNotFound(ex))
        {
            return null;
        }
        catch (Exception ex)
        {
            throw Wrap($"inspect container '{container}'", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_session is not null)
        {
            try
            {
                _session.Terminate();
            }
            catch
            {
                // The service may already have terminated a crashed session.
            }

            _session.Dispose();
            _session = null;
        }

        _sessionLock.Dispose();
    }

    private bool ImageExists(string image)
        => _session!.GetImages().Any(item =>
            string.Equals(item.Name, image, StringComparison.OrdinalIgnoreCase)
            || (image.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, image[..^7], StringComparison.OrdinalIgnoreCase))
            || (!image.Contains(':', StringComparison.Ordinal)
                && string.Equals(item.Name, image + ":latest", StringComparison.OrdinalIgnoreCase)));

    private static ContainerRuntimeState ParseState(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var state = root.TryGetProperty("State", out var value) ? value : default;
        var status = GetString(state, "Status") ?? string.Empty;
        var exitCode = GetInt32(state, "ExitCode");
        var healthText = state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("Health", out var health)
            ? GetString(health, "Status")
            : null;

        var healthStatus = healthText?.ToLowerInvariant() switch
        {
            "starting" => HealthStatus.Starting,
            "healthy" => HealthStatus.Healthy,
            "unhealthy" => HealthStatus.Unhealthy,
            _ => HealthStatus.None,
        };

        return new ContainerRuntimeState(status, healthStatus, exitCode);
    }

    private static (Signal Signal, TimeSpan Timeout) ParseStopSettings(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var config = root.TryGetProperty("Config", out var configValue) ? configValue : default;
        var hostConfig = root.TryGetProperty("HostConfig", out var hostConfigValue) ? hostConfigValue : default;
        var signal = (GetString(config, "StopSignal") ?? "SIGTERM").ToUpperInvariant() switch
        {
            "SIGHUP" or "HUP" => Signal.SIGHUP,
            "SIGINT" or "INT" => Signal.SIGINT,
            "SIGQUIT" or "QUIT" => Signal.SIGQUIT,
            "SIGKILL" or "KILL" => Signal.SIGKILL,
            _ => Signal.SIGTERM,
        };
        var seconds = GetInt32(hostConfig, "StopTimeout") ?? GetInt32(config, "StopTimeout") ?? 10;
        return (signal, TimeSpan.FromSeconds(Math.Max(0, seconds)));
    }

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static int? GetInt32(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var result)
                ? result
                : null;

    private static bool IsContainerNotFound(Exception exception)
        => exception.HResult == unchecked((int)Error.ContainerNotFound);

    private static ProviderException Wrap(string action, Exception exception)
        => exception is ProviderException providerException
            ? providerException
            : new ProviderException($"Failed to {action} using the WSL containers API: {exception.Message}", exception);
}
