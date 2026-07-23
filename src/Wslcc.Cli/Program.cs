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
        compose.AddCommand<ComposeStartCommand>("start")
            .WithDescription("Start existing (stopped) containers.");
        compose.AddCommand<ComposeStopCommand>("stop")
            .WithDescription("Stop containers without removing them.");
        compose.AddCommand<ComposeRestartCommand>("restart")
            .WithDescription("Restart containers.");
        compose.AddCommand<ComposePullCommand>("pull")
            .WithDescription("Pull service images.");
        compose.AddCommand<ComposeBuildCommand>("build")
            .WithDescription("Build or rebuild services.");
        compose.AddCommand<ComposeLogsCommand>("logs")
            .WithDescription("View output from containers.");
        compose.AddCommand<ComposeConfigCommand>("config")
            .WithDescription("Parse, resolve and render the compose configuration.");
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
        daemon.AddCommand<DaemonInstallCommand>("install")
            .WithDescription("Register wslccd as a Windows Service (requires Administrator).");
        daemon.AddCommand<DaemonUninstallCommand>("uninstall")
            .WithDescription("Remove the wslccd Windows Service (requires Administrator).");
    });
});

return await app.RunAsync(args);
