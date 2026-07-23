namespace Wslcc.Cli.Tests;

public sealed class ServiceControlCommandBuilderTests
{
    [Fact]
    public void BuildCreateArguments_quotes_service_name_and_display_name()
    {
        var args = ServiceControlCommandBuilder.BuildCreateArguments(
            "WSLCC Daemon", @"C:\wslcc\wslccd.exe", Array.Empty<string>(), "auto", "WSLCC Daemon");

        Assert.StartsWith("create \"WSLCC Daemon\" binPath=", args);
        Assert.Contains("start= auto", args);
        Assert.Contains("DisplayName= \"WSLCC Daemon\"", args);
    }

    [Fact]
    public void BuildCreateArguments_wraps_executable_path_for_embedded_spaces()
    {
        var args = ServiceControlCommandBuilder.BuildCreateArguments(
            "WSLCC Daemon", @"C:\Program Files\wslcc\wslccd.exe", Array.Empty<string>(), "demand", "WSLCC Daemon");

        Assert.Contains("binPath= \"\\\"C:\\Program Files\\wslcc\\wslccd.exe\\\"\"", args);
    }

    [Fact]
    public void BuildCreateArguments_appends_executable_args_inside_binpath_value()
    {
        var args = ServiceControlCommandBuilder.BuildCreateArguments(
            "WSLCC Daemon", @"C:\wslcc\wslccd.exe", new[] { "--Wslcc:DefaultProvider=docker" }, "auto", "WSLCC Daemon");

        Assert.Contains("binPath= \"\\\"C:\\wslcc\\wslccd.exe\\\" --Wslcc:DefaultProvider=docker\"", args);
    }

    [Fact]
    public void BuildDeleteArguments_quotes_service_name()
    {
        Assert.Equal("delete \"WSLCC Daemon\"", ServiceControlCommandBuilder.BuildDeleteArguments("WSLCC Daemon"));
    }

    [Fact]
    public void BuildStopArguments_quotes_service_name()
    {
        Assert.Equal("stop \"WSLCC Daemon\"", ServiceControlCommandBuilder.BuildStopArguments("WSLCC Daemon"));
    }

    [Fact]
    public void BuildDescriptionArguments_quotes_service_name_and_description()
    {
        Assert.Equal(
            "description \"WSLCC Daemon\" \"WSLCC container orchestration daemon.\"",
            ServiceControlCommandBuilder.BuildDescriptionArguments("WSLCC Daemon", "WSLCC container orchestration daemon."));
    }
}
