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

    [Fact]
    public void Expands_references_to_earlier_variables_in_the_same_file()
    {
        const string text = """
            BASE=/data
            LOGS=${BASE}/logs
            ALT=$BASE/alt
            """;

        var env = EnvFile.Parse(text);

        Assert.Equal("/data/logs", env["LOGS"]);
        Assert.Equal("/data/alt", env["ALT"]);
    }

    [Fact]
    public void Expands_references_to_the_external_lookup_with_defaults()
    {
        const string text = """
            HOST=${DB_HOST:-localhost}
            PORT=${DB_PORT}
            """;

        var env = EnvFile.Parse(text, name => name == "DB_PORT" ? "5432" : null);

        Assert.Equal("localhost", env["HOST"]);
        Assert.Equal("5432", env["PORT"]);
    }

    [Fact]
    public void In_file_definitions_win_over_the_external_lookup_during_expansion()
    {
        const string text = """
            TAG=1.0
            IMAGE=nginx:${TAG}
            """;

        var env = EnvFile.Parse(text, name => name == "TAG" ? "2.0" : null);

        Assert.Equal("nginx:1.0", env["IMAGE"]);
    }

    [Fact]
    public void Single_quotes_are_literal_no_expansion_or_escapes()
    {
        const string text = """
            RAW='$NOPE and \n stays'
            """;

        var env = EnvFile.Parse(text, _ => "should-not-be-used");

        Assert.Equal(@"$NOPE and \n stays", env["RAW"]);
    }

    [Fact]
    public void Double_quotes_apply_escapes_and_interpolation()
    {
        const string text = "MSG=\"line1\\nline2 ${WHO}\"";

        var env = EnvFile.Parse(text, name => name == "WHO" ? "world" : null);

        Assert.Equal("line1\nline2 world", env["MSG"]);
    }

    [Fact]
    public void Dollar_dollar_is_a_literal_dollar()
    {
        var env = EnvFile.Parse("PRICE=$$5");
        Assert.Equal("$5", env["PRICE"]);
    }

    [Fact]
    public void Multiline_double_quoted_value_spans_lines()
    {
        const string text = "KEY=\"first\nsecond\nthird\"\nNEXT=after";

        var env = EnvFile.Parse(text);

        Assert.Equal("first\nsecond\nthird", env["KEY"]);
        Assert.Equal("after", env["NEXT"]);
    }

    [Fact]
    public void Multiline_single_quoted_value_spans_lines_literally()
    {
        const string text = "KEY='a\nb'\nNEXT=after";

        var env = EnvFile.Parse(text);

        Assert.Equal("a\nb", env["KEY"]);
        Assert.Equal("after", env["NEXT"]);
    }

    [Fact]
    public void Strips_inline_comment_after_whitespace_for_unquoted_values()
    {
        var env = EnvFile.Parse("TAG=1.27 # the pinned version");
        Assert.Equal("1.27", env["TAG"]);
    }

    [Fact]
    public void Hash_without_preceding_whitespace_is_part_of_an_unquoted_value()
    {
        var env = EnvFile.Parse("FRAG=page#section");
        Assert.Equal("page#section", env["FRAG"]);
    }

    [Fact]
    public void Hash_is_literal_inside_a_quoted_value()
    {
        var env = EnvFile.Parse("C=\"a # b\"");
        Assert.Equal("a # b", env["C"]);
    }

    [Fact]
    public void Unterminated_quote_is_reported()
    {
        Assert.Throws<ComposeLoadException>(() => EnvFile.Parse("KEY=\"no closing quote"));
    }
}
