using Wslcc.Abstractions.Compose;
using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ProjectNamesTests
{
    [Fact]
    public void Explicit_name_wins_over_file_and_default()
    {
        var file = new ComposeFile { Name = "fromfile" };
        Assert.Equal("explicit", ProjectNames.Resolve("explicit", file, "fromdir"));
    }

    [Fact]
    public void File_name_wins_over_default()
    {
        var file = new ComposeFile { Name = "fromfile" };
        Assert.Equal("fromfile", ProjectNames.Resolve(null, file, "fromdir"));
    }

    [Fact]
    public void Falls_back_to_default_then_wslcc()
    {
        Assert.Equal("fromdir", ProjectNames.Resolve(null, null, "fromdir"));
        Assert.Equal("wslcc", ProjectNames.Resolve(null, null, null));
    }

    [Theory]
    [InlineData("My Project", "my_project")]
    [InlineData("app.v1", "app_v1")]
    [InlineData("  Trim-Me  ", "trim-me")]
    [InlineData("!!!", "wslcc")]
    public void Sanitizes_names(string input, string expected)
    {
        Assert.Equal(expected, ProjectNames.Sanitize(input));
    }

    [Fact]
    public void ResolveOrNull_returns_null_when_nothing_identifies_a_project()
    {
        Assert.Null(ProjectNames.ResolveOrNull(null, null, null));
        Assert.Equal("proj", ProjectNames.ResolveOrNull(null, null, "proj"));
    }
}
