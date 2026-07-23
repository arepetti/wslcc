using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ComposeMergeTests
{
    private static Dictionary<string, object?> MergeWeb(string baseYaml, string overrideYaml)
    {
        var merged = ComposeMerge.Merge(YamlGraph.Deserialize(baseYaml), YamlGraph.Deserialize(overrideYaml));
        var services = YamlGraph.AsMap(YamlGraph.AsMap(merged)!["services"])!;
        return YamlGraph.AsMap(services["web"])!;
    }

    [Fact]
    public void Environment_list_form_merges_by_key()
    {
        var web = MergeWeb(
            "services:\n  web:\n    image: nginx\n    environment:\n      - A=1\n      - B=1",
            "services:\n  web:\n    environment:\n      - B=2\n      - C=3");

        var env = YamlGraph.AsMap(web["environment"])!;
        Assert.Equal("1", env["A"]);
        Assert.Equal("2", env["B"]);
        Assert.Equal("3", env["C"]);
    }

    [Fact]
    public void Labels_map_form_merges_by_key()
    {
        var web = MergeWeb(
            "services:\n  web:\n    labels:\n      a: \"1\"\n      b: \"1\"",
            "services:\n  web:\n    labels:\n      b: \"2\"");

        var labels = YamlGraph.AsMap(web["labels"])!;
        Assert.Equal("1", labels["a"]);
        Assert.Equal("2", labels["b"]);
    }

    [Fact]
    public void Ports_are_appended_and_exact_duplicates_dropped()
    {
        var web = MergeWeb(
            "services:\n  web:\n    ports:\n      - \"8080:80\"",
            "services:\n  web:\n    ports:\n      - \"9090:90\"\n      - \"8080:80\"");

        var ports = YamlGraph.AsList(web["ports"])!;
        Assert.Equal(new[] { "8080:80", "9090:90" }, ports.Select(p => p as string));
    }

    [Fact]
    public void Command_is_replaced_not_appended()
    {
        var web = MergeWeb(
            "services:\n  web:\n    command:\n      - echo\n      - base",
            "services:\n  web:\n    command:\n      - echo\n      - override");

        var command = YamlGraph.AsList(web["command"])!;
        Assert.Equal(new[] { "echo", "override" }, command.Select(c => c as string));
    }

    [Fact]
    public void Depends_on_list_form_is_appended()
    {
        var web = MergeWeb(
            "services:\n  web:\n    depends_on:\n      - a",
            "services:\n  web:\n    depends_on:\n      - b");

        var dependsOn = YamlGraph.AsList(web["depends_on"])!;
        Assert.Equal(new[] { "a", "b" }, dependsOn.Select(d => d as string));
    }

    [Fact]
    public void New_services_from_override_are_added()
    {
        var merged = ComposeMerge.Merge(
            YamlGraph.Deserialize("services:\n  web:\n    image: nginx"),
            YamlGraph.Deserialize("services:\n  db:\n    image: postgres"));
        var services = YamlGraph.AsMap(YamlGraph.AsMap(merged)!["services"])!;

        Assert.True(services.ContainsKey("web"));
        Assert.True(services.ContainsKey("db"));
    }
}
