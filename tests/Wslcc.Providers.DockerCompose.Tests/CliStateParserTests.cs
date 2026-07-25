using Wslcc.Abstractions;
using Wslcc.Providers.Common;

namespace Wslcc.Providers.DockerCompose.Tests;

public sealed class CliStateParserTests
{
    private const char Us = '\u001f';

    [Fact]
    public void Parse_reads_status_health_and_exit_code()
    {
        var state = CliStateParser.Parse($"running{Us}healthy{Us}0");

        Assert.Equal("running", state.Status);
        Assert.Equal(HealthStatus.Healthy, state.Health);
        Assert.Equal(0, state.ExitCode);
        Assert.False(state.HasExited);
    }

    [Fact]
    public void Parse_maps_empty_health_to_none_and_detects_exit()
    {
        var state = CliStateParser.Parse($"exited{Us}{Us}137");

        Assert.Equal("exited", state.Status);
        Assert.Equal(HealthStatus.None, state.Health);
        Assert.Equal(137, state.ExitCode);
        Assert.True(state.HasExited);
    }

    [Theory]
    [InlineData("starting", HealthStatus.Starting)]
    [InlineData("unhealthy", HealthStatus.Unhealthy)]
    public void Parse_maps_health_states(string raw, HealthStatus expected)
    {
        var state = CliStateParser.Parse($"running{Us}{raw}{Us}0");

        Assert.Equal(expected, state.Health);
    }
}
