using System.Globalization;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Primitives;

namespace HamQso.Spots.Host;

/// <summary>The query of GET /v1/spots, checked; a string says what is wrong.</summary>
public sealed record SpotQuery(SpotFilter Filter, int? Limit, long? SinceSeq);

public static class SpotQueryParser
{
    private static readonly Dictionary<string, SpotProgram> Programs = new(StringComparer.Ordinal)
    {
        ["dx"] = SpotProgram.Dx,
        ["pota"] = SpotProgram.Pota,
        ["sota"] = SpotProgram.Sota,
        ["wwff"] = SpotProgram.Wwff,
    };

    private static readonly HashSet<string> BandValues = [.. Bands.Labels, SpotTopics.Other];

    private static readonly HashSet<string> ModeValues = ["cw", "phone", "digital", SpotTopics.Other];

    /// <summary>program (required), band and mode (repeatable, the topic segments), limit, since_seq.</summary>
    public static SpotQuery? Parse(Func<string, StringValues> query, out string? error)
    {
        ArgumentNullException.ThrowIfNull(query);
        error = null;
        if (!Programs.TryGetValue(query("program").ToString(), out var program))
        {
            error = "program must be dx, pota, sota or wwff";
            return null;
        }
        var bands = Values(query("band"));
        if (bands.FirstOrDefault(b => !BandValues.Contains(b)) is { } badBand)
        {
            error = $"unknown band '{badBand}'";
            return null;
        }
        var modes = Values(query("mode"));
        if (modes.FirstOrDefault(m => !ModeValues.Contains(m)) is { } badMode)
        {
            error = $"mode must be cw, phone, digital or other, not '{badMode}'";
            return null;
        }
        int? limit = null;
        if (query("limit").ToString() is { Length: > 0 } l)
        {
            if (!int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                error = "limit must be a positive number";
                return null;
            }
            limit = n;
        }
        long? since = null;
        if (query("since_seq").ToString() is { Length: > 0 } s)
        {
            if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                error = "since_seq must be a seq received before";
                return null;
            }
            since = n;
        }
        return new SpotQuery(new SpotFilter(program, bands, modes), limit, since);
    }

    /// <summary>Repeated and comma separated values: band=20m&amp;band=40m or band=20m,40m.</summary>
    private static string[] Values(StringValues values) =>
        [.. values.SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.Ordinal)];
}
