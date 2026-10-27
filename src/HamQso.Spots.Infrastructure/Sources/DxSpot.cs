using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources;

/// <summary>A DX spot built from cluster fields; the key is call, kHz and minute.</summary>
internal static class DxSpot
{
    public static Spot Create(string feed, string call, double khz, DateTimeOffset time, string? spotter, string? comment) => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = FormattableString.Invariant($"{call}_{khz:0.0}_{time.UtcDateTime:yyyyMMddHHmm}"),
        Callsign = call,
        FrequencyKhz = khz,
        SpotTime = time,
        Feeds = [feed],
        Spotters = spotter is null ? [] : [spotter],
        Comment = comment,
    };
}
