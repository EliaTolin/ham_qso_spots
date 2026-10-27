using System.Text.Json;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>POTA: one row per ongoing activation. Frequency in kHz (as text), time UTC without zone.</summary>
public sealed class PotaFeed : IPolledFeed
{
    public string Name => "POTA";

    public Uri Url { get; } = new("https://api.pota.app/spot/activator");

    public IReadOnlyList<Spot> Parse(string body, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var spots = new List<Spot>();
        foreach (var r in JsonValues.Rows(doc.RootElement))
        {
            if (r.Number("frequency") is not { } khz || khz <= 0 || r.Text("activator") is not { } call || r.Id("spotId") is not { } id) continue;
            spots.Add(new Spot
            {
                Program = SpotProgram.Pota,
                SpotKey = id,
                Callsign = call.ToUpperInvariant(),
                FrequencyKhz = khz,
                SpotTime = r.Time("spotTime") ?? now,
                Feeds = [Name],
                Spotters = r.Text("spotter") is { } spotter ? [spotter.ToUpperInvariant()] : [],
                Mode = r.Text("mode")?.ToUpperInvariant() ?? "",
                Reference = r.Text("reference")?.ToUpperInvariant(),
                ReferenceName = r.Text("name") ?? r.Text("parkName"),
                Location = r.Text("locationDesc")?.ToUpperInvariant(),
                Latitude = r.Number("latitude"),
                Longitude = r.Number("longitude"),
                Comment = r.Text("comments"),
            });
        }
        return spots;
    }
}
