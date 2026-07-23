using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class VariableInterpolatorTests
{
    private static VariableInterpolator Make(Dictionary<string, string?> vars, List<string>? warnings = null)
        => new(name => vars.TryGetValue(name, out var v) ? v : null, warnings);

    [Fact]
    public void Substitutes_plain_and_braced_names()
    {
        var interp = Make(new() { ["TAG"] = "1.27", ["HOST"] = "db" });

        Assert.Equal("nginx:1.27", interp.Interpolate("nginx:$TAG"));
        Assert.Equal("db:5432", interp.Interpolate("${HOST}:5432"));
    }

    [Fact]
    public void Double_dollar_is_a_literal_dollar()
    {
        var interp = Make(new());
        Assert.Equal("$VAR and $", interp.Interpolate("$$VAR and $$"));
    }

    [Fact]
    public void Unset_without_default_yields_empty_and_warns()
    {
        var warnings = new List<string>();
        var interp = Make(new(), warnings);

        Assert.Equal("prefix-", interp.Interpolate("prefix-${MISSING}"));
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("${X:-fallback}", "fallback")] // unset -> default
    [InlineData("${X-fallback}", "fallback")]  // unset -> default
    [InlineData("${X:+set}", "")]              // unset -> empty for :+
    public void Default_and_alternate_forms_when_unset(string template, string expected)
    {
        var interp = Make(new());
        Assert.Equal(expected, interp.Interpolate(template));
    }

    [Fact]
    public void Colon_default_treats_empty_as_unset_but_dash_default_does_not()
    {
        var interp = Make(new() { ["X"] = string.Empty });

        Assert.Equal("fallback", interp.Interpolate("${X:-fallback}"));
        Assert.Equal(string.Empty, interp.Interpolate("${X-fallback}"));
    }

    [Fact]
    public void Alternate_replacement_when_set()
    {
        var interp = Make(new() { ["FEATURE"] = "on" });
        Assert.Equal("--enabled", interp.Interpolate("${FEATURE:+--enabled}"));
    }

    [Fact]
    public void Required_variable_unset_throws_with_message()
    {
        var interp = Make(new());
        var ex = Assert.Throws<ComposeLoadException>(() => interp.Interpolate("${TOKEN:?must be provided}"));
        Assert.Contains("TOKEN", ex.Message);
        Assert.Contains("must be provided", ex.Message);
    }

    [Fact]
    public void Default_values_are_themselves_interpolated()
    {
        var interp = Make(new() { ["BASE"] = "acme" });
        Assert.Equal("acme/app", interp.Interpolate("${IMAGE:-${BASE}/app}"));
    }
}
