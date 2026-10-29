using System.Net;
using System.Net.Http.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sending;
using Microsoft.Extensions.DependencyInjection;

namespace HamQso.Spots.Host.Tests;

/// <summary>What the server does without the Supabase secret key and in Development.</summary>
public class FeatureSwitchesTests
{
    private static DxSendOptions Node(string host, bool enabled = true) => new() { Host = host, Enabled = enabled };

    [Theory]
    [InlineData(true, "dxfun.com", true, false, true)]
    [InlineData(false, "dxfun.com", true, false, false)]
    [InlineData(true, "dxfun.com", false, false, false)]
    [InlineData(true, "dxfun.com", true, true, false)]
    [InlineData(true, "127.0.0.1", true, true, true)]
    [InlineData(true, "localhost", true, true, true)]
    [InlineData(true, "::1", true, true, true)]
    [InlineData(true, "127.0.0.1.nip.io", true, true, false)]
    public void Sending_needs_the_key_must_be_enabled_and_in_Development_stays_on_this_machine(bool key, string host, bool enabled, bool development, bool on) =>
        Assert.Equal(on, FeatureSwitches.Sending(key, Node(host, enabled), development).Enabled);

    [Fact]
    public void The_reason_says_what_is_missing() =>
        Assert.Contains("Supabase:SecretKey", FeatureSwitches.Sending(false, Node("dxfun.com"), false).OffReason, StringComparison.Ordinal);

    [Fact]
    public async Task Without_the_secret_key_alerts_and_sending_are_off_and_the_log_says_so()
    {
        using var host = new TestHost();
        host.Settings["Supabase:SecretKey"] = "";

        using var response = await host.Client().PostAsJsonAsync(new Uri("/v1/dx/spots", UriKind.Relative), new { line = "DX 14025.0 K1ABC CQ", ssid = 7 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("sending_disabled", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.IsType<NullAlertNotifier>(host.Services.GetRequiredService<IAlertNotifier>());
        Assert.Contains(host.Logs.Lines, l => l.Contains("Alerts OFF", StringComparison.Ordinal));
        Assert.Contains(host.Logs.Lines, l => l.Contains("Spot sending OFF", StringComparison.Ordinal));
        Assert.Empty(host.Cluster.Sent);
    }

    [Fact]
    public async Task In_Development_a_real_cluster_is_never_used()
    {
        using var host = new TestHost();
        host.Settings["DxSend:Host"] = "dxfun.com";

        using var response = await host.Client().PostAsJsonAsync(new Uri("/v1/dx/spots", UriKind.Relative), new { line = "DX 14025.0 K1ABC CQ", ssid = 7 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Cluster.Sent);
    }

    [Fact]
    public void With_the_key_alerts_go_to_Supabase()
    {
        using var host = new TestHost();

        Assert.IsNotType<NullAlertNotifier>(host.Services.GetRequiredService<IAlertNotifier>());
    }
}
