using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ComposeFileDiscoveryTests : IDisposable
{
    private readonly string _dir;

    public ComposeFileDiscoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wslcc-discovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "services: {}");
        return path;
    }

    private static Dictionary<string, string> Env(params (string Key, string Value)[] pairs)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            env[key] = value;
        }

        return env;
    }

    [Fact]
    public void Explicit_files_win_and_are_returned_in_order()
    {
        Touch("compose.yaml");
        var a = Touch("a.yaml");
        var b = Touch("b.yaml");

        var files = ComposeFileDiscovery.Discover(new[] { "a.yaml", "b.yaml" }, _dir, Env());

        Assert.Equal(new[] { a, b }, files);
    }

    [Fact]
    public void Missing_explicit_file_throws()
    {
        Assert.Throws<ComposeLoadException>(
            () => ComposeFileDiscovery.Discover(new[] { "nope.yaml" }, _dir, Env()));
    }

    [Fact]
    public void Falls_back_to_conventional_name()
    {
        var expected = Touch("docker-compose.yml");

        var files = ComposeFileDiscovery.Discover(Array.Empty<string>(), _dir, Env());

        Assert.Equal(new[] { expected }, files);
    }

    [Fact]
    public void Reads_compose_file_env_with_default_separator()
    {
        var a = Touch("a.yaml");
        var b = Touch("b.yaml");

        var files = ComposeFileDiscovery.Discover(
            Array.Empty<string>(), _dir, Env(("COMPOSE_FILE", $"a.yaml{Path.PathSeparator}b.yaml")));

        Assert.Equal(new[] { a, b }, files);
    }

    [Fact]
    public void Honors_custom_path_separator()
    {
        var a = Touch("a.yaml");
        var b = Touch("b.yaml");

        var files = ComposeFileDiscovery.Discover(
            Array.Empty<string>(), _dir, Env(("COMPOSE_FILE", "a.yaml|b.yaml"), ("COMPOSE_PATH_SEPARATOR", "|")));

        Assert.Equal(new[] { a, b }, files);
    }

    [Fact]
    public void Explicit_files_take_precedence_over_compose_file_env()
    {
        var a = Touch("a.yaml");
        Touch("b.yaml");

        var files = ComposeFileDiscovery.Discover(new[] { "a.yaml" }, _dir, Env(("COMPOSE_FILE", "b.yaml")));

        Assert.Equal(new[] { a }, files);
    }
}
