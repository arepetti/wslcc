using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class EnvFileTests
{
    [Fact]
    public void Parses_pairs_ignores_comments_and_blanks()
    {
        const string text = """
            # a comment
            TAG=1.27

            HOST=db
            """;

        var env = EnvFile.Parse(text);

        Assert.Equal("1.27", env["TAG"]);
        Assert.Equal("db", env["HOST"]);
        Assert.Equal(2, env.Count);
    }

    [Fact]
    public void Strips_surrounding_quotes_and_export_prefix()
    {
        const string text = """
            export NAME="hello world"
            OTHER='single'
            """;

        var env = EnvFile.Parse(text);

        Assert.Equal("hello world", env["NAME"]);
        Assert.Equal("single", env["OTHER"]);
    }

    [Fact]
    public void Value_may_contain_equals_signs()
    {
        var env = EnvFile.Parse("CONN=Server=db;Port=5432");
        Assert.Equal("Server=db;Port=5432", env["CONN"]);
    }
}
