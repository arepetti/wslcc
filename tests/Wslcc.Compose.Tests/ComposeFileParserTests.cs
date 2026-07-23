using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ComposeFileParserTests
{
    private readonly ComposeFileParser _parser = new();

    [Fact]
    public void Parses_services_with_common_short_and_long_forms()
    {
        const string yaml = """
            name: sample
            services:
              web:
                image: nginx:1.27
                ports:
                  - "8080:80"
                environment:
                  - FOO=bar
                  - EMPTY
                depends_on:
                  - redis
              redis:
                image: redis:7
                build: .
            networks:
              default:
                driver: bridge
            volumes:
              data: {}
            """;

        var file = _parser.Parse(yaml);

        Assert.Equal("sample", file.Name);
        Assert.Equal(2, file.Services.Count);

        var web = file.Services["web"];
        Assert.Equal("nginx:1.27", web.Image);
        Assert.Equal("web", web.Name);
        Assert.Contains("8080:80", web.Ports);
        Assert.Equal("bar", web.Environment["FOO"]);
        Assert.Null(web.Environment["EMPTY"]);
        Assert.Contains("redis", web.DependsOn);

        var redis = file.Services["redis"];
        Assert.NotNull(redis.Build);
        Assert.Equal(".", redis.Build!.Context);

        Assert.True(file.Networks.ContainsKey("default"));
        Assert.Equal("bridge", file.Networks["default"].Driver);
        Assert.True(file.Volumes.ContainsKey("data"));
    }

    [Fact]
    public void Parses_environment_and_depends_on_map_forms()
    {
        const string yaml = """
            services:
              app:
                image: app:latest
                environment:
                  KEY: value
                depends_on:
                  db:
                    condition: service_started
            """;

        var file = _parser.Parse(yaml);
        var app = file.Services["app"];

        Assert.Equal("value", app.Environment["KEY"]);
        Assert.Contains("db", app.DependsOn);
    }

    [Fact]
    public void Empty_document_yields_empty_model()
    {
        var file = _parser.Parse(string.Empty);

        Assert.Null(file.Name);
        Assert.Empty(file.Services);
    }
}
