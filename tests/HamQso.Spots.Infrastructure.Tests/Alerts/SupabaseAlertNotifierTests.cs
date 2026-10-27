using System.Net;
using System.Text.Json;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Alerts;

public class SupabaseAlertNotifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static Spot Dx(string comment) => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = "JA1ABC_14025.0_202610271200",
        Callsign = "JA1ABC",
        FrequencyKhz = 14025.0,
        SpotTime = Now,
        Feeds = ["DXFun"],
        Spotters = ["EA4RCH-#"],
        Comment = comment,
    });

    [Fact]
    public async Task New_spots_go_to_the_alerts_function_with_the_secret_key()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var notifier = new SupabaseAlertNotifier(
            Options.Create(new SupabaseOptions { Url = new Uri("https://example.supabase.co"), SecretKey = "sb_secret_test" }), http);

        await notifier.NotifyAsync([Dx("CW 12 dB 25 WPM CQ")], TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://example.supabase.co/functions/v1/alert_spots", request.Url);
        Assert.Equal("Bearer sb_secret_test", request.Authorization);
        Assert.Equal("sb_secret_test", request.ApiKey);
        using var body = JsonDocument.Parse(request.Body);
        var spot = Assert.Single(body.RootElement.GetProperty("spots").EnumerateArray().ToArray());
        Assert.Equal("DX", spot.GetProperty("source").GetString());
        Assert.Equal("JA1ABC", spot.GetProperty("callsign").GetString());
        Assert.Equal(14025.0, spot.GetProperty("frequency_khz").GetDouble());
        Assert.Equal("2026-10-27T12:00:00.000Z", spot.GetProperty("spot_time").GetString());
    }

    [Fact]
    public void Only_a_reported_mode_is_pushed_the_band_plan_one_only_feeds_the_filters()
    {
        var reported = AlertSpotPayload.From(Dx("CW 12 dB 25 WPM CQ"));
        var guessed = AlertSpotPayload.From(Dx("tnx QSO"));

        Assert.Equal(("CW", "CW"), (reported["mode"], reported["mode_category"]));
        Assert.Equal((null, "CW"), (guessed["mode"], guessed["mode_category"]));
    }

    [Fact]
    public void A_POTA_spot_carries_its_reference_and_location()
    {
        var payload = AlertSpotPayload.From(SpotEnrichment.Enrich(new Spot
        {
            Program = SpotProgram.Pota,
            SpotKey = "59545154",
            Callsign = "K8RLE",
            FrequencyKhz = 7074,
            SpotTime = Now,
            Feeds = ["POTA"],
            Mode = "FT8",
            Reference = "US-6701",
            Location = "US-MI",
        }));

        Assert.Equal(("POTA", "US-6701", "US-MI", "FT8", "DIGITAL"), (payload["source"], payload["ref"], payload["location"], payload["mode"], payload["mode_category"]));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Url, string? Authorization, string? ApiKey, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("apikey", out var keys) ? keys.Single() : null,
                body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }
}
