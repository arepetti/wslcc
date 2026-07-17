using Spectre.Console.Cli;
using Wslcc.Cli;
using Wslcc.Cli.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("wslcc");
    config.SetApplicationVersion(CliVersion.Value);

    config.AddCommand<VersionCommand>("version")
        .WithDescription("Show wslcc, daemon, and provider versions.");

    config.AddBranch<BranchSettings>("compose", compose =>
    {
        compose.SetDescription("Manage a Compose application (mirrors 'docker compose ...').");

        compose.AddCommand<ComposeVersionCommand>("version")
            .WithDescription("Show the active provider / compose engine version.");

        compose.AddCommand<ComposeUpCommand>("up")
            .WithDescription("Create and start containers.");
        compose.AddCommand<ComposeDownCommand>("down")
            .WithDescription("Stop and remove containers.");
        compose.AddCommand<ComposePsCommand>("ps")
            .WithDescription("List containers.");

        // Remaining compose verbs are stubbed; implementations tracked in docs/todo.md.
        foreach (var (verb, description) in ComposeVerbs)
        {
            compose.AddCommand<ComposeStubCommand>(verb).WithDescription(description);
        }
    });

    config.AddBranch<BranchSettings>("daemon", daemon =>
    {
        daemon.SetDescription("Manage the wslccd background daemon.");

        daemon.AddCommand<DaemonStartCommand>("start")
            .WithDescription("Start the local daemon and wait until it is ready.");
        daemon.AddCommand<DaemonStopCommand>("stop")
            .WithDescription("Stop the daemon gracefully.");
        daemon.AddCommand<DaemonStatusCommand>("status")
            .WithDescription("Report whether the daemon is running.");
    });
});

return await app.RunAsync(args);

partial class Program
{
    private static readonly (string Verb, string Description)[] ComposeVerbs =
    [
        ("logs", "View output from containers. (not implemented yet)"),
        ("build", "Build or rebuild services. (not implemented yet)"),
        ("pull", "Pull service images. (not implemented yet)"),
        ("config", "Parse, resolve and render the compose file. (not implemented yet)"),
        ("start", "Start services. (not implemented yet)"),
        ("stop", "Stop services. (not implemented yet)"),
        ("restart", "Restart services. (not implemented yet)"),
    ];
}
