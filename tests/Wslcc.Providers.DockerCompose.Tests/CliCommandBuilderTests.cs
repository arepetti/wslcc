using Wslcc.Abstractions;
using Wslcc.Abstractions.Compose;
using Wslcc.Providers.Common;

namespace Wslcc.Providers.DockerCompose.Tests;

public sealed class CliCommandBuilderTests
{
    [Fact]
    public void BuildRunArguments_includes_flags_and_image_last()
    {
        var spec = new ContainerRunSpec { Image = "nginx:1.27", Name = "proj-web", Detach = true };
        spec.Labels["wslcc.project"] = "proj";
        spec.Environment["MODE"] = "prod";
        spec.Ports.Add("8080:80");

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.StartsWith("run -d", args);
        Assert.Contains("--name proj-web", args);
        Assert.Contains("--label wslcc.project=proj", args);
        Assert.Contains("-e MODE=prod", args);
        Assert.Contains("-p 8080:80", args);
        Assert.EndsWith("nginx:1.27", args);
    }

    [Fact]
    public void BuildRunArguments_quotes_values_with_spaces()
    {
        var spec = new ContainerRunSpec { Image = "busybox", Name = "proj-svc" };
        spec.Environment["MSG"] = "hello world";

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("-e \"MSG=hello world\"", args);
    }

    [Fact]
    public void BuildRunArguments_includes_GA_resource_DNS_terminal_and_stop_options()
    {
        var spec = new ContainerRunSpec
        {
            Image = "busybox",
            DomainName = "dev.local",
            Gpus = "all",
            Cpus = "1.5",
            MemoryLimit = "512m",
            ShmSize = "128m",
            StdinOpen = true,
            Tty = true,
            StopSignal = "SIGINT",
            StopGracePeriod = "1m30s",
        };
        spec.Dns.Add("1.1.1.1");
        spec.DnsOptions.Add("use-vc");
        spec.DnsSearch.Add("dev.local");
        spec.Ulimits["nofile"] = "1024:2048";

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--domainname dev.local", args);
        Assert.Contains("--gpus all", args);
        Assert.Contains("--cpus 1.5", args);
        Assert.Contains("--memory 512m", args);
        Assert.Contains("--dns 1.1.1.1", args);
        Assert.Contains("--dns-option use-vc", args);
        Assert.Contains("--dns-search dev.local", args);
        Assert.Contains("--shm-size 128m", args);
        Assert.Contains("--ulimit nofile=1024:2048", args);
        Assert.Contains("--stop-signal SIGINT", args);
        Assert.Contains("--stop-timeout 90", args);
        Assert.Contains("-i", args);
        Assert.Contains("-t", args);
    }

