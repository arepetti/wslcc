using Spectre.Console;
using Spectre.Console.Cli;

namespace Wslcc.Cli;

/// <summary>
/// Runs before every command and switches the shared <see cref="AnsiConsole"/> to a color-less profile
/// when <c>--no-color</c> is passed or the <c>NO_COLOR</c> environment variable is set. Spectre.Console
/// already detects <c>NO_COLOR</c> natively; routing both through here keeps the flag and the environment
/// variable on a single, predictable path.
/// </summary>
public sealed class ColorInterceptor : ICommandInterceptor
{
    /// <summary>Whether color output should be disabled given the parsed settings and the NO_COLOR value.</summary>
    public static bool ShouldDisableColor(CommandSettings settings, string? noColorEnvValue)
        => settings is GlobalSettings { NoColor: true } || !string.IsNullOrEmpty(noColorEnvValue);

    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (ShouldDisableColor(settings, Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            AnsiConsole.Console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        }
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
    }
}
