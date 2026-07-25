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
    public static ContainerRuntimeState Parse(string output)
    {
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
            {
                return line;
            }
        }

        return string.Empty;
    }
}
