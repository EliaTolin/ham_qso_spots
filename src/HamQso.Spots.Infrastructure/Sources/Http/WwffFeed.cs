using System.Text.Json;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>WWFF (spots.wwff.co): frequency in kHz, time in Unix seconds. QRT spots are skipped.</summary>
public sealed class WwffFeed : IPolledFeed
{
    public string Name => "WWFF";

    public Uri Url { get; } = new("https://spots.wwff.co/static/spots.json");

    public IReadOnlyList<Spot> Parse(string body, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var spots = new List<Spot>();
        foreach (var r in JsonValues.Rows(doc.RootElement))
        {
            var remarks = r.Text("remarks");
            if (QrtComment.Says(remarks)) continue;
            if (r.Number("frequency_khz") is not { } khz || khz <= 0 || r.Text("activator") is not { } call || r.Id("id") is not { } id) continue;
            spots.Add(new Spot
            {
                Program = SpotProgram.Wwff,
                SpotKey = id,
                Callsign = call.ToUpperInvariant(),
                FrequencyKhz = khz,
                SpotTime = r.Number("spot_time") is { } unix ? DateTimeOffset.FromUnixTimeSeconds((long)unix) : now,
                Feeds = [Name],
                Spotters = r.Text("spotter") is { } spotter ? [spotter.ToUpperInvariant()] : [],
                Mode = r.Text("mode")?.ToUpperInvariant() ?? "",
                Reference = r.Text("reference")?.ToUpperInvariant(),
                ReferenceName = r.Text("reference_name"),
                Latitude = r.Number("latitude"),
                Longitude = r.Number("longitude"),
                Comment = remarks,
            });
        }
        return spots;
    }
}
