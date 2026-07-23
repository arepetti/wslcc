using Wslcc.Compose;

namespace Wslcc.Compose.Tests;

public sealed class ComposeHashTests
{
    [Fact]
    public void Produces_a_hash_per_service()
    {
        var hashes = ComposeHash.ComputeServiceHashes("""
            services:
              web:
                image: nginx
              api:
                image: myapi
            """);

        Assert.Equal(new[] { "api", "web" }, hashes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(hashes.Values, h => Assert.Equal(64, h.Length)); // SHA-256 hex
    }

    [Fact]
    public void Hash_is_independent_of_key_order()
    {
        var a = ComposeHash.ComputeServiceHashes("""
            services:
              web:
                image: nginx
                restart: always
            """);
        var b = ComposeHash.ComputeServiceHashes("""
            services:
              web:
                restart: always
                image: nginx
            """);

        Assert.Equal(a["web"], b["web"]);
    }

    [Fact]
    public void Hash_changes_when_config_changes()
    {
        var a = ComposeHash.ComputeServiceHashes("services:\n  web:\n    image: nginx:1");
        var b = ComposeHash.ComputeServiceHashes("services:\n  web:\n    image: nginx:2");

        Assert.NotEqual(a["web"], b["web"]);
    }

    [Fact]
    public void Empty_document_yields_no_hashes()
    {
        Assert.Empty(ComposeHash.ComputeServiceHashes("{}"));
    }
}
