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

        public List<string> Started { get; } = new();

        public List<string> Restarted { get; } = new();

        public List<ContainerInfo> Existing { get; } = new();

        public List<(string Image, bool AlwaysPull)> EnsuredImages { get; } = new();

        public List<ImageBuildSpec> BuildSpecs { get; } = new();

        /// <summary>When set, the operation on this container name fails with a <see cref="ProviderException"/>.</summary>
        public string? FailContainer { get; set; }

        /// <summary>When set, <see cref="EnsureImageAsync"/> for this image fails with a <see cref="ProviderException"/>.</summary>
        public string? FailImage { get; set; }

        /// <summary>When set, <see cref="BuildImageAsync"/> for this tag fails with a <see cref="ProviderException"/>.</summary>
        public string? FailBuildTag { get; set; }

        public Task<ProviderInfo> GetProviderInfoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_info);

        public Task EnsureImageAsync(string image, bool alwaysPull, CancellationToken cancellationToken = default)
        {
            EnsuredImages.Add((image, alwaysPull));
            if (image == FailImage)
            {
                throw new ProviderException($"boom: {image}");
            }

            return Task.CompletedTask;
        }

        public Task BuildImageAsync(ImageBuildSpec spec, CancellationToken cancellationToken = default)
        {
            BuildSpecs.Add(spec);
            if (spec.Tag == FailBuildTag)
            {
                throw new ProviderException($"boom: {spec.Tag}");
            }

            return Task.CompletedTask;
        }

        public Task<string> RunContainerAsync(ContainerRunSpec spec, CancellationToken cancellationToken = default)
        {
            RunOrder.Add(spec.Labels.TryGetValue(WslccLabels.Service, out var svc) ? svc : spec.Name);
            RunSpecs.Add(spec);
            return Task.FromResult("id-" + spec.Name);
        }

        public Task StopContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            ThrowIfShouldFail(container);
            Stopped.Add(container);
            return Task.CompletedTask;
        }

        public Task StartContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            ThrowIfShouldFail(container);
            Started.Add(container);
            return Task.CompletedTask;
        }

        public Task RestartContainerAsync(string container, CancellationToken cancellationToken = default)
        {
            ThrowIfShouldFail(container);
            Restarted.Add(container);
            return Task.CompletedTask;
        }

        private void ThrowIfShouldFail(string container)
        {
            if (container == FailContainer)
            {
                throw new ProviderException($"boom: {container}");
            }
        }

        public Task RemoveContainerAsync(string container, bool force, CancellationToken cancellationToken = default)
        {
            Removed.Add(container);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(string? projectName, bool all, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContainerInfo>>(Existing);

        /// <summary>Maps a container name to the canned lines it should "emit" for <see cref="GetLogsAsync"/>.</summary>
        public Dictionary<string, string[]> Logs { get; } = new();

        public async IAsyncEnumerable<string> GetLogsAsync(
            string container,
            bool follow,
            int? tail,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!Logs.TryGetValue(container, out var lines))
            {
                yield break;
            }

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }
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

    private static FakeProvider ProviderWithTwoContainers()
    {
        var provider = new FakeProvider("docker", true);
        provider.Existing.Add(new ContainerInfo("id1", "proj-web", "nginx", "running", Service: "web"));
        provider.Existing.Add(new ContainerInfo("id2", "proj-redis", "redis:7", "exited", Service: "redis"));
        return provider;
    }

    [Fact]
    public async Task Start_starts_every_existing_container_when_no_services_given()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.StartAsync("proj", providerName: null, services: null);

        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Started);
        Assert.All(results, r => Assert.Equal("started", r.Status));
    }

    [Fact]
    public async Task Start_filters_by_requested_services()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.StartAsync("proj", providerName: null, services: new[] { "web" });

        Assert.Equal(new[] { "proj-web" }, provider.Started);
        var result = Assert.Single(results);
        Assert.Equal("web", result.Service);
    }

    [Fact]
    public async Task Stop_stops_matching_containers_and_reports_failures()
    {
        var provider = ProviderWithTwoContainers();
        provider.FailContainer = "proj-redis";
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.StopAsync("proj", providerName: null, services: null);

        Assert.Equal(new[] { "proj-web" }, provider.Stopped);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Service == "web" && r.Status == "stopped");
        Assert.Contains(results, r => r.Service == "redis" && r.Status == "failed" && r.Error!.Contains("proj-redis"));
    }

    [Fact]
    public async Task Restart_restarts_every_existing_container()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.RestartAsync("proj", providerName: null, services: null);

        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Restarted);
        Assert.All(results, r => Assert.Equal("restarted", r.Status));
    }

    [Fact]
    public async Task Pull_pulls_every_service_image_regardless_of_local_cache()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.PullAsync(TwoServiceFile(), providerName: null, services: null);

        Assert.Equal(2, provider.EnsuredImages.Count);
        Assert.All(provider.EnsuredImages, i => Assert.True(i.AlwaysPull));
        Assert.Contains(provider.EnsuredImages, i => i.Image == "nginx");
        Assert.Contains(provider.EnsuredImages, i => i.Image == "redis:7");
        Assert.All(results, r => Assert.Equal("pulled", r.Status));
    }

    [Fact]
    public async Task Pull_filters_by_requested_services()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.PullAsync(TwoServiceFile(), providerName: null, services: new[] { "redis" });

        var pulled = Assert.Single(provider.EnsuredImages);
        Assert.Equal("redis:7", pulled.Image);
        var result = Assert.Single(results);
        Assert.Equal("redis", result.Service);
    }

    [Fact]
    public async Task Pull_reports_failure_for_missing_image_and_provider_errors()
    {
        var provider = new FakeProvider("docker", true);
        provider.FailImage = "redis:7";
        var engine = new ComposeEngine(new[] { provider });
        var file = TwoServiceFile();
        file.Services["nolimage"] = new ServiceSpec { Name = "nolimage" };

        var results = await engine.PullAsync(file, providerName: null, services: null);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Service == "web" && r.Status == "pulled");
        Assert.Contains(results, r => r.Service == "redis" && r.Status == "failed" && r.Error!.Contains("redis:7"));
        Assert.Contains(results, r => r.Service == "nolimage" && r.Status == "failed");
    }

    private static ComposeFile FileWithBuildableService(string? image = null)
    {
        var file = new ComposeFile();
        file.Services["web"] = new ServiceSpec
        {
            Name = "web",
            Image = image,
            Build = new BuildSpec { Context = "./web", Dockerfile = "Dockerfile.dev" },
        };
        file.Services["redis"] = new ServiceSpec { Name = "redis", Image = "redis:7" };
        return file;
    }

    [Fact]
    public async Task Build_builds_only_services_with_a_build_section()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.BuildAsync("proj", FileWithBuildableService(), providerName: null, baseDirectory: null, services: null);

        var result = Assert.Single(results);
        Assert.Equal("web", result.Service);
        Assert.Equal("built", result.Status);

        var spec = Assert.Single(provider.BuildSpecs);
        Assert.Equal("proj-web", spec.Tag);
        Assert.Equal("Dockerfile.dev", spec.Dockerfile);
    }

    [Fact]
    public async Task Build_uses_the_service_image_as_tag_when_specified()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        await engine.BuildAsync("proj", FileWithBuildableService(image: "myrepo/web:latest"), providerName: null, baseDirectory: null, services: null);

        var spec = Assert.Single(provider.BuildSpecs);
        Assert.Equal("myrepo/web:latest", spec.Tag);
    }

    [Fact]
    public async Task Build_resolves_relative_context_against_base_directory()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });
        var baseDir = Path.Combine(Path.GetTempPath(), "wslcc-test-project");

        await engine.BuildAsync("proj", FileWithBuildableService(), providerName: null, baseDirectory: baseDir, services: null);

        var spec = Assert.Single(provider.BuildSpecs);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "web")), spec.Context);
    }

    [Fact]
    public async Task Build_reports_failure_from_the_provider()
    {
        var provider = new FakeProvider("docker", true);
        provider.FailBuildTag = "proj-web";
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.BuildAsync("proj", FileWithBuildableService(), providerName: null, baseDirectory: null, services: null);

        var result = Assert.Single(results);
        Assert.Equal("failed", result.Status);
        Assert.Contains("proj-web", result.Error);
    }

    [Fact]
    public async Task GetLogs_merges_lines_from_every_container_tagged_by_service()
    {
        var provider = ProviderWithTwoContainers();
        provider.Logs["proj-web"] = new[] { "web line 1", "web line 2" };
        provider.Logs["proj-redis"] = new[] { "redis line 1" };
        var engine = new ComposeEngine(new[] { provider });

        var lines = new List<ServiceLogLine>();
        await foreach (var line in engine.GetLogsAsync("proj", providerName: null, services: null, follow: false, tail: null))
        {
            lines.Add(line);
        }

        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.Service == "web" && l.Line == "web line 1");
        Assert.Contains(lines, l => l.Service == "web" && l.Line == "web line 2");
        Assert.Contains(lines, l => l.Service == "redis" && l.Line == "redis line 1");
    }

    [Fact]
    public async Task GetLogs_filters_by_requested_services()
    {
        var provider = ProviderWithTwoContainers();
        provider.Logs["proj-web"] = new[] { "web line 1" };
        provider.Logs["proj-redis"] = new[] { "redis line 1" };
        var engine = new ComposeEngine(new[] { provider });

        var lines = new List<ServiceLogLine>();
        await foreach (var line in engine.GetLogsAsync("proj", providerName: null, services: new[] { "redis" }, follow: false, tail: null))
        {
            lines.Add(line);
        }

        var single = Assert.Single(lines);
        Assert.Equal("redis", single.Service);
    }

    [Fact]
    public async Task GetLogs_yields_nothing_when_project_has_no_containers()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var lines = new List<ServiceLogLine>();
        await foreach (var line in engine.GetLogsAsync("proj", providerName: null, services: null, follow: false, tail: null))
        {
            lines.Add(line);
        }

        Assert.Empty(lines);
    }
}
