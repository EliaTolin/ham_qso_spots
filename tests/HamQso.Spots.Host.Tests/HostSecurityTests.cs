using HamQso.Spots.Application.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Host.Tests;

public class HostSecurityTests
{
    [Fact]
    public void A_request_on_the_public_port_with_a_forged_Host_header_does_not_reach_the_MQTT_endpoints()
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 8080;
        context.Request.Host = new HostString("server", 8081);

        Assert.False(InternalPortOnly.Allows(context, internalPort: 8081));
    }

    [Fact]
    public void Mosquitto_on_the_internal_port_reaches_the_MQTT_endpoints()
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = 8081;

        Assert.True(InternalPortOnly.Allows(context, internalPort: 8081));
    }

    [Fact]
    public async Task The_public_health_shows_no_error_details()
    {
        var registry = new SourceHealthRegistry(new FakeTimeProvider(), NullLogger<SourceHealthRegistry>.Instance);
        registry.Failed("DXFun", "Connection refused 10.0.1.7:8000 (secret path /etc/x)");

        var result = await new SourcesHealthCheck(registry).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal("failing", result.Data["DXFun"]);
        Assert.DoesNotContain("refused", result.Description ?? "", StringComparison.Ordinal);
    }
}
