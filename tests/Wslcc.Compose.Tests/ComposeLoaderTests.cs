using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ComposeLoaderTests : IDisposable
{
    private readonly string _dir;

    public ComposeLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wslcc-compose-tests", Guid.NewGuid().ToString("N"));
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

    private ComposeLoadResult Load(
        IReadOnlyList<string> files,
        IReadOnlyList<string>? profiles = null,
        Dictionary<string, string>? env = null,
        string? envFile = null,
        IReadOnlyList<string>? targetedServices = null,
        string? projectDirectory = null,
        bool interpolate = true)
        => ComposeLoader.Load(new ComposeLoadOptions
        {
            Files = files,
            Profiles = profiles ?? Array.Empty<string>(),
            TargetedServices = targetedServices ?? Array.Empty<string>(),
            WorkingDirectory = _dir,
            ProjectDirectory = projectDirectory,
            EnvFilePath = envFile,
            ProcessEnvironment = env ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Interpolate = interpolate,
        });

    private static IReadOnlyCollection<string> ServiceNames(string yaml)
        => YamlGraph.AsMap(YamlGraph.AsMap(YamlGraph.Deserialize(yaml))!["services"])!.Keys;

    private static Dictionary<string, object?> Service(string yaml, string name)
    {
        var root = YamlGraph.AsMap(YamlGraph.Deserialize(yaml))!;
        var services = YamlGraph.AsMap(root["services"])!;
        return YamlGraph.AsMap(services[name])!;
    }

    [Fact]
    public void Interpolates_from_env_file()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx:${TAG}
            """);
        Write(".env", "TAG=1.27");

        var result = Load(new[] { file });

        Assert.Equal("nginx:1.27", Service(result.ResolvedYaml, "web")["image"]);
    }

    [Fact]
    public void Process_environment_overrides_env_file()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx:${TAG}
            """);
        Write(".env", "TAG=1.27");

        var result = Load(new[] { file }, env: new() { ["TAG"] = "2.0" });

        Assert.Equal("nginx:2.0", Service(result.ResolvedYaml, "web")["image"]);
    }

    [Fact]
    public void Merges_multiple_files_with_later_overriding()
    {
        var basePath = Write("compose.yaml", """
            services:
              web:
                image: nginx:1
                environment:
                  A: "1"
            """);
        var overridePath = Write("compose.override.yaml", """
            services:
              web:
                image: nginx:2
                environment:
                  B: "2"
            """);

        var result = Load(new[] { basePath, overridePath });
        var web = Service(result.ResolvedYaml, "web");
        var env = YamlGraph.AsMap(web["environment"])!;

        Assert.Equal("nginx:2", web["image"]);
        Assert.Equal("1", env["A"]);
        Assert.Equal("2", env["B"]);
    }

    [Fact]
    public void Resolves_extends_within_the_same_file()
    {
        var file = Write("compose.yaml", """
            services:
              base:
                image: busybox
                environment:
                  A: "1"
              app:
                extends:
                  service: base
                environment:
                  B: "2"
            """);

        var result = Load(new[] { file });
        var app = Service(result.ResolvedYaml, "app");
        var env = YamlGraph.AsMap(app["environment"])!;

        Assert.Equal("busybox", app["image"]);
        Assert.Equal("1", env["A"]);
        Assert.Equal("2", env["B"]);
        Assert.False(app.ContainsKey("extends"));
    }

    [Fact]
    public void Resolves_extends_across_files()
    {
        Write("common.yaml", """
            services:
              base:
                image: alpine:3.20
            """);
        var file = Write("app/compose.yaml", """
            services:
              app:
                extends:
                  file: ../common.yaml
                  service: base
            """);

        var result = Load(new[] { file });

        Assert.Equal("alpine:3.20", Service(result.ResolvedYaml, "app")["image"]);
    }

    [Fact]
    public void Extends_cycle_is_rejected()
    {
        var file = Write("compose.yaml", """
            services:
              a:
                extends:
                  service: b
              b:
                extends:
                  service: a
            """);

        Assert.Throws<ComposeLoadException>(() => Load(new[] { file }));
    }

    [Fact]
    public void Filters_services_by_profile()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx
              debugger:
                image: busybox
                profiles:
                  - debug
            """);

        var withoutProfile = Load(new[] { file });
        Assert.DoesNotContain("debugger", YamlGraph.AsMap(YamlGraph.AsMap(YamlGraph.Deserialize(withoutProfile.ResolvedYaml))!["services"])!.Keys);

        var withProfile = Load(new[] { file }, profiles: new[] { "debug" });
        Assert.Contains("debugger", YamlGraph.AsMap(YamlGraph.AsMap(YamlGraph.Deserialize(withProfile.ResolvedYaml))!["services"])!.Keys);
    }

    [Fact]
    public void Compose_profiles_env_activates_profile()
    {
        var file = Write("compose.yaml", """
            services:
              debugger:
                image: busybox
                profiles:
                  - debug
            """);

        var result = Load(new[] { file }, env: new() { ["COMPOSE_PROFILES"] = "debug" });

        Assert.Contains("debugger", YamlGraph.AsMap(YamlGraph.AsMap(YamlGraph.Deserialize(result.ResolvedYaml))!["services"])!.Keys);
    }

    [Fact]
    public void Missing_required_variable_throws()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx:${TAG:?tag is required}
            """);

        var ex = Assert.Throws<ComposeLoadException>(() => Load(new[] { file }));
        Assert.Contains("TAG", ex.Message);
    }

    [Fact]
    public void Unset_variable_produces_warning()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx:${TAG}
            """);

        var result = Load(new[] { file });

        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Merges_list_form_environment_across_files_by_key()
    {
        var basePath = Write("compose.yaml", """
            services:
              web:
                image: nginx
                environment:
                  - A=1
                  - B=1
            """);
        var overridePath = Write("compose.override.yaml", """
            services:
              web:
                environment:
                  - B=2
                  - C=3
            """);

        var result = Load(new[] { basePath, overridePath });
        var env = YamlGraph.AsMap(Service(result.ResolvedYaml, "web")["environment"])!;

        Assert.Equal("1", env["A"]);
        Assert.Equal("2", env["B"]);
        Assert.Equal("3", env["C"]);
    }

    [Fact]
    public void Rejects_extending_a_service_that_declares_depends_on()
    {
        var file = Write("compose.yaml", """
            services:
              base:
                image: busybox
                depends_on:
                  - other
              other:
                image: alpine
              app:
                extends:
                  service: base
            """);

        var ex = Assert.Throws<ComposeLoadException>(() => Load(new[] { file }));
        Assert.Contains("depends_on", ex.Message);
    }

    [Fact]
    public void Targeting_a_service_activates_its_profile()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx
              debugger:
                image: busybox
                profiles:
                  - debug
            """);

        Assert.DoesNotContain("debugger", ServiceNames(Load(new[] { file }).ResolvedYaml));

        var targeted = Load(new[] { file }, targetedServices: new[] { "debugger" });
        Assert.Contains("debugger", ServiceNames(targeted.ResolvedYaml));
    }

    [Fact]
    public void Declared_profiles_are_collected_before_filtering_sorted_and_deduplicated()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx
              api:
                image: myapi
                profiles:
                  - backend
              debug:
                image: busybox
                profiles:
                  - debug
                  - backend
            """);

        // No profile active, so api/debug are filtered out of the resolved doc...
        var result = Load(new[] { file });
        Assert.DoesNotContain("api", ServiceNames(result.ResolvedYaml));

        // ...but every declared profile is still reported.
        Assert.Equal(new[] { "backend", "debug" }, result.DeclaredProfiles);
    }

    [Fact]
    public void No_interpolate_leaves_variables_verbatim()
    {
        var file = Write("compose.yaml", """
            services:
              web:
                image: nginx:${TAG:-stable}
            """);
        Write(".env", "TAG=1.27");

        var result = Load(new[] { file }, interpolate: false);

        Assert.Equal("nginx:${TAG:-stable}", Service(result.ResolvedYaml, "web")["image"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Project_directory_sets_env_location_and_default_name()
    {
        var file = Write("proj/compose.yaml", """
            services:
              web:
                image: nginx:${TAG}
            """);
        Write("proj/.env", "TAG=9.9");
        var projectDirectory = Path.Combine(_dir, "proj");

        var result = Load(new[] { file }, projectDirectory: projectDirectory);

        Assert.Equal("nginx:9.9", Service(result.ResolvedYaml, "web")["image"]);
        Assert.Equal("proj", new DirectoryInfo(result.ProjectDirectory).Name);
    }
}
