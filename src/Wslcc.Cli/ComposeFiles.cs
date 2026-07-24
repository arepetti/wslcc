using Spectre.Console;
using Wslcc.Compose;

namespace Wslcc.Cli;

/// <summary>Resolved Compose file inputs to send to the daemon.</summary>
internal sealed record ComposeInputs(
    string Yaml,
    string DefaultProjectName,
    string ProjectDirectory,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DeclaredProfiles);

/// <summary>
/// Locates the Compose file(s) (explicit <c>--file</c>, <c>COMPOSE_FILE</c>, or conventional names) and
/// resolves them on the client via <see cref="ComposeLoader"/> — multi-file merge, <c>.env</c>, variable
/// interpolation, <c>extends</c> and profile filtering — so the daemon receives a single fully-resolved
/// document.
/// </summary>
internal static class ComposeFiles
{
    /// <summary>
    /// Resolves the compose inputs. Returns <c>true</c> on success (with <paramref name="inputs"/> being
    /// <c>null</c> when no compose file was found or specified); returns <c>false</c> with a rendered
    /// <paramref name="error"/> when resolution failed (bad YAML, unset required variable, ...).
    /// <paramref name="targetedServices"/> are the services named on the command line, whose profiles are
    /// auto-activated. Set <paramref name="printWarnings"/> to <c>false</c> to keep <c>stdout</c> clean
    /// (e.g. <c>compose config</c>); warnings remain available via <see cref="ComposeInputs.Warnings"/>.
    /// </summary>
    public static bool TryResolve(
        ComposeFileSettings settings,
        out ComposeInputs? inputs,
        out string error,
        IReadOnlyList<string>? targetedServices = null,
        bool printWarnings = true,
        bool interpolate = true)
    {
        inputs = null;
        error = string.Empty;

        try
        {
            inputs = Resolve(settings, targetedServices ?? Array.Empty<string>(), interpolate);
            if (inputs is not null && printWarnings)
            {
                foreach (var warning in inputs.Warnings)
                {
                    AnsiConsole.MarkupLine($"[yellow]warning:[/] {warning.EscapeMarkup()}");
                }
            }

            return true;
        }
        catch (ComposeLoadException ex)
        {
            error = $"[red]{ex.Message.EscapeMarkup()}[/]";
            return false;
        }
    }

    private static ComposeInputs? Resolve(ComposeFileSettings settings, IReadOnlyList<string> targetedServices, bool interpolate)
    {
        var cwd = Directory.GetCurrentDirectory();
        var projectDirectory = string.IsNullOrWhiteSpace(settings.ProjectDirectory) ? null : Path.GetFullPath(settings.ProjectDirectory);

        var files = ComposeFileDiscovery.Discover(settings.Files, projectDirectory ?? cwd, ProcessEnvironment());
        if (files.Count == 0)
        {
            return null;
        }

        var result = ComposeLoader.Load(new ComposeLoadOptions
        {
            Files = files,
            Profiles = settings.Profiles,
            TargetedServices = targetedServices,
            EnvFilePath = string.IsNullOrWhiteSpace(settings.EnvFile) ? null : Path.GetFullPath(settings.EnvFile),
            ProjectDirectory = projectDirectory,
            WorkingDirectory = cwd,
            Interpolate = interpolate,
        });

        var defaultProjectName = new DirectoryInfo(result.ProjectDirectory).Name;
        if (string.IsNullOrEmpty(defaultProjectName))
        {
            defaultProjectName = "wslcc";
        }

        return new ComposeInputs(result.ResolvedYaml, defaultProjectName, result.ProjectDirectory, result.Warnings, result.DeclaredProfiles);
    }

    private static IReadOnlyDictionary<string, string> ProcessEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "COMPOSE_FILE", "COMPOSE_PATH_SEPARATOR" })
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                env[key] = value;
            }
        }

        return env;
    }
}
