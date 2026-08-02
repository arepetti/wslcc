using Wslcc.Abstractions.Compose;
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

    [Fact]
    public void Parses_depends_on_conditions_and_healthcheck()
    {
        const string yaml = """
            services:
              web:
                image: nginx
                depends_on:
                  db:
                    condition: service_healthy
                  migrate:
                    condition: service_completed_successfully
                    required: false
              db:
                image: postgres
                healthcheck:
                  test: ["CMD-SHELL", "pg_isready"]
                  interval: 10s
                  timeout: 5s
                  retries: 5
                  start_period: 20s
              migrate:
                image: migrate
            """;

        var file = _parser.Parse(yaml);
        var web = file.Services["web"];

        Assert.Contains(web.DependsOn, d => d.Name == "db" && d.Condition == DependencyCondition.ServiceHealthy && d.Required);
        Assert.Contains(web.DependsOn, d => d.Name == "migrate" && d.Condition == DependencyCondition.ServiceCompletedSuccessfully && !d.Required);

        var db = file.Services["db"];
        Assert.NotNull(db.HealthCheck);
        Assert.False(db.HealthCheck!.Disabled);
        Assert.Equal(new[] { "CMD-SHELL", "pg_isready" }, db.HealthCheck.Test);
        Assert.Equal("10s", db.HealthCheck.Interval);
        Assert.Equal("5s", db.HealthCheck.Timeout);
        Assert.Equal(5, db.HealthCheck.Retries);
        Assert.Equal("20s", db.HealthCheck.StartPeriod);
    }

    [Fact]
    public void Parses_a_disabled_healthcheck()
    {
        const string yaml = """
            services:
              web:
                image: nginx
                healthcheck:
                  disable: true
            """;

        var file = _parser.Parse(yaml);

        Assert.True(file.Services["web"].HealthCheck!.Disabled);
    }

    [Fact]
    public void Rejects_long_form_ports()
    {
        const string yaml = """
            services:
              web:
                image: nginx
                ports:
                  - target: 80
                    published: 8080
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("ports", ex.Message);
        Assert.Contains("long map form", ex.Message);
        Assert.Contains("web", ex.Message);
    }

    [Fact]
    public void Rejects_long_form_volumes()
    {
        const string yaml = """
            services:
              web:
                image: nginx
                volumes:
                  - type: bind
                    source: ./data
                    target: /data
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("volumes", ex.Message);
        Assert.Contains("long map form", ex.Message);
    }
}
