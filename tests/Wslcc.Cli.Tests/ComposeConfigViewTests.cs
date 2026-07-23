namespace Wslcc.Cli.Tests;

public sealed class ComposeConfigViewTests
{
    private const string Yaml = """
        name: demo
        services:
          web:
            image: nginx:1.27
          api:
            image: myapi:1.0
          worker:
            image: myapi:1.0
          builder:
            build: .
        volumes:
          data: {}
          cache: {}
        """;

    [Fact]
    public void ServiceNames_are_sorted()
    {
        Assert.Equal(new[] { "api", "builder", "web", "worker" }, ComposeConfigView.ServiceNames(Yaml));
    }

    [Fact]
    public void VolumeNames_are_sorted()
    {
        Assert.Equal(new[] { "cache", "data" }, ComposeConfigView.VolumeNames(Yaml));
    }

    [Fact]
    public void ImageNames_are_distinct_sorted_and_skip_services_without_an_image()
    {
        // myapi:1.0 appears twice (api + worker) -> once; the build-only 'builder' has no image -> omitted.
        Assert.Equal(new[] { "myapi:1.0", "nginx:1.27" }, ComposeConfigView.ImageNames(Yaml));
    }

    [Fact]
    public void Empty_document_yields_no_names()
    {
        Assert.Empty(ComposeConfigView.ServiceNames("{}"));
        Assert.Empty(ComposeConfigView.VolumeNames("{}"));
        Assert.Empty(ComposeConfigView.ImageNames("{}"));
    }

    [Fact]
    public void ResolveProjectName_prefers_explicit_then_file_then_default()
    {
        Assert.Equal("explicit", ComposeConfigView.ResolveProjectName(Yaml, "explicit", "dir"));
        Assert.Equal("demo", ComposeConfigView.ResolveProjectName(Yaml, null, "dir"));
        Assert.Equal("dir", ComposeConfigView.ResolveProjectName("services: {}", null, "dir"));
    }

    [Fact]
    public void Render_injects_name_as_the_first_key()
    {
        var yaml = ComposeConfigView.Render("services:\n  web:\n    image: nginx", "myproj", asJson: false);

        Assert.StartsWith("name: myproj", yaml);
        Assert.Contains("services:", yaml);
    }

    [Fact]
    public void Render_replaces_an_existing_name_with_the_resolved_one()
    {
        var yaml = ComposeConfigView.Render("name: original\nservices:\n  web:\n    image: nginx", "override", asJson: false);

        Assert.StartsWith("name: override", yaml);
        Assert.DoesNotContain("original", yaml);
    }

    [Fact]
    public void Render_can_emit_json()
    {
        var json = ComposeConfigView.Render("services:\n  web:\n    image: nginx", "myproj", asJson: true)
            .Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);

        Assert.Equal("{\"name\":\"myproj\",\"services\":{\"web\":{\"image\":\"nginx\"}}}", json);
    }
}
