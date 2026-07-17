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
/// Options shared by every leaf command. Declared here (below <see cref="BranchSettings"/>) so they
/// bind at the leaf level, i.e. after the subcommand.
/// </summary>
public class GlobalSettings : BranchSettings
{
    [CommandOption("-H|--host <URI>")]
    [Description("Daemon endpoint. npipe://<name> (default) for a local pipe, or http(s)://host:port for a remote daemon.")]
    public string? Host { get; set; }

    [CommandOption("--provider <NAME>")]
    [Description("Provider to target: 'wslc' or 'docker'. Defaults to the daemon's configured provider.")]
    public string? Provider { get; set; }
}
