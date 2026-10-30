using System.Net;
using System.Text.Json;
using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace HamQso.Spots.Host.Tests;

/// <summary>/health and the broker auth endpoints of the real Host (see <see cref="TestHost"/>).</summary>
public sealed class HostEndpointsTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    private Task<HttpResponseMessage> Post(string path, object body, int port = 8081) =>
        host.Client(port, token: null).PostAsync(new Uri(path, UriKind.Relative), new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Health_lists_every_registered_source_with_its_state_only()
    {
        var registry = host.Services.GetRequiredService<SourceHealthRegistry>();
        registry.Register("DXFun");
        registry.Succeeded("HamQTH");
        registry.Failed("POTA", "secret detail");

        using var response = await host.Client(token: null).GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Degraded", doc.RootElement.GetProperty("status").GetString());
        var sources = doc.RootElement.GetProperty("sources");
        Assert.Equal(("starting", "ok", "failing"), (sources.GetProperty("DXFun").GetString(), sources.GetProperty("HamQTH").GetString(), sources.GetProperty("POTA").GetString()));
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_says_how_many_references_the_catalog_has_and_when_it_was_built()
    {
        using (var before = await host.Client(token: null).GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            using var none = JsonDocument.Parse(await before.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(JsonValueKind.Null, none.RootElement.GetProperty("catalog").ValueKind);
        }
        host.Services.GetRequiredService<Application.Catalog.CurrentReferenceCatalog>().File = Domain.References.ReferenceCatalogFormat.Write(
            [new Domain.References.CatalogReference("IT-1271", "POTA", "Park", null, null, null, null, null, null, null)],
            new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero));

        using var response = await host.Client(token: null).GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var catalog = doc.RootElement.GetProperty("catalog");
        Assert.Equal((1, "2026-10-30T03:00:00Z"), (catalog.GetProperty("references").GetInt32(), catalog.GetProperty("built").GetString()));
    }

    [Theory]
    [InlineData("ingestor", "ingestor-password", HttpStatusCode.OK)]
    [InlineData("ingestor", "wrong", HttpStatusCode.Forbidden)]
    [InlineData("app-user", TestHost.ValidToken, HttpStatusCode.OK)]
    [InlineData("app-user", "forged.token", HttpStatusCode.Forbidden)]
    public async Task Mosquitto_asks_who_may_connect(string username, string password, HttpStatusCode expected)
    {
        using var response = await Post("/mqtt/user", new { username, password, clientid = "c" });

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("app-user", "spots/dx/20m/cw", 4, HttpStatusCode.OK)]
    [InlineData("app-user", "spots/dx/20m/cw", 2, HttpStatusCode.Forbidden)]
    [InlineData("ingestor", "spots/dx/20m/cw", 2, HttpStatusCode.OK)]
    [InlineData("app-user", "spots/dx/20m/cw", 9, HttpStatusCode.Forbidden)]
    public async Task Mosquitto_asks_who_may_read_or_write_a_topic(string username, string topic, int acc, HttpStatusCode expected)
    {
        using var response = await Post("/mqtt/acl", new { username, topic, clientid = "c", acc });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_is_a_superuser()
    {
        using var response = await Post("/mqtt/superuser", new { username = "ingestor" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task From_the_public_port_the_broker_auth_endpoints_do_not_exist()
    {
        using var response = await Post("/mqtt/user", new { username = "ingestor", password = "ingestor-password" }, port: 8080);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void The_configured_sources_start_enabled_nodes_and_feeds_only()
    {
        var names = host.Services.GetRequiredService<IEnumerable<ISpotSource>>().Select(s => s.Name).ToArray();

        Assert.Equal(["DXFun", "IZ8MBW-6", "IK5PWJ-6", "DA0BCC-7", "HamQTH", "DXSummit", "POTA", "SOTA"], names);
    }
}
