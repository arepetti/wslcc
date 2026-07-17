using Wslcc.Abstractions;
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
    public void BuildRunArguments_throws_when_no_image()
    {
        var spec = new ContainerRunSpec { Name = "x" };
        Assert.Throws<ProviderException>(() => CliCommandBuilder.BuildRunArguments(spec));
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
}
