using System.Net;
using System.Text.Json;

namespace HamQso.Spots.EndToEnd.Tests;

public class HealthTests
{
    private static readonly string[] Expected =
        ["DXFun", "IZ8MBW-6", "IK5PWJ-6", "DA0BCC-7", "HamQTH", "DXSummit", "POTA", "SOTA", "WWFF", "broker", "alerts", "archive"];

    [Fact]
    public async Task Health_shows_every_source_and_the_broker_ok_without_error_details()
    {
        using var http = new HttpClient { BaseAddress = E2eSettings.Server };

        var body = await http.GetStringAsync("health", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
        var sources = doc.RootElement.GetProperty("sources").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal(Expected.Order(StringComparer.Ordinal), sources.Keys.Order(StringComparer.Ordinal));
        Assert.All(sources.Values, v => Assert.Equal("ok", v));
    }

    [Theory]
    [InlineData("mqtt/user")]
    [InlineData("mqtt/acl")]
    [InlineData("mqtt/superuser")]
    public async Task The_broker_auth_endpoints_are_not_reachable_from_outside(string path)
    {
        // From outside /mqtt goes to Mosquitto's WebSocket (Traefik), never to the
        // server's internal port: the auth backend must not answer 200.
        using var http = new HttpClient { BaseAddress = E2eSettings.Server };

        using var response = await http.PostAsync(path, TestUser.Json(new { username = "ingestor", password = "x", topic = "spots/x", acc = 2 }), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
