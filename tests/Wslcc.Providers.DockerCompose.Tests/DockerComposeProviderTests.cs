using Wslcc.Providers.DockerCompose;

namespace Wslcc.Providers.DockerCompose.Tests;

public sealed class DockerComposeProviderTests
{
    [Fact]
    public void Name_is_docker()
    {
        var provider = new DockerComposeProvider();
        Assert.Equal("docker", provider.Name);
    }

    [Fact]
    public async Task GetProviderInfo_never_throws_and_reports_docker()
    {
        var provider = new DockerComposeProvider();

        // Works whether or not docker is installed on the machine running the tests.
        var info = await provider.GetProviderInfoAsync();

        Assert.Equal("docker", info.Name);
        Assert.Equal("Docker Compose", info.DisplayName);
        if (!info.IsAvailable)
        {
            Assert.False(string.IsNullOrEmpty(info.Details));
        }
    }
}
