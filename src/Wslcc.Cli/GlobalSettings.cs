using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wslcc.Cli;

/// <summary>
/// Empty settings used as the type for branches (<c>compose</c>, <c>daemon</c>). Branches deliberately
/// declare no options so that global options belong to the leaf commands and can be supplied after the
/// subcommand, e.g. <c>wslcc compose up --provider docker</c>.
/// </summary>
public class BranchSettings : CommandSettings
{
}

/// <summary>
/// Options shared by <em>every</em> leaf command. Only the truly universal switches live here; the
/// daemon endpoint and provider selection are declared by the command families that actually use them
/// (see <see cref="HostSettings"/>, <see cref="DaemonProviderSettings"/> and
/// <see cref="ComposeCommandSettings"/>) so they are not advertised on commands that ignore them.
/// </summary>
public class GlobalSettings : BranchSettings
{
    [CommandOption("--no-color")]
    [Description("Disable colored output. Also honored via the NO_COLOR environment variable.")]
    public bool NoColor { get; set; }
}
