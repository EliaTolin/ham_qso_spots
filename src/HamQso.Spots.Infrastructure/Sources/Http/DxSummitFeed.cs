using System.Text.Json;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Sources;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>DX Summit (dxsummit.fi API v1): frequency in kHz, time UTC without zone.</summary>
public sealed class DxSummitFeed : IPolledFeed
{
    public string Name => "DXSummit";

    public Uri Url { get; } = new("https://www.dxsummit.fi/api/v1/spots?limit=100");

    public IReadOnlyList<Spot> Parse(string body, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var spots = new List<Spot>();
        foreach (var r in JsonValues.Rows(doc.RootElement))
        {
            if (r.Number("frequency") is not { } khz || khz <= 0 || r.Text("dx_call") is not { } call || r.Time("time") is not { } time) continue;
            spots.Add(DxSpot.Create(Name, call.ToUpperInvariant(), khz, time, r.Text("de_call")?.ToUpperInvariant(), r.Text("info")));
        }
        return spots;
    }
}
