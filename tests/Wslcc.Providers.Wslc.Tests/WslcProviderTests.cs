using Wslcc.Abstractions;

namespace Wslcc.Providers.Wslc.Tests;

public sealed class WslcProviderTests
{
    [Fact]
    public void Run_command_targets_managed_session_and_uses_GA_network_syntax()
    {
        var spec = new ContainerRunSpec
        {
            Image = "nginx:latest",
            Name = "demo-web",
            Network = "demo_front",
            NetworkAlias = "web",
            NetworkIPv4Address = "10.20.0.5",
        };
        spec.NetworkAliases.Add("api");

        var arguments = WslcCommandBuilder.BuildRunArguments(spec);

        Assert.StartsWith("--session wslcc run", arguments);
        Assert.Contains("--network demo_front", arguments);
        Assert.Contains("--network-alias web", arguments);
        Assert.Contains("--network-alias api", arguments);
        Assert.Contains("--ip 10.20.0.5", arguments);
    }

    [Theory]
    [InlineData("annotations")]
    [InlineData("read-only")]
    [InlineData("restart")]
    [InlineData("host")]
    public void Run_command_rejects_options_missing_from_WSLc_3_0_1(string option)
    {
        var spec = new ContainerRunSpec { Image = "alpine", Name = "demo" };
        switch (option)
        {
            case "annotations":
                spec.Annotations["org.example.test"] = "true";
                break;
            case "read-only":
                spec.ReadOnly = true;
                break;
            case "restart":
                spec.Restart = "always";
                break;
            case "host":
                spec.Network = "host";
                break;
        }

        var error = Assert.Throws<ProviderException>(() => WslcCommandBuilder.BuildRunArguments(spec));
        Assert.Contains("WSLc 3.0.1", error.Message);
    }

    [Fact]
    public void Network_connect_uses_GA_alias_and_static_IP_options()
    {
        var arguments = WslcCommandBuilder.BuildNetworkConnectArguments(
            "demo_back",
            "demo-web",
            new[] { "web", "api" },
            "10.30.0.8");

        Assert.Equal(
            "--session wslcc network connect --network-alias web --network-alias api --ip 10.30.0.8 demo_back demo-web",
            arguments);
    }

    [Fact]
    public void Container_parser_reads_GA_JSON_and_WSLCC_labels()
    {
        const string json = """
            [
              {
                "ID": "abc123",
                "Names": ["demo-web"],
                "Image": "nginx:latest",
                "State": "running",
                "Status": "Up 5 seconds",
                "Ports": ["0.0.0.0:8080->80/tcp"],
                "Labels": {
                  "wslcc.project": "demo",
                  "wslcc.service": "web",
                  "wslcc.config-hash": "cafebabe"
                }
              }
            ]
            """;

        var container = Assert.Single(WslcJsonParser.ParseContainers(json));

        Assert.Equal("abc123", container.Id);
        Assert.Equal("demo-web", container.Name);
        Assert.Equal("demo", container.Project);
        Assert.Equal("web", container.Service);
        Assert.Equal("cafebabe", container.ConfigHash);
    }

    [Fact]
    public void Name_parser_accepts_newline_delimited_JSON()
    {
        const string json = """
            {"ID":"one","Name":"demo_front"}
            {"ID":"two","Name":"demo_back"}
            """;

        Assert.Equal(new[] { "demo_front", "demo_back" }, WslcJsonParser.ParseNames(json));
    }

    [Fact]
    public async Task Restart_uses_managed_open_stop_start_operations()
    {
        var client = new FakeClient
        {
            State = new ContainerRuntimeState("running", HealthStatus.None, null),
        };
        using var provider = new WslcProvider(client);

        await provider.RestartContainerAsync("demo-web");

        Assert.Equal(new[] { "stop:demo-web", "start:demo-web" }, client.Calls);
    }

    private sealed class FakeClient : IWslcClient
    {
        public ContainerRuntimeState? State { get; set; }

        public List<string> Calls { get; } = new();

        public Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ProviderInfo("wslc", "WSL Containers", true, "3.0.1"));

        public Task EnsureSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task StartContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            Calls.Add("start:" + container);
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            Calls.Add("stop:" + container);
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(
            string container,
            bool force,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ContainerRuntimeState?> GetContainerStateAsync(
            string container,
            CancellationToken cancellationToken = default)
            => Task.FromResult(State);

        public void Dispose()
        {
        }
    }
}
