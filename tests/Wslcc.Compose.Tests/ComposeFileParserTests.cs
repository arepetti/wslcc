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
    public void String_command_expands_to_shell_form()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                command: npm start
            """;

        var file = _parser.Parse(yaml);

        Assert.Equal(new[] { "/bin/sh", "-c", "npm start" }, file.Services["web"].Command);
    }

    [Fact]
    public void List_command_is_exec_form()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                command: ["npm", "start"]
            """;

        var file = _parser.Parse(yaml);

        Assert.Equal(new[] { "npm", "start" }, file.Services["web"].Command);
    }

    [Fact]
    public void String_entrypoint_expands_to_shell_form()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                entrypoint: npm start
            """;

        var file = _parser.Parse(yaml);

        Assert.Equal(new[] { "/bin/sh", "-c", "npm start" }, file.Services["web"].Entrypoint);
    }

    [Fact]
    public void Parses_env_file_short_and_long_forms()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                env_file:
                  - ./a.env
                  - path: ./b.env
                    required: false
            """;

        var file = _parser.Parse(yaml);
        var files = file.Services["web"].EnvFile;

        Assert.Equal(2, files.Count);
        Assert.Equal("./a.env", files[0].Path);
        Assert.True(files[0].Required);
        Assert.Equal("./b.env", files[1].Path);
        Assert.False(files[1].Required);
    }

    [Fact]
    public void Parses_user_workdir_hostname_readonly_labels_container_name()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                container_name: my-web
                user: "1000:1000"
                working_dir: /app
                hostname: api-node-1
                read_only: true
                labels:
                  com.example.team: platform
            """;

        var file = _parser.Parse(yaml);
        var web = file.Services["web"];

        Assert.Equal("my-web", web.ContainerName);
        Assert.Equal("1000:1000", web.User);
        Assert.Equal("/app", web.WorkingDir);
        Assert.Equal("api-node-1", web.Hostname);
        Assert.True(web.ReadOnly);
        Assert.Equal("platform", web.Labels["com.example.team"]);
    }

    [Fact]
    public void Parses_annotations_map_form_including_empty_value()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                annotations:
                  org.opencontainers.image.source: https://github.com/acme/api
                  com.example.empty: ""
            """;

        var file = _parser.Parse(yaml);
        var web = file.Services["web"];

        Assert.Equal("https://github.com/acme/api", web.Annotations["org.opencontainers.image.source"]);
        Assert.Equal("", web.Annotations["com.example.empty"]);
        Assert.Empty(web.Labels);
    }

    [Fact]
    public void Parses_annotations_list_form()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                annotations:
                  - org.opencontainers.image.source=https://github.com/acme/api
                  - com.example.note=hello world
            """;

        var file = _parser.Parse(yaml);
        var web = file.Services["web"];

        Assert.Equal("https://github.com/acme/api", web.Annotations["org.opencontainers.image.source"]);
        Assert.Equal("hello world", web.Annotations["com.example.note"]);
    }

    [Fact]
    public void Rejects_map_command()
    {
        const string yaml = """
            services:
              web:
                image: busybox
                command:
                  foo: bar
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("command", ex.Message);
        Assert.Contains("web", ex.Message);
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
    public void Parses_short_and_long_form_volumes_and_tmpfs()
    {
        const string yaml = """
            services:
              web:
                image: nginx
                read_only: true
                tmpfs:
                  - /tmp
                  - /run:size=64m,mode=0o1777
                volumes:
                  - data:/var/lib
                  - type: bind
                    source: ./config
                    target: /etc/app
                    read_only: true
                  - type: tmpfs
                    target: /cache
                    tmpfs:
                      size: 64mb
                      mode: 0o1777
                  - type: volume
                    source: data
                    target: /db
                    volume:
                      nocopy: true
                      subpath: pgdata
            volumes:
              data:
            """;

        var file = _parser.Parse(yaml);
        var mounts = file.Services["web"].Volumes;

        Assert.Equal(6, mounts.Count);

        Assert.Equal(MountType.Volume, mounts[0].Type);
        Assert.Equal("data", mounts[0].Source);
        Assert.Equal("/var/lib", mounts[0].Target);

        Assert.Equal(MountType.Bind, mounts[1].Type);
        Assert.Equal("./config", mounts[1].Source);
        Assert.Equal("/etc/app", mounts[1].Target);
        Assert.True(mounts[1].ReadOnly);

        Assert.Equal(MountType.Tmpfs, mounts[2].Type);
        Assert.Equal("/cache", mounts[2].Target);
        Assert.Equal("64mb", mounts[2].TmpfsSize);
        Assert.Equal("1777", mounts[2].TmpfsMode);

        Assert.Equal(MountType.Volume, mounts[3].Type);
        Assert.True(mounts[3].VolumeNocopy);
        Assert.Equal("pgdata", mounts[3].VolumeSubpath);

        Assert.Equal(MountType.Tmpfs, mounts[4].Type);
        Assert.Equal("/tmp", mounts[4].Target);

        Assert.Equal(MountType.Tmpfs, mounts[5].Type);
        Assert.Equal("/run", mounts[5].Target);
        Assert.Equal("64m", mounts[5].TmpfsSize);
        Assert.Equal("1777", mounts[5].TmpfsMode);
    }

    [Theory]
    [InlineData("npipe")]
    [InlineData("cluster")]
    [InlineData("image")]
    public void Rejects_unsupported_volume_types(string type)
    {
        var yaml = $"""
            services:
              web:
                image: nginx
                volumes:
                  - type: {type}
                    source: unused
                    target: /unused
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("web", ex.Message);
        Assert.Contains(type, ex.Message);
        Assert.Contains("not supported", ex.Message);
        Assert.Contains("volume, bind, tmpfs", ex.Message);
    }

    [Fact]
    public void Rejects_missing_volume_type()
    {
        const string yaml = """
            services:
              web:
                image: nginx
                volumes:
                  - source: ./data
                    target: /data
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("volume type is required", ex.Message);
    }

    [Fact]
    public void Parses_secrets_short_and_long_form()
    {
        const string yaml = """
            services:
              db:
                image: postgres
                secrets:
                  - db_password
                  - source: tls_key
                    target: /etc/ssl/private/tls.key
            secrets:
              db_password:
                file: ./secrets/db_password.txt
              tls_key: ./certs/server.key
            """;

        var file = _parser.Parse(yaml);

        Assert.Equal("./secrets/db_password.txt", file.Secrets["db_password"].File);
        Assert.Equal("./certs/server.key", file.Secrets["tls_key"].File);

        var db = file.Services["db"];
        Assert.Equal(2, db.Secrets.Count);
        Assert.Equal("db_password", db.Secrets[0].Source);
        Assert.Equal("/run/secrets/db_password", db.Secrets[0].Target);
        Assert.Equal("tls_key", db.Secrets[1].Source);
        Assert.Equal("/etc/ssl/private/tls.key", db.Secrets[1].Target);
    }

    [Fact]
    public void Parses_environment_sourced_secret()
    {
        const string yaml = """
            services:
              app:
                image: app
                secrets:
                  - api_token
            secrets:
              api_token:
                environment: API_TOKEN
            """;

        var file = _parser.Parse(yaml);

        Assert.Equal("API_TOKEN", file.Secrets["api_token"].Environment);
        Assert.Null(file.Secrets["api_token"].File);
    }

    [Fact]
    public void Rejects_unknown_secret_source()
    {
        const string yaml = """
            services:
              app:
                image: app
                secrets:
                  - missing
            secrets:
              other:
                file: ./other
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("secret 'missing' is not declared", ex.Message);
    }

    [Fact]
    public void Rejects_external_secret()
    {
        const string yaml = """
            services:
              app:
                image: app
            secrets:
              corp:
                external: true
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("external: true", ex.Message);
        Assert.Contains("corp", ex.Message);
    }

    [Fact]
    public void Rejects_external_secret_map_form()
    {
        const string yaml = """
            secrets:
              corp:
                external:
                  name: signing-key
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("external: true", ex.Message);
    }

    [Fact]
    public void Rejects_secret_without_file_or_environment()
    {
        const string yaml = """
            secrets:
              empty: {}
            """;

        var ex = Assert.Throws<ComposeLoadException>(() => _parser.Parse(yaml));

        Assert.Contains("exactly one of 'file' or 'environment'", ex.Message);
    }
}
