using System.Text.Json;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>
/// SOTA (SOTAwatch 3): frequency in MHz. QRT and test spots are skipped.
/// SOTA asks to fetch the spots only when the epoch has changed (B26).
/// </summary>
public sealed class SotaFeed : IPolledFeed
{
    public string Name => "SOTA";

    public Uri Url { get; } = new("https://api-db2.sota.org.uk/api/spots/50/all/all");

    public Uri? EpochUrl { get; } = new("https://api-db2.sota.org.uk/api/spots/epoch");

    public IReadOnlyList<Spot> Parse(string body, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var spots = new List<Spot>();
        foreach (var r in JsonValues.Rows(doc.RootElement))
        {
            var type = r.Text("type")?.ToUpperInvariant();
            if (type is "QRT" or "TEST") continue;
            if (r.Number("frequency") is not { } mhz || mhz <= 0 || r.Text("activatorCallsign") is not { } call || r.Id("id") is not { } id) continue;
            spots.Add(new Spot
            {
                Program = SpotProgram.Sota,
                SpotKey = id,
                Callsign = call.ToUpperInvariant(),
                FrequencyKhz = Math.Round(mhz * 10000) / 10,
                SpotTime = r.Time("timeStamp") ?? now,
                Feeds = [Name],
                Spotters = r.Text("callsign") is { } spotter ? [spotter.ToUpperInvariant()] : [],
                Mode = r.Text("mode")?.ToUpperInvariant() ?? "",
                Reference = r.Text("summitCode")?.ToUpperInvariant(),
                ReferenceName = r.Text("summitName"),
                ActivatorName = r.Text("activatorName"),
                AltitudeM = r.Int("AltM"),
                Points = r.Int("points"),
                Latitude = r.Number("latitude"),
                Longitude = r.Number("longitude"),
                Comment = r.Text("comments"),
            });
        }
        return spots;
    }
}
