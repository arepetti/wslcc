using Wslcc.Providers.Common;

namespace Wslcc.Providers.DockerCompose.Tests;

public sealed class CliLogParserTests
{
    [Fact]
    public void ParseTimestamped_splits_rfc3339_prefix_from_the_message()
    {
        var line = CliLogParser.ParseTimestamped("2024-05-01T12:34:56.789012345Z hello world");

        Assert.NotNull(line.Timestamp);
        Assert.Equal(DateTimeOffset.Parse("2024-05-01T12:34:56.789012345Z"), line.Timestamp);
        Assert.Equal("hello world", line.Message);
    }

    [Fact]
    public void ParseTimestamped_keeps_the_whole_line_when_there_is_no_valid_timestamp()
    {
        var line = CliLogParser.ParseTimestamped("not a timestamp here");

        Assert.Null(line.Timestamp);
        Assert.Equal("not a timestamp here", line.Message);
    }
}
