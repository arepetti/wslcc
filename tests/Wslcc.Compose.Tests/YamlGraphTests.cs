using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class YamlGraphTests
{
    [Fact]
    public void DeepMerge_merges_mappings_recursively()
    {
        var baseNode = YamlGraph.Deserialize("""
            a: 1
            nested:
              x: 1
              y: 1
            """);
        var overrideNode = YamlGraph.Deserialize("""
            b: 2
            nested:
              y: 2
              z: 3
            """);

        var merged = YamlGraph.AsMap(YamlGraph.DeepMerge(baseNode, overrideNode))!;
        var nested = YamlGraph.AsMap(merged["nested"])!;

        Assert.Equal("1", merged["a"]);
        Assert.Equal("2", merged["b"]);
        Assert.Equal("1", nested["x"]);
        Assert.Equal("2", nested["y"]);
        Assert.Equal("3", nested["z"]);
    }

    [Fact]
    public void DeepMerge_replaces_sequences()
    {
        var baseNode = YamlGraph.Deserialize("ports:\n  - \"8080:80\"\n  - \"9090:90\"");
        var overrideNode = YamlGraph.Deserialize("ports:\n  - \"1234:12\"");

        var merged = YamlGraph.AsMap(YamlGraph.DeepMerge(baseNode, overrideNode))!;
        var ports = YamlGraph.AsList(merged["ports"])!;

        Assert.Single(ports);
        Assert.Equal("1234:12", ports[0]);
    }

    [Fact]
    public void Null_override_keeps_base()
    {
        var baseNode = YamlGraph.Deserialize("a: 1");
        Assert.Same(baseNode, YamlGraph.DeepMerge(baseNode, null));
    }
}
