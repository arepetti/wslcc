namespace Wslcc.Cli.Tests;

public sealed class ColorInterceptorTests
{
    [Fact]
    public void No_color_flag_disables_color()
    {
        var settings = new GlobalSettings { NoColor = true };
        Assert.True(ColorInterceptor.ShouldDisableColor(settings, noColorEnvValue: null));
    }

    [Fact]
    public void No_color_env_disables_color_even_without_the_flag()
    {
        var settings = new GlobalSettings { NoColor = false };
        Assert.True(ColorInterceptor.ShouldDisableColor(settings, noColorEnvValue: "1"));
    }

    [Fact]
    public void Color_stays_enabled_without_flag_or_env()
    {
        var settings = new GlobalSettings { NoColor = false };
        Assert.False(ColorInterceptor.ShouldDisableColor(settings, noColorEnvValue: null));
        Assert.False(ColorInterceptor.ShouldDisableColor(settings, noColorEnvValue: string.Empty));
    }
}
