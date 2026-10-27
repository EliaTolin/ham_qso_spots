using System.Globalization;
using System.Net;
using System.Text.Json;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>
/// The deployed alert_spots, called as the server calls it (SupabaseAlertNotifier).
/// The spots are made up so that only the test alert can match them: callsign
/// with a Q prefix (no DXCC entity), 20m (the open alerts of real users are on
/// 40m, 60m and 6m only, checked on 27/10/2026), no reference. Everything they
/// leave is removed at the end, statistics included.
/// </summary>
public sealed class AlertSpotsTests : IAsyncLifetime
{
    private const string AlertCall = "Q0E2E";
    private const string OtherCall = "Q0E2F";

    private readonly string runKey = $"e2e-{Guid.NewGuid():N}";
    private readonly DateTimeOffset spotTime = DateTimeOffset.UtcNow;
    private TestUser user = null!;

    public async ValueTask InitializeAsync() => user = await TestUser.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await RemoveTracesAsync();
        await user.DisposeAsync();
    }

    private Spot Dx(string call, string suffix) => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = $"{runKey}-{suffix}",
        Callsign = call,
        FrequencyKhz = 14025.0,
        SpotTime = spotTime,
        Feeds = ["E2E"],
        Comment = "CW e2e test",
    });

    [Fact]
    public async Task A_server_spot_on_the_alerted_callsign_becomes_an_alert_event_and_no_other_spot_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var alertId = await user.AddAlertAsync("E2E", AlertCall, ct);
        using var http = new HttpClient();
        var notifier = new SupabaseAlertNotifier(Options.Create(new SupabaseOptions { Url = E2eSettings.Supabase, SecretKey = E2eSettings.SecretKey }), http);

        await notifier.NotifyAsync([Dx(AlertCall, "hit"), Dx(OtherCall, "miss")], ct);

        var events = await user.AlertEventsAsync(ct);
        var e = Assert.Single(events);
        Assert.Equal(alertId, e.GetProperty("alert_id").GetString());
        Assert.Equal(("DX", AlertCall, "20m"), (e.GetProperty("source").GetString(), e.GetProperty("callsign").GetString(), e.GetProperty("band").GetString()));
        // The test user has no device: nothing is pushed.
        Assert.Equal(JsonValueKind.Null, e.GetProperty("push_sent_at").ValueKind);
    }

    [Fact]
    public async Task The_same_spot_sent_twice_alerts_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await user.AddAlertAsync("E2E", AlertCall, ct);
        using var http = new HttpClient();
        var notifier = new SupabaseAlertNotifier(Options.Create(new SupabaseOptions { Url = E2eSettings.Supabase, SecretKey = E2eSettings.SecretKey }), http);

        await notifier.NotifyAsync([Dx(AlertCall, "twice")], ct);
        await notifier.NotifyAsync([Dx(AlertCall, "twice")], ct);

        Assert.Single(await user.AlertEventsAsync(ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("publishable")]
    [InlineData("sb_secret_not_a_real_key_0000000000000")]
    public async Task Without_the_secret_key_alert_spots_refuses(string? key)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(E2eSettings.Supabase, "functions/v1/alert_spots"))
        {
            Content = TestUser.Json(new { spots = Array.Empty<object>() }),
        };
        if (key is not null) request.Headers.Add("apikey", key == "publishable" ? E2eSettings.PublishableKey : key);

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>processed_spots rows of this run and the +1 they added to the anonymous statistics.</summary>
    private async Task RemoveTracesAsync()
    {
        using var admin = TestUser.Admin();
        using var counted = await admin.GetAsync($"rest/v1/processed_spots?source=eq.DX&spot_key=like.{runKey}*&counted_at=not.is.null&select=spot_key");
        using var rows = JsonDocument.Parse(await counted.Content.ReadAsStringAsync());
        var n = rows.RootElement.GetArrayLength();
        if (n > 0)
        {
            var utc = spotTime.UtcDateTime;
            var hour = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var month = new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await DecrementAsync(admin, $"spot_stats_hourly?hour=eq.{Uri.EscapeDataString(hour)}&source=eq.dx&dxcc=eq.0&band=eq.20m&mode_category=eq.cw&spotter_continent=eq.XX", n);
            await DecrementAsync(admin, $"spot_stats_monthly?month=eq.{month}&source=eq.dx&dxcc=eq.0&band=eq.20m&mode_category=eq.cw", n);
        }
        using var deleted = await admin.DeleteAsync($"rest/v1/processed_spots?source=eq.DX&spot_key=like.{runKey}*");
        deleted.EnsureSuccessStatusCode();
    }

    private static async Task DecrementAsync(HttpClient admin, string filter, int by)
    {
        using var rows = JsonDocument.Parse(await admin.GetStringAsync($"rest/v1/{filter}&select=n"));
        if (rows.RootElement.GetArrayLength() == 0) return;
        var left = rows.RootElement[0].GetProperty("n").GetInt32() - by;
        using var response = left > 0
            ? await admin.PatchAsync($"rest/v1/{filter}", TestUser.Json(new { n = left }))
            : await admin.DeleteAsync($"rest/v1/{filter}");
        response.EnsureSuccessStatusCode();
    }
}