    [Fact]
    public void BuildRunArguments_quotes_annotation_values_with_spaces()
    {
        var spec = new ContainerRunSpec { Image = "busybox", Name = "proj-svc" };
        spec.Annotations["com.example.note"] = "hello world";

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--annotation \"com.example.note=hello world\"", args);
    }

    [Fact]
    public void BuildRunArguments_appends_shell_form_command_after_image()
    {
        var spec = new ContainerRunSpec { Image = "busybox", Name = "proj-svc" };
        spec.Command.Add("/bin/sh");
        spec.Command.Add("-c");
        spec.Command.Add("npm start");

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.EndsWith("busybox /bin/sh -c \"npm start\"", args);
    }

    [Fact]
    public void BuildRunArguments_includes_user_workdir_hostname_readonly_entrypoint_env_file_and_labels()
    {
        var spec = new ContainerRunSpec
        {
            Image = "busybox",
            Name = "custom-name",
            User = "1000:1000",
            WorkingDir = "/app",
            Hostname = "api-node-1",
            ReadOnly = true,
        };
        spec.Labels["com.example.team"] = "platform";
        spec.Labels["wslcc.project"] = "proj";
        spec.Annotations["org.opencontainers.image.source"] = "https://github.com/acme/api";
        spec.EnvFiles.Add(@"C:\proj\a.env");
        spec.Environment["NODE_ENV"] = "production";
        spec.Entrypoint.Add("/bin/sh");
        spec.Entrypoint.Add("-c");
        spec.Entrypoint.Add("echo hi");
        spec.Command.Add("extra");

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--name custom-name", args);
        Assert.Contains("--label com.example.team=platform", args);
        Assert.Contains("--label wslcc.project=proj", args);
        Assert.Contains("--annotation org.opencontainers.image.source=https://github.com/acme/api", args);
        Assert.Contains("--env-file", args);
        Assert.Contains("-e NODE_ENV=production", args);
        Assert.Contains("-u 1000:1000", args);
        Assert.Contains("-w /app", args);
        Assert.Contains("--hostname api-node-1", args);
        Assert.Contains("--read-only", args);
        Assert.Contains("--entrypoint /bin/sh", args);
        Assert.EndsWith("busybox -c \"echo hi\" extra", args);
        Assert.True(args.IndexOf("--env-file", StringComparison.Ordinal) < args.IndexOf("-e NODE_ENV", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRunArguments_omits_readonly_when_false()
    {
        var spec = new ContainerRunSpec { Image = "busybox", ReadOnly = false };
        var args = CliCommandBuilder.BuildRunArguments(spec);
        Assert.DoesNotContain("--read-only", args);
    }

    [Fact]
    public void BuildRunArguments_throws_when_no_image()
    {
        var spec = new ContainerRunSpec { Name = "x" };
        Assert.Throws<ProviderException>(() => CliCommandBuilder.BuildRunArguments(spec));
    }

    [Fact]
    public void BuildRunArguments_includes_healthcheck_flags()
    {
        var spec = new ContainerRunSpec { Image = "nginx", Name = "proj-web" };
        spec.HealthCheck = new ContainerHealthCheck
        {
            Command = "curl -f http://localhost",
            Interval = "30s",
            Timeout = "5s",
            Retries = 3,
            StartPeriod = "10s",
        };

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--health-cmd \"curl -f http://localhost\"", args);
        Assert.Contains("--health-interval 30s", args);
        Assert.Contains("--health-timeout 5s", args);
        Assert.Contains("--health-retries 3", args);
        Assert.Contains("--health-start-period 10s", args);
        Assert.EndsWith("nginx", args);
    }

    [Fact]
    public void BuildRunArguments_disables_healthcheck()
    {
        var spec = new ContainerRunSpec
        {
            Image = "nginx",
            Name = "proj-web",
            HealthCheck = new ContainerHealthCheck { Disabled = true },
        };

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--no-healthcheck", args);
        Assert.DoesNotContain("--health-cmd", args);
    }

    [Fact]
    public void BuildRunArguments_includes_network_alias_and_volumes()
    {
        var spec = new ContainerRunSpec { Image = "nginx", Name = "proj-web", Network = "proj_default", NetworkAlias = "web" };
        spec.Volumes.Add(ServiceMount.FromShortSyntax("proj_data:/var/lib"));
        spec.Volumes.Add(ServiceMount.FromShortSyntax("/host:/app:ro"));

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--network proj_default", args);
        Assert.Contains("--network-alias web", args);
        Assert.Contains("-v proj_data:/var/lib", args);
        Assert.Contains("-v /host:/app:ro", args);
        Assert.EndsWith("nginx", args);
    }

    [Fact]
    public void BuildRunArguments_emits_tmpfs_and_mount_flag()
    {
        var spec = new ContainerRunSpec { Image = "nginx", Name = "proj-web" };
        spec.Volumes.Add(ServiceMount.FromTmpfsShortSyntax("/run:size=64m,mode=1777"));
        spec.Volumes.Add(new ServiceMount
        {
            Type = MountType.Volume,
            Source = "proj_data",
            Target = "/var/lib",
            VolumeNocopy = true,
        });

        var args = CliCommandBuilder.BuildRunArguments(spec);

        Assert.Contains("--tmpfs /run:size=64m,mode=1777", args);
        Assert.Contains("--mount type=volume,source=proj_data,target=/var/lib,volume-nocopy", args);
        Assert.DoesNotContain("-v proj_data", args);
    }

    [Fact]
    public void BuildNetworkCreateArguments_includes_driver_and_labels_with_name_last()
    {
        var spec = new NetworkCreateSpec { Name = "proj_backend", Driver = "bridge" };
        spec.Labels["wslcc.project"] = "proj";

        var args = CliCommandBuilder.BuildNetworkCreateArguments(spec);

        Assert.StartsWith("network create", args);
        Assert.Contains("--driver bridge", args);
        Assert.Contains("--label wslcc.project=proj", args);
        Assert.EndsWith("proj_backend", args);
    }

    [Fact]
    public void BuildVolumeCreateArguments_includes_labels_with_name_last()
    {
        var spec = new VolumeCreateSpec { Name = "proj_data" };
        spec.Labels["wslcc.volume"] = "data";

        var args = CliCommandBuilder.BuildVolumeCreateArguments(spec);

        Assert.StartsWith("volume create", args);
        Assert.Contains("--label wslcc.volume=data", args);
        Assert.EndsWith("proj_data", args);
    }

    [Fact]
    public void BuildNetworkConnectArguments_includes_alias()
    {
        var args = CliCommandBuilder.BuildNetworkConnectArguments(
            "proj_backend",
            "proj-web",
            new[] { "web" },
            ipv4Address: null);

        Assert.Equal("network connect --alias web proj_backend proj-web", args);
    }

    [Fact]
    public void BuildNetworkListNamesArguments_filters_by_project_label()
    {
        var args = CliCommandBuilder.BuildNetworkListNamesArguments("proj");

        Assert.StartsWith("network ls", args);
        Assert.Contains("--filter label=wslcc.project=proj", args);
        Assert.Contains("--format {{.Name}}", args);
    }

    [Fact]
    public void BuildVolumeRemoveArguments_targets_the_volume()
    {
        Assert.Equal("volume rm proj_data", CliCommandBuilder.BuildVolumeRemoveArguments("proj_data"));
        Assert.Equal("network rm proj_default", CliCommandBuilder.BuildNetworkRemoveArguments("proj_default"));
    }

    [Fact]
    public void BuildInspectStateArguments_targets_container_with_the_state_format()
    {
        var args = CliCommandBuilder.BuildInspectStateArguments("proj-web");

        Assert.StartsWith("container inspect --format", args);
        Assert.EndsWith("proj-web", args);
    }

    [Fact]
    public void BuildPsArguments_filters_by_project_label()
    {
        var args = CliCommandBuilder.BuildPsArguments("proj", all: true);

        Assert.Contains("ps", args);
        Assert.Contains("--all", args);
        Assert.Contains("--filter", args);
        Assert.Contains("label=wslcc.project=proj", args);
        Assert.Contains("--format", args);
    }

    [Fact]
    public void BuildPsArguments_without_project_filters_by_label_presence()
    {
        var args = CliCommandBuilder.BuildPsArguments(null, all: false);

        Assert.Contains("label=wslcc.project", args);
        Assert.DoesNotContain("--all", args);
    }

    [Fact]
    public void BuildStartArguments_targets_the_container()
    {
        var args = CliCommandBuilder.BuildStartArguments("proj-web");

        Assert.Equal("start proj-web", args);
    }

    [Fact]
    public void BuildRestartArguments_targets_the_container()
    {
        var args = CliCommandBuilder.BuildRestartArguments("proj-web");

        Assert.Equal("restart proj-web", args);
    }

    [Fact]
    public void BuildBuildArguments_includes_tag_dockerfile_target_and_args_with_context_last()
    {
        var spec = new ImageBuildSpec
        {
            Context = "./web",
            Dockerfile = "Dockerfile.dev",
            Target = "prod",
            Tag = "proj-web",
            NoCache = true,
            Pull = true,
        };
        spec.Args["VERSION"] = "1.2.3";
        spec.Labels["org.example.build"] = "test";
        spec.Secrets.Add("id=certificate,src=certificate");

        var args = CliCommandBuilder.BuildBuildArguments(spec);

        Assert.StartsWith("build", args);
        Assert.Contains("-t proj-web", args);
        Assert.Contains("-f Dockerfile.dev", args);
        Assert.Contains("--target prod", args);
        Assert.Contains("--build-arg VERSION=1.2.3", args);
        Assert.Contains("--label org.example.build=test", args);
        Assert.Contains("--no-cache", args);
        Assert.Contains("--pull", args);
        Assert.Contains("--secret id=certificate,src=certificate", args);
        Assert.EndsWith("./web", args);
    }

    [Fact]
    public void BuildBuildArguments_throws_when_no_context()
    {
        var spec = new ImageBuildSpec { Tag = "proj-web" };
        Assert.Throws<ProviderException>(() => CliCommandBuilder.BuildBuildArguments(spec));
    }

    [Fact]
    public void BuildLogsArguments_includes_follow_and_tail()
    {
        var args = CliCommandBuilder.BuildLogsArguments("proj-web", follow: true, tail: 50);

        Assert.Equal("logs --follow --tail 50 proj-web", args);
    }

    [Fact]
    public void BuildLogsArguments_omits_follow_and_tail_when_not_requested()
    {
        var args = CliCommandBuilder.BuildLogsArguments("proj-web", follow: false, tail: null);

        Assert.Equal("logs proj-web", args);
    }

    [Fact]
    public void BuildLogsArguments_includes_timestamps_and_since()
    {
        var args = CliCommandBuilder.BuildLogsArguments("proj-web", follow: true, tail: null, timestamps: true, since: "10m");

        Assert.Equal("logs --follow --timestamps --since 10m proj-web", args);
    }
}
