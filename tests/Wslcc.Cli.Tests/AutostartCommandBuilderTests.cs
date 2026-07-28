namespace Wslcc.Cli.Tests;

public sealed class AutostartCommandBuilderTests
{
    [Fact]
    public void BuildAddArguments_targets_the_per_user_run_key_as_a_string_value()
    {
        var args = AutostartCommandBuilder.BuildAddArguments(
            "WSLCC Daemon", @"C:\wslcc\wslccd.exe", Array.Empty<string>());

        Assert.StartsWith("add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"WSLCC Daemon\" /t REG_SZ /d ", args);
        Assert.EndsWith("/f", args);
    }

    [Fact]
    public void BuildAddArguments_wraps_executable_path_for_embedded_spaces()
    {
        var args = AutostartCommandBuilder.BuildAddArguments(
            "WSLCC Daemon", @"C:\Program Files\wslcc\wslccd.exe", Array.Empty<string>());

        Assert.Contains("/d \"\\\"C:\\Program Files\\wslcc\\wslccd.exe\\\"\" /f", args);
    }

    [Fact]
    public void BuildAddArguments_appends_executable_args_inside_the_command_value()
    {
        var args = AutostartCommandBuilder.BuildAddArguments(
            "WSLCC Daemon", @"C:\wslcc\wslccd.exe", new[] { "--Wslcc:DefaultProvider=docker" });

        Assert.Contains("/d \"\\\"C:\\wslcc\\wslccd.exe\\\" --Wslcc:DefaultProvider=docker\" /f", args);
    }

    [Fact]
    public void BuildDeleteArguments_targets_the_named_value_and_forces()
    {
        Assert.Equal(
            "delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"WSLCC Daemon\" /f",
            AutostartCommandBuilder.BuildDeleteArguments("WSLCC Daemon"));
    }
}
