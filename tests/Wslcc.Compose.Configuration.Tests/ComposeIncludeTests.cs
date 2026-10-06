using Wslcc.Compose.Configuration;

namespace Wslcc.Compose.Configuration.Tests;

public sealed class ComposeIncludeTests : IDisposable
{
    private readonly string _dir;

    public ComposeIncludeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wslcc-include-tests", Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup.
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private ComposeLoadResult Load(IReadOnlyList<string> files, Dictionary<string, string>? env = null, bool interpolate = true)
        => ComposeLoader.Load(new ComposeLoadOptions
        {
            Files = files,
            WorkingDirectory = _dir,
            ProcessEnvironment = env ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Interpolate = interpolate,
        });

    private static Dictionary<string, object?> Root(string yaml)
        => YamlGraph.AsMap(YamlGraph.Deserialize(yaml))!;

    private static Dictionary<string, object?> Service(string yaml, string name)
    {
        var services = YamlGraph.AsMap(Root(yaml)["services"])!;
        return YamlGraph.AsMap(services[name])!;
    }

    [Fact]
    public void Short_form_include_imports_service_and_strips_include_key()
    {
        Write("shared/db.yaml", """
            services:
              db:
                image: postgres:16
            """);
        var parent = Write("compose.yaml", """
            include:
              - shared/db.yaml
            services:
              web:
                image: nginx
            """);

        var result = Load(new[] { parent });
        var root = Root(result.ResolvedYaml);

        Assert.False(root.ContainsKey("include"));
        Assert.Equal("nginx", Service(result.ResolvedYaml, "web")["image"]);
        Assert.Equal("postgres:16", Service(result.ResolvedYaml, "db")["image"]);
    }

    [Fact]
    public void Long_form_env_file_interpolates_included_file_not_parent_env()
    {
        Write(".env", "TAG=parent");
        Write("shared/.env", "TAG=child");
        Write("shared/db.yaml", """
            services:
              db:
                image: postgres:${TAG}
            """);
        var parent = Write("compose.yaml", """
            include:
              - path: shared/db.yaml
                env_file: shared/.env
            services:
              web:
                image: nginx:${TAG}
            """);

        var result = Load(new[] { parent });

        Assert.Equal("nginx:parent", Service(result.ResolvedYaml, "web")["image"]);
        Assert.Equal("postgres:child", Service(result.ResolvedYaml, "db")["image"]);
    }

    [Fact]
    public void Rewrites_relative_bind_source_against_included_project_directory()
    {
        Write("shared/db.yaml", """
            services:
              db:
                image: postgres:16
                volumes:
                  - ./data:/var/lib/data
            """);
        var parent = Write("compose.yaml", """
            include:
              - shared/db.yaml
            services:
              web:
                image: nginx
            """);

        var result = Load(new[] { parent });
        var volumes = YamlGraph.AsList(Service(result.ResolvedYaml, "db")["volumes"])!;
        var expected = Path.GetFullPath(Path.Combine(_dir, "shared", "data")) + ":/var/lib/data";

        Assert.Equal(expected, Convert.ToString(volumes[0]));
    }

    [Fact]
    public void Duplicate_service_name_fails()
    {
        Write("shared/db.yaml", """
            services:
              web:
                image: postgres:16
            """);
        var parent = Write("compose.yaml", """
            include:
              - shared/db.yaml
            services:
              web:
                image: nginx
            """);

        var ex = Assert.Throws<ComposeLoadException>(() => Load(new[] { parent }));

        Assert.Contains("web", ex.Message);
        Assert.Contains("services", ex.Message);
    }

    [Fact]
    public void Path_list_merges_like_dash_f()
    {
        Write("shared/a.yaml", """
            services:
              db:
                image: postgres:1
                environment:
                  A: "1"
            """);
        Write("shared/b.yaml", """
            services:
              db:
                image: postgres:2
                environment:
                  B: "2"
            """);
        var parent = Write("compose.yaml", """
            include:
              - path:
                  - shared/a.yaml
                  - shared/b.yaml
            """);

        var result = Load(new[] { parent });
        var db = Service(result.ResolvedYaml, "db");
        var env = YamlGraph.AsMap(db["environment"])!;

        Assert.Equal("postgres:2", db["image"]);
        Assert.Equal("1", env["A"]);
        Assert.Equal("2", env["B"]);
    }

    [Fact]
    public void Nested_include_is_resolved()
    {
        Write("leaf/redis.yaml", """
            services:
              redis:
                image: redis:7
            """);
        Write("mid/compose.yaml", """
            include:
              - ../leaf/redis.yaml
            services:
              db:
                image: postgres:16
            """);
        var parent = Write("compose.yaml", """
            include:
              - mid/compose.yaml
            services:
              web:
                image: nginx
            """);

        var result = Load(new[] { parent });

        Assert.Equal("nginx", Service(result.ResolvedYaml, "web")["image"]);
        Assert.Equal("postgres:16", Service(result.ResolvedYaml, "db")["image"]);
        Assert.Equal("redis:7", Service(result.ResolvedYaml, "redis")["image"]);
    }

    [Fact]
    public void Cycle_is_rejected()
    {
        Write("a.yaml", """
            include:
              - b.yaml
            services:
              a:
                image: busybox
            """);
        Write("b.yaml", """
            include:
              - a.yaml
            services:
              b:
                image: busybox
            """);
        var parent = Write("compose.yaml", """
            include:
              - a.yaml
            """);

        var ex = Assert.Throws<ComposeLoadException>(() => Load(new[] { parent }));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remote_url_is_rejected()
    {
        var parent = Write("compose.yaml", """
            include:
              - https://example.com/compose.yaml
            services:
              web:
                image: nginx
            """);

        var ex = Assert.Throws<ComposeLoadException>(() => Load(new[] { parent }));

        Assert.Contains("local file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_interpolate_still_imports_included_service()
    {
        Write("shared/db.yaml", """
            services:
              db:
                image: postgres:${TAG}
            """);
        var parent = Write("compose.yaml", """
            include:
              - shared/db.yaml
            services:
              web:
                image: nginx:${TAG}
            """);

        var result = Load(new[] { parent }, interpolate: false);

        Assert.Equal("nginx:${TAG}", Service(result.ResolvedYaml, "web")["image"]);
        Assert.Equal("postgres:${TAG}", Service(result.ResolvedYaml, "db")["image"]);
    }
}
