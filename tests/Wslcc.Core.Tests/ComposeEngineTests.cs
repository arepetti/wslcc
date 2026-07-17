using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;

namespace Wslcc.Core.Tests;

public sealed class ComposeEngineTests
{
    private sealed class FakeProvider : IContainerProvider
    {
        public FakeProvider(string name, bool available)
        {
            Name = name;
            _info = new ProviderInfo(name, name, available, available ? "1.0" : null);
        }

        private readonly ProviderInfo _info;

        public string Name { get; }

        public List<string> RunOrder { get; } = new();

        public List<ContainerRunSpec> RunSpecs { get; } = new();

        public List<string> Removed { get; } = new();

        public List<string> Stopped { get; } = new();

        public List<ContainerInfo> Existing { get; } = new();

        public Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_info);

        public Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default)
        {
            RunOrder.Add(spec.Labels.TryGetValue(WslccLabels.Service, out var svc) ? svc : spec.Name);
            RunSpecs.Add(spec);
            return Task.FromResult("id-" + spec.Name);
        }

        public Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            Stopped.Add(container);
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default)
        {
            Removed.Add(container);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(string? projectName, bool all, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContainerInfo>>(Existing);
    }

    [Fact]
    public async Task GetProviderInfos_returns_all_providers()
    {
        var engine = new ComposeEngine(new[] { new FakeProvider("wslc", true), new FakeProvider("docker", false) });

        var infos = await engine.GetProviderInfosAsync();

        Assert.Equal(2, infos.Count);
        Assert.Contains(infos, i => i.Name == "wslc" && i.IsAvailable);
        Assert.Contains(infos, i => i.Name == "docker" && !i.IsAvailable);
    }

    [Fact]
    public async Task GetProviderInfo_uses_default_when_none_requested()
    {
        var engine = new ComposeEngine(
            new[] { new FakeProvider("wslc", true), new FakeProvider("docker", true) },
            defaultProvider: "docker");

        var info = await engine.GetProviderInfoAsync(null);

        Assert.Equal("docker", info.Name);
    }

    [Fact]
    public async Task GetProviderInfo_throws_for_unknown_provider()
    {
        var engine = new ComposeEngine(new[] { new FakeProvider("wslc", true) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.GetProviderInfoAsync("nope"));
    }

    [Fact]
    public void ProviderNames_lists_registered_providers()
    {
        var engine = new ComposeEngine(new[] { new FakeProvider("wslc", true), new FakeProvider("docker", true) });

        Assert.Equal(new[] { "wslc", "docker" }, engine.ProviderNames);
    }

    private static ComposeFile TwoServiceFile()
    {
        var file = new ComposeFile();
        file.Services["web"] = new ServiceSpec { Name = "web", Image = "nginx", DependsOn = { "redis" } };
        file.Services["redis"] = new ServiceSpec { Name = "redis", Image = "redis:7" };
        return file;
    }

    [Fact]
    public async Task Up_starts_services_in_dependency_order_with_labels()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync("proj", TwoServiceFile(), providerName: null, pull: false);

        Assert.Equal(new[] { "redis", "web" }, provider.RunOrder);
        Assert.All(results, r => Assert.Equal("started", r.Status));

        var web = provider.RunSpecs.Single(s => s.Labels[WslccLabels.Service] == "web");
        Assert.Equal("proj-web", web.Name);
        Assert.Equal("proj", web.Labels[WslccLabels.Project]);
    }

    [Fact]
    public async Task Up_reports_failure_when_image_missing()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });
        var file = new ComposeFile();
        file.Services["svc"] = new ServiceSpec { Name = "svc" };

        var results = await engine.UpAsync("proj", file, providerName: null, pull: false);

        var result = Assert.Single(results);
        Assert.Equal("failed", result.Status);
        Assert.Empty(provider.RunOrder);
    }

    [Fact]
    public async Task Down_stops_and_removes_project_containers()
    {
        var provider = new FakeProvider("docker", true);
        provider.Existing.Add(new ContainerInfo("id1", "proj-web", "nginx", "running", Service: "web"));
        provider.Existing.Add(new ContainerInfo("id2", "proj-redis", "redis:7", "running", Service: "redis"));
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.DownAsync("proj", providerName: null);

        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Stopped);
        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Removed);
        Assert.All(results, r => Assert.Equal("removed", r.Status));
    }
}
