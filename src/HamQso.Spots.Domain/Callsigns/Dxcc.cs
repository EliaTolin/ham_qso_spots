using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HamQso.Spots.Domain.Callsigns;

/// <summary>
/// Callsign to DXCC entity / continent by longest prefix, with the prefixes of the
/// app's cty.csv. Port of ham_qso_supabase/supabase/functions/_shared/dxcc-prefix.ts
/// (same data file, Data/dxcc_prefixes.json). Prefix-only: exact-call exceptions
/// of cty.csv are not applied.
/// </summary>
public static partial class Dxcc
{
    private const int Guantanamo = 105;
    private const int Usa = 291;

    /// <summary>Portable suffixes that never carry the entity ("/P", "/QRP", "/1").</summary>
    private static readonly HashSet<string> Suffixes = ["P", "M", "QRP", "QRPP", "A", "R", "B", "LH", "J", "T", "X"];

    /// <summary>Maritime / aeronautical mobile: no entity.</summary>
    private static readonly HashSet<string> NoEntity = ["MM", "AM"];

    private static readonly Lazy<PrefixData> Data = new(Load);

    /// <summary>
    /// The part of a callsign that tells the entity: "EA8/IK2ABC" is "EA8",
    /// "IK2ABC/P" is "IK2ABC", "DL1ABC-#" (skimmer) is "DL1ABC", "K1ABC/MM" is null.
    /// </summary>
    public static string? EntityCallsign(string? call)
    {
        var parts = (call ?? "").ToUpperInvariant().Trim().Split('-')[0]
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(NoEntity.Contains)) return null;
        var kept = parts.Where(p => !Suffixes.Contains(p) && !SingleDigit().IsMatch(p)).ToArray();
        if (kept.Length == 0) return null;
        // "EA8/IK2ABC" or "IK2ABC/EA8": the shorter part is the prefix.
        return kept.Length == 1 ? kept[0] : kept.OrderBy(p => p.Length).First();
    }

    /// <summary>ADIF DXCC number of a callsign, or null.</summary>
    public static int? OfCallsign(string? call)
    {
        var c = EntityCallsign(call);
        if (c is null || !Alphanumeric().IsMatch(c)) return null;
        var data = Data.Value;
        // KG4 is Guantanamo Bay only with a 2-letter suffix (KG4AB) or as a bare
        // prefix (KG4/K1ABC); KG4ABC and KG4A are US calls of the 4th district.
        if (c.StartsWith("KG4", StringComparison.Ordinal) && data.Prefixes.GetValueOrDefault("KG4") == Guantanamo)
        {
            return Guantanamo2Letters().IsMatch(c) ? Guantanamo : Usa;
        }
        for (var len = Math.Min(c.Length, data.MaxPrefix); len > 0; len--)
        {
            if (data.Prefixes.TryGetValue(c[..len], out var dxcc)) return dxcc;
        }
        return null;
    }

    /// <summary>Continent (AF, AN, AS, EU, NA, OC, SA) of a DXCC entity, or null.</summary>
    public static string? ContinentOf(int? dxcc) =>
        dxcc is { } d ? Data.Value.Continents.GetValueOrDefault(d) : null;

    private static PrefixData Load()
    {
        using var stream = typeof(Dxcc).Assembly.GetManifestResourceStream("dxcc_prefixes.json")
            ?? throw new InvalidOperationException("dxcc_prefixes.json is not embedded");
        using var doc = JsonDocument.Parse(stream);
        var prefixes = doc.RootElement.GetProperty("prefixes").EnumerateObject()
            .ToFrozenDictionary(p => p.Name, p => p.Value.GetInt32());
        var continents = doc.RootElement.GetProperty("continents").EnumerateObject()
            .ToFrozenDictionary(p => int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture), p => p.Value.GetString()!);
        return new PrefixData(prefixes, continents, prefixes.Keys.Max(k => k.Length));
    }

    private sealed record PrefixData(
        FrozenDictionary<string, int> Prefixes,
        FrozenDictionary<int, string> Continents,
        int MaxPrefix);

    [GeneratedRegex(@"^\d$")]
    private static partial Regex SingleDigit();

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex Alphanumeric();

    [GeneratedRegex("^KG4([A-Z]{2})?$")]
    private static partial Regex Guantanamo2Letters();
}
