using System.Net;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Alerts;

/// <summary>/health must show when alert_spots refuses the server (a rotated key, the function down).</summary>
public class AlertsHealthTests
{
    private static Spot Dx() => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = "k",
        Callsign = "JA1ABC",
        FrequencyKhz = 14025,
        SpotTime = DateTimeOffset.UtcNow,
        Feeds = ["DXFun"],
    });

    private static (SupabaseAlertNotifier Notifier, RecordingHealth Health, HttpClient Http) Notifier(HttpStatusCode answer)
    {
        var http = new HttpClient(new FixedHandler(answer));
        var health = new RecordingHealth();
        var options = Options.Create(new SupabaseOptions { Url = new Uri("https://example.supabase.co"), SecretKey = "sb_secret_test" });
        return (new SupabaseAlertNotifier(options, http, health), health, http);
    }

    [Fact]
    public async Task When_alert_spots_refuses_the_key_health_shows_alerts_failing()
    {
        var (notifier, health, http) = Notifier(HttpStatusCode.Unauthorized);
        using var _ = http;

        await Assert.ThrowsAnyAsync<Exception>(() => notifier.NotifyAsync([Dx()], TestContext.Current.CancellationToken));

        Assert.Equal(["alerts"], health.Failures);
        Assert.False(health.LastOf("alerts"));
    }

    [Fact]
    public async Task When_alert_spots_answers_health_shows_alerts_ok()
    {
        var (notifier, health, http) = Notifier(HttpStatusCode.OK);
        using var _ = http;

        await notifier.NotifyAsync([Dx()], TestContext.Current.CancellationToken);

        Assert.True(health.LastOf("alerts"));
        Assert.Empty(health.Failures);
    }

    private sealed class FixedHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"success\":true}") });
    }
}
