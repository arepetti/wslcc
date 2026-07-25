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

        /// <summary>Images that <see cref="ImageExistsAsync"/> should report as already present locally.</summary>
        public HashSet<string> ExistingImages { get; } = new(StringComparer.Ordinal);

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

        public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
            => Task.FromResult(ExistingImages.Contains(image));

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

        /// <summary>Maps a container name to the runtime state <see cref="GetContainerStateAsync"/> reports.</summary>
        public Dictionary<string, ContainerRuntimeState> States { get; } = new(StringComparer.Ordinal);

        public Task<ContainerRuntimeState?> GetContainerStateAsync(string container, CancellationToken cancellationToken = default)
            => Task.FromResult(States.TryGetValue(container, out var state) ? state : null);

        public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(string? projectName, bool all, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContainerInfo>>(Existing);

        /// <summary>Maps a container name to the canned lines it should "emit" for <see cref="GetLogsAsync"/>.</summary>
        public Dictionary<string, ContainerLogLine[]> Logs { get; } = new();

        /// <summary>Records the flags each <see cref="GetLogsAsync"/> call was made with, per container.</summary>
        public List<(string Container, bool Follow, int? Tail, bool Timestamps, string? Since)> LogCalls { get; } = new();

        public async IAsyncEnumerable<ContainerLogLine> GetLogsAsync(
            string container,
            bool follow,
            int? tail,
            bool timestamps,
            string? since,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LogCalls.Add((container, follow, tail, timestamps, since));

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

    private static ContainerLogLine Line(string message, DateTimeOffset? timestamp = null)
        => new(timestamp, message);

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

        var results = await engine.UpAsync("proj", TwoServiceFile(), providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

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

        var results = await engine.UpAsync("proj", file, providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        var result = Assert.Single(results);
        Assert.Equal("failed", result.Status);
        Assert.Empty(provider.RunOrder);
    }

    [Fact]
    public async Task Up_auto_builds_a_build_only_service_then_runs_the_built_tag()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync("proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.All(results, r => Assert.Equal("started", r.Status));

        var built = Assert.Single(provider.BuildSpecs);
        Assert.Equal("proj-web", built.Tag);

        // The container for the build-only service runs the freshly built tag, not an empty image.
        var web = provider.RunSpecs.Single(s => s.Labels[WslccLabels.Service] == "web");
        Assert.Equal("proj-web", web.Image);

        // Services that only reference an image are still pulled/ensured, never built.
        Assert.Contains(provider.EnsuredImages, i => i.Image == "redis:7");
    }

    [Fact]
    public async Task Up_skips_the_build_when_the_target_image_already_exists()
    {
        var provider = new FakeProvider("docker", true);
        provider.ExistingImages.Add("proj-web");
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync("proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.All(results, r => Assert.Equal("started", r.Status));
        Assert.Empty(provider.BuildSpecs);

        var web = provider.RunSpecs.Single(s => s.Labels[WslccLabels.Service] == "web");
        Assert.Equal("proj-web", web.Image);
    }

    [Fact]
    public async Task Up_tags_the_auto_build_as_the_service_image_when_specified()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        await engine.UpAsync(
            "proj", FileWithBuildableService(image: "myrepo/web:latest"), providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        var built = Assert.Single(provider.BuildSpecs);
        Assert.Equal("myrepo/web:latest", built.Tag);

        var web = provider.RunSpecs.Single(s => s.Labels[WslccLabels.Service] == "web");
        Assert.Equal("myrepo/web:latest", web.Image);
    }

    [Fact]
    public async Task Up_resolves_the_auto_build_context_against_the_base_directory()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });
        var baseDir = Path.Combine(Path.GetTempPath(), "wslcc-up-project");

        await engine.UpAsync("proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: baseDir);

        var built = Assert.Single(provider.BuildSpecs);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "web")), built.Context);
    }

    [Fact]
    public async Task Up_reports_a_build_failure_and_does_not_run_the_service()
    {
        var provider = new FakeProvider("docker", true);
        provider.FailBuildTag = "proj-web";
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync("proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Contains(results, r => r.Service == "web" && r.Status == "failed" && r.Error!.Contains("proj-web"));
        Assert.DoesNotContain(provider.RunOrder, s => s == "web");
        Assert.Contains(provider.RunOrder, s => s == "redis");
    }

    [Fact]
    public async Task Up_with_build_policy_always_rebuilds_even_when_the_image_exists()
    {
        var provider = new FakeProvider("docker", true);
        provider.ExistingImages.Add("proj-web");
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Always, baseDirectory: null);

        Assert.All(results, r => Assert.Equal("started", r.Status));
        var built = Assert.Single(provider.BuildSpecs);
        Assert.Equal("proj-web", built.Tag);
    }

    [Fact]
    public async Task Up_with_build_policy_never_runs_the_existing_image_without_building()
    {
        var provider = new FakeProvider("docker", true);
        provider.ExistingImages.Add("proj-web");
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Never, baseDirectory: null);

        Assert.All(results, r => Assert.Equal("started", r.Status));
        Assert.Empty(provider.BuildSpecs);
        var web = provider.RunSpecs.Single(s => s.Labels[WslccLabels.Service] == "web");
        Assert.Equal("proj-web", web.Image);
    }

    [Fact]
    public async Task Up_with_build_policy_never_fails_when_the_image_is_missing()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", FileWithBuildableService(), providerName: null, pull: false, buildPolicy: BuildPolicy.Never, baseDirectory: null);

        Assert.Empty(provider.BuildSpecs);
        Assert.Contains(results, r => r.Service == "web" && r.Status == "failed" && r.Error!.Contains("--no-build"));
        Assert.DoesNotContain(provider.RunOrder, s => s == "web");
    }

    private static ComposeFile DependencyFile(DependencyCondition condition, bool dependencyHasHealthCheck = false)
    {
        var file = new ComposeFile();
        file.Services["web"] = new ServiceSpec
        {
            Name = "web",
            Image = "nginx",
            DependsOn = { new ServiceDependency("db", condition) },
        };

        var db = new ServiceSpec { Name = "db", Image = "postgres" };
        if (dependencyHasHealthCheck)
        {
            db.HealthCheck = new HealthCheckSpec { Test = { "CMD-SHELL", "pg_isready" } };
        }

        file.Services["db"] = db;
        return file;
    }

    [Fact]
    public async Task Up_waits_for_a_healthy_dependency_then_starts_the_dependent()
    {
        var provider = new FakeProvider("docker", true);
        provider.States["proj-db"] = new ContainerRuntimeState("running", HealthStatus.Healthy, null);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", DependencyFile(DependencyCondition.ServiceHealthy, dependencyHasHealthCheck: true),
            providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Equal(new[] { "db", "web" }, provider.RunOrder);
        Assert.All(results, r => Assert.Equal("started", r.Status));
    }

    [Fact]
    public async Task Up_fails_the_dependent_when_the_dependency_is_unhealthy()
    {
        var provider = new FakeProvider("docker", true);
        provider.States["proj-db"] = new ContainerRuntimeState("running", HealthStatus.Unhealthy, null);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", DependencyFile(DependencyCondition.ServiceHealthy, dependencyHasHealthCheck: true),
            providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Contains(results, r => r.Service == "db" && r.Status == "started");
        Assert.Contains(results, r => r.Service == "web" && r.Status == "failed" && r.Error!.Contains("unhealthy"));
        Assert.DoesNotContain(provider.RunOrder, s => s == "web");
    }

    [Fact]
    public async Task Up_fails_when_service_healthy_dependency_has_no_healthcheck()
    {
        var provider = new FakeProvider("docker", true);
        provider.States["proj-db"] = new ContainerRuntimeState("running", HealthStatus.None, null);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", DependencyFile(DependencyCondition.ServiceHealthy, dependencyHasHealthCheck: false),
            providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Contains(results, r => r.Service == "web" && r.Status == "failed" && r.Error!.Contains("no healthcheck"));
    }

    [Fact]
    public async Task Up_waits_for_a_dependency_to_complete_successfully()
    {
        var provider = new FakeProvider("docker", true);
        provider.States["proj-db"] = new ContainerRuntimeState("exited", HealthStatus.None, 0);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", DependencyFile(DependencyCondition.ServiceCompletedSuccessfully),
            providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Equal(new[] { "db", "web" }, provider.RunOrder);
        Assert.All(results, r => Assert.Equal("started", r.Status));
    }

    [Fact]
    public async Task Up_fails_the_dependent_when_the_dependency_exits_non_zero()
    {
        var provider = new FakeProvider("docker", true);
        provider.States["proj-db"] = new ContainerRuntimeState("exited", HealthStatus.None, 1);
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", DependencyFile(DependencyCondition.ServiceCompletedSuccessfully),
            providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Contains(results, r => r.Service == "web" && r.Status == "failed" && r.Error!.Contains("exit code 1"));
        Assert.DoesNotContain(provider.RunOrder, s => s == "web");
    }

    [Fact]
    public async Task Up_skips_a_dependent_when_its_required_dependency_fails_to_start()
    {
        var provider = new FakeProvider("docker", true);
        provider.FailImage = "postgres"; // the db dependency cannot pull its image
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.UpAsync(
            "proj", DependencyFile(DependencyCondition.ServiceStarted),
            providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        Assert.Contains(results, r => r.Service == "db" && r.Status == "failed");
        Assert.Contains(results, r => r.Service == "web" && r.Status == "failed" && r.Error!.Contains("dependency 'db'"));
        Assert.Empty(provider.RunOrder);
    }

    [Fact]
    public async Task Up_applies_a_service_healthcheck_to_the_run_spec()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });
        var file = new ComposeFile();
        file.Services["web"] = new ServiceSpec
        {
            Name = "web",
            Image = "nginx",
            HealthCheck = new HealthCheckSpec
            {
                Test = { "CMD-SHELL", "curl -f http://localhost || exit 1" },
                Interval = "30s",
                Retries = 3,
            },
        };

        await engine.UpAsync("proj", file, providerName: null, pull: false, buildPolicy: BuildPolicy.Auto, baseDirectory: null);

        var spec = Assert.Single(provider.RunSpecs);
        Assert.NotNull(spec.HealthCheck);
        Assert.Equal("curl -f http://localhost || exit 1", spec.HealthCheck!.Command);
        Assert.Equal("30s", spec.HealthCheck.Interval);
        Assert.Equal(3, spec.HealthCheck.Retries);
    }

    [Fact]
    public async Task Down_stops_and_removes_project_containers()
    {
        var provider = new FakeProvider("docker", true);
        provider.Existing.Add(new ContainerInfo("id1", "proj-web", "nginx", "running", Service: "web"));
        provider.Existing.Add(new ContainerInfo("id2", "proj-redis", "redis:7", "running", Service: "redis"));
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.DownAsync("proj", file: null, providerName: null);

        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Stopped);
        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Removed);
        Assert.All(results, r => Assert.Equal("removed", r.Status));
    }

    [Fact]
    public async Task Down_tears_down_in_reverse_dependency_order_when_a_file_is_provided()
    {
        var provider = new FakeProvider("docker", true);
        provider.Existing.Add(new ContainerInfo("id1", "proj-redis", "redis:7", "running", Service: "redis"));
        provider.Existing.Add(new ContainerInfo("id2", "proj-web", "nginx", "running", Service: "web"));
        var engine = new ComposeEngine(new[] { provider });

        await engine.DownAsync("proj", TwoServiceFile(), providerName: null);

        // web depends_on redis, so the dependent (web) is stopped/removed before its dependency (redis).
        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Stopped);
        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Removed);
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

        var results = await engine.StartAsync("proj", file: null, providerName: null, services: null);

        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Started);
        Assert.All(results, r => Assert.Equal("started", r.Status));
    }

    [Fact]
    public async Task Start_filters_by_requested_services()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        var results = await engine.StartAsync("proj", file: null, providerName: null, services: new[] { "web" });

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

        var results = await engine.StopAsync("proj", file: null, providerName: null, services: null);

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

        var results = await engine.RestartAsync("proj", file: null, providerName: null, services: null);

        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Restarted);
        Assert.All(results, r => Assert.Equal("restarted", r.Status));
    }

    [Fact]
    public async Task Start_orders_containers_by_dependency_when_a_file_is_provided()
    {
        // Containers are listed web-then-redis, but web depends_on redis.
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        await engine.StartAsync("proj", TwoServiceFile(), providerName: null, services: null);

        Assert.Equal(new[] { "proj-redis", "proj-web" }, provider.Started);
    }

    [Fact]
    public async Task Stop_orders_containers_in_reverse_dependency_when_a_file_is_provided()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        await engine.StopAsync("proj", TwoServiceFile(), providerName: null, services: null);

        // Teardown reverses the order: the dependent (web) stops before its dependency (redis).
        Assert.Equal(new[] { "proj-web", "proj-redis" }, provider.Stopped);
    }

    [Fact]
    public async Task Start_rejects_an_unknown_service_name()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => engine.StartAsync("proj", TwoServiceFile(), providerName: null, services: new[] { "web", "ghost" }));

        Assert.Contains("ghost", ex.Message);
        Assert.Empty(provider.Started);
    }

    [Fact]
    public async Task Start_validates_against_existing_containers_when_no_file_is_provided()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => engine.StartAsync("proj", file: null, providerName: null, services: new[] { "ghost" }));

        Assert.Contains("ghost", ex.Message);
        Assert.Empty(provider.Started);
    }

    [Fact]
    public async Task GetLogs_rejects_an_unknown_service_name()
    {
        var provider = ProviderWithTwoContainers();
        var engine = new ComposeEngine(new[] { provider });

        await Assert.ThrowsAsync<ProviderException>(async () =>
        {
            await foreach (var _ in engine.GetLogsAsync(
                "proj", TwoServiceFile(), providerName: null, services: new[] { "ghost" }, follow: false, tail: null, timestamps: false, since: null))
            {
            }
        });
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

    [Fact]
    public async Task Pull_rejects_an_unknown_service_name()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => engine.PullAsync(TwoServiceFile(), providerName: null, services: new[] { "web", "ghost" }));

        Assert.Contains("ghost", ex.Message);
        Assert.Empty(provider.EnsuredImages);
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
    public async Task Build_rejects_an_unknown_service_name()
    {
        var provider = new FakeProvider("docker", true);
        var engine = new ComposeEngine(new[] { provider });

        var ex = await Assert.ThrowsAsync<ProviderException>(
            () => engine.BuildAsync("proj", FileWithBuildableService(), providerName: null, baseDirectory: null, services: new[] { "ghost" }));

        Assert.Contains("ghost", ex.Message);
        Assert.Empty(provider.BuildSpecs);
    }

    [Fact]
    public async Task GetLogs_merges_lines_from_every_container_tagged_by_service()
    {
        var provider = ProviderWithTwoContainers();
        provider.Logs["proj-web"] = new[] { Line("web line 1"), Line("web line 2") };
        provider.Logs["proj-redis"] = new[] { Line("redis line 1") };
        var engine = new ComposeEngine(new[] { provider });

        var lines = new List<ServiceLogLine>();
        await foreach (var line in engine.GetLogsAsync("proj", file: null, providerName: null, services: null, follow: false, tail: null, timestamps: false, since: null))
        {
            lines.Add(line);
        }

        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.Service == "web" && l.Line == "web line 1");
        Assert.Contains(lines, l => l.Service == "web" && l.Line == "web line 2");
        Assert.Contains(lines, l => l.Service == "redis" && l.Line == "redis line 1");
    }

    [Fact]
    public async Task GetLogs_without_follow_merges_containers_in_timestamp_order()
    {
        var provider = ProviderWithTwoContainers();
        var t0 = DateTimeOffset.Parse("2024-05-01T00:00:00Z");
        // Interleave two containers so a container-by-container dump would NOT be chronological.
        provider.Logs["proj-web"] = new[] { Line("web @0s", t0), Line("web @2s", t0.AddSeconds(2)) };
        provider.Logs["proj-redis"] = new[] { Line("redis @1s", t0.AddSeconds(1)), Line("redis @3s", t0.AddSeconds(3)) };
        var engine = new ComposeEngine(new[] { provider });

        var lines = new List<ServiceLogLine>();
        await foreach (var line in engine.GetLogsAsync("proj", file: null, providerName: null, services: null, follow: false, tail: null, timestamps: true, since: null))
        {
            lines.Add(line);
        }

        Assert.Equal(
            new[] { "web @0s", "redis @1s", "web @2s", "redis @3s" },
            lines.Select(l => l.Line).ToArray());
    }

    [Fact]
    public async Task GetLogs_without_follow_requests_timestamps_even_when_not_asked_to_display_them()
    {
        // A bounded dump must be merged chronologically, so the engine still fetches timestamps.
        var provider = ProviderWithTwoContainers();
        provider.Logs["proj-web"] = new[] { Line("web line") };
        var engine = new ComposeEngine(new[] { provider });

        await foreach (var _ in engine.GetLogsAsync("proj", file: null, providerName: null, services: null, follow: false, tail: null, timestamps: false, since: "10m"))
        {
        }

        Assert.All(provider.LogCalls, c => Assert.True(c.Timestamps));
        Assert.All(provider.LogCalls, c => Assert.Equal("10m", c.Since));
    }

    [Fact]
    public async Task GetLogs_filters_by_requested_services()
    {
        var provider = ProviderWithTwoContainers();
        provider.Logs["proj-web"] = new[] { Line("web line 1") };
        provider.Logs["proj-redis"] = new[] { Line("redis line 1") };
        var engine = new ComposeEngine(new[] { provider });

        var lines = new List<ServiceLogLine>();
        await foreach (var line in engine.GetLogsAsync("proj", file: null, providerName: null, services: new[] { "redis" }, follow: false, tail: null, timestamps: false, since: null))
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
        await foreach (var line in engine.GetLogsAsync("proj", file: null, providerName: null, services: null, follow: false, tail: null, timestamps: false, since: null))
        {
            lines.Add(line);
        }

        Assert.Empty(lines);
    }
}
