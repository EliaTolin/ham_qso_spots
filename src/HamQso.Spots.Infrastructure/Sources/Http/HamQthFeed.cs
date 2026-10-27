using System.Text.Json;

using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Sources;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>
/// DX cluster via HamQTH API v2 (HamQTH asks for one request every 15 s and for
/// "Data provided by HamQTH.com" to be shown). Its DXCC and continent are kept;
/// its mode is a guess, so the mode is resolved like every other DX feed.
/// </summary>
public sealed class HamQthFeed : IPolledFeed
{
    public string Name => "HamQTH";

    public Uri Url { get; } = new("https://www.hamqth.com/api/v2/spots?limit=200");

    public SpotProgram? SnapshotOf => null;

    public IReadOnlyList<Spot> Parse(string body, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("spots", out var rows)) return [];
        var spots = new List<Spot>();
        foreach (var r in JsonValues.Rows(rows))
        {
            if (r.Number("freq") is not { } khz || khz <= 0 || r.Text("dxcall") is not { } call || r.Time("time") is not { } time) continue;
            spots.Add(DxSpot.Create(Name, call.ToUpperInvariant(), khz, time, r.Text("spotter")?.ToUpperInvariant(), r.Text("comment")) with
            {
                Dxcc = r.Number("dxcc") is { } dxcc and > 0 ? (int)dxcc : null,
                Continent = r.Text("continent")?.ToUpperInvariant(),
            });
        }
        return spots;
    }
}
