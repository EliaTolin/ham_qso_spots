using System.Globalization;
using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Host;

/// <summary>
/// The JSON of F71 for the clients (plan, 3.1 and 3.2): snake_case, times in UTC
/// with "Z", every field always present (null when unknown). Checked against the
/// shared fixtures rbn_skimmers_v1.json and rbn_sse_v2.txt.
/// </summary>
public static class RbnJson
{
    /// <summary>The answer of GET /v1/rbn/skimmers.</summary>
    public static string Skimmers(ListeningSkimmers r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return JsonSerializer.Serialize(new
        {
            band = r.Band,
            from = Seconds(r.From),
            to = Seconds(r.To),
            feed = Feed(r.Feed),
            nodes = new { fetched_at = r.Nodes.FetchedAt is { } at ? Seconds(at) : null, count = r.Nodes.Count, stale = r.Nodes.Stale },
            skimmers = r.Skimmers.Select(s => new
            {
                call = s.Skimmer.Call,
                locator = s.Skimmer.Position?.Locator,
                lat = s.Skimmer.Position?.Lat,
                lon = s.Skimmer.Position?.Lon,
                position_source = s.Skimmer.Position?.Source,
                dxcc = s.Skimmer.Dxcc,
                continent = s.Skimmer.Continent,
                spots = s.Spots,
                last_spot_at = Seconds(s.LastSpotAt),
            }),
        });
    }

    /// <summary>The data of an "event: spot" of the stream v2.</summary>
    public static string Spot(RbnSpotEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return JsonSerializer.Serialize(new
        {
            skimmer = e.Skimmer,
            call = e.Call,
            khz = e.Khz,
            snr_db = e.SnrDb,
            wpm = e.Wpm,
            kind = e.Kind,
            band = e.Band,
            rbn_time = e.RbnTime,
            received_at = Millis(e.ReceivedAt),
            position = Position(e.Position),
            dxcc = e.Dxcc,
            continent = e.Continent,
        });
    }

    /// <summary>The data of an "event: status" of the stream v2.</summary>
    public static string Status(RbnFeedState state) => JsonSerializer.Serialize(new { feed = State(state) });

    public static string State(RbnFeedState state) => state switch
    {
        RbnFeedState.Up => "up",
        RbnFeedState.Reconnecting => "reconnecting",
        _ => "down",
    };

    private static object Feed(RbnFeedStatus status) => new { state = State(status.State), since = status.Since is { } s ? Seconds(s) : null };

    private static object? Position(SkimmerPosition? p) => p is null ? null : new { locator = p.Locator, lat = p.Lat, lon = p.Lon, source = p.Source };

    private static string Seconds(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Millis(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
