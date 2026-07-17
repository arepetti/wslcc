using System.Reflection;

namespace Wslcc.Cli;

/// <summary>Resolves the wslcc build version for <c>-v|--version</c> and the version command.</summary>
internal static class CliVersion
{
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var informational = typeof(CliVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational!.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }

        return typeof(CliVersion).Assembly.GetName().Version?.ToString() ?? "0.1.0";
    }
}
