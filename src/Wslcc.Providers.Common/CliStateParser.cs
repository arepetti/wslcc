using System.Globalization;
using Wslcc.Abstractions;

namespace Wslcc.Providers.Common;

/// <summary>
/// Parses the output of a container CLI's <c>container inspect --format</c> using
/// <see cref="CliCommandBuilder.InspectStateFormat"/> (status, health, exit code separated by the unit
/// separator). Kept pure and separate from the process plumbing so it can be unit-tested.
/// </summary>
public static class CliStateParser
{
    /// <summary>Reads the first non-empty output line into a runtime state.</summary>
    /// <param name="output">Raw output of the inspect invocation.</param>
    /// <returns>
    /// The parsed state. Missing or unrecognized fields degrade gracefully: an empty status, a health of
    /// <see cref="HealthStatus.None"/>, and a <c>null</c> exit code.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <c>null</c>.</exception>
    public static ContainerRuntimeState Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var line = FirstNonEmptyLine(output);
        var fields = line.Split(CliCommandBuilder.FieldSeparator);

        string Field(int index) => index < fields.Length ? fields[index].Trim() : string.Empty;

        var exitCode = int.TryParse(Field(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code
            : (int?)null;

        return new ContainerRuntimeState(Field(0), ParseHealth(Field(1)), exitCode);
    }

    private static HealthStatus ParseHealth(string value) => value.ToLowerInvariant() switch
    {
        "starting" => HealthStatus.Starting,
        "healthy" => HealthStatus.Healthy,
        "unhealthy" => HealthStatus.Unhealthy,
        _ => HealthStatus.None,
    };

    private static string FirstNonEmptyLine(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
                return line;
        }

        return string.Empty;
    }
}
