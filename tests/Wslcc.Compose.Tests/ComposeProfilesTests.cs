using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ComposeProfilesTests
{
    private const string Yaml = """
        services:
          web:
            image: nginx
            depends_on:
              - debugger
          debugger:
            image: busybox
            profiles:
              - debug
        """;

    private static Dictionary<string, object?> ApplyAndGetServices(params string[] active)
    {
        var graph = YamlGraph.Deserialize(Yaml);
        var result = ComposeProfiles.Apply(graph, new HashSet<string>(active, StringComparer.Ordinal));
        return YamlGraph.AsMap(YamlGraph.AsMap(result)!["services"])!;
    }

    [Fact]
    public void Removes_disabled_service_and_prunes_depends_on()
    {
        var services = ApplyAndGetServices();

        Assert.False(services.ContainsKey("debugger"));
        Assert.True(services.ContainsKey("web"));

        var web = YamlGraph.AsMap(services["web"])!;
        Assert.False(web.ContainsKey("depends_on")); // sole dependency was pruned away
    }

    [Fact]
    public void Keeps_service_when_its_profile_is_active_and_strips_profiles_key()
    {
        var services = ApplyAndGetServices("debug");

        Assert.True(services.ContainsKey("debugger"));
        var debugger = YamlGraph.AsMap(services["debugger"])!;
        Assert.False(debugger.ContainsKey("profiles"));

        var web = YamlGraph.AsMap(services["web"])!;
        var dependsOn = YamlGraph.AsList(web["depends_on"])!;
        Assert.Contains("debugger", dependsOn);
    }
}
