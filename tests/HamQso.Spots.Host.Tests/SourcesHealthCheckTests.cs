using HamQso.Spots.Application.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Host.Tests;

/// <summary>What Uptime Kuma reads: Healthy, Degraded or Unhealthy, and the state of each entry.</summary>
public class SourcesHealthCheckTests
{
    private static readonly string[] Entries = ["DXFun", "POTA", "broker", "alerts", "archive"];

    private static Task<HealthCheckResult> Check(Action<SourceHealthRegistry> report)
    {
        var registry = new SourceHealthRegistry(new FakeTimeProvider(), NullLogger<SourceHealthRegistry>.Instance);
        report(registry);
        return new SourcesHealthCheck(registry).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Everything_ok_is_Healthy()
    {
        var result = await Check(r =>
        {
            foreach (var name in Entries) r.Succeeded(name);
        });

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.All(result.Data.Values, v => Assert.Equal("ok", v));
    }

    [Theory]
    [InlineData("broker")]
    [InlineData("alerts")]
    [InlineData("archive")]
    [InlineData("DXFun")]
    public async Task One_entry_failing_is_Degraded_and_shows_failing(string failing)
    {
        var result = await Check(r =>
        {
            foreach (var name in Entries) r.Succeeded(name);
            r.Failed(failing, "down");
        });

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("failing", result.Data[failing]);
    }

    [Fact]
    public async Task Everything_failing_is_Unhealthy()
    {
        var result = await Check(r =>
        {
            foreach (var name in Entries) r.Failed(name, "down");
        });

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Entries_that_have_not_reported_yet_are_starting_and_not_failing()
    {
        var result = await Check(r => r.Register("alerts"));

        Assert.Equal((HealthStatus.Healthy, "starting"), (result.Status, result.Data["alerts"]));
    }
}
