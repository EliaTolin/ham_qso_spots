using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Spots;

/// <summary>Band, DXCC, continent and mode, computed once for every feed; xOTA spots get their activation key.</summary>
public static class SpotEnrichment
{
    public static Spot Enrich(Spot spot)
    {
        var dxcc = spot.Dxcc ?? Dxcc.OfCallsign(spot.Callsign);
        var continent = spot.Continent ?? Dxcc.ContinentOf(dxcc);
        var (mode, source) = spot.ModeSource is null || spot.Mode.Length == 0
            ? ResolveMode(spot, dxcc, continent)
            : (spot.Mode, spot.ModeSource);
        var enriched = spot with
        {
            Band = spot.Band ?? Bands.Of(spot.FrequencyKhz),
            Dxcc = dxcc,
            Continent = continent,
            Mode = mode,
            ModeSource = source,
            ModeCategory = BandPlan.CategoryOf(mode),
        };
        return enriched.Program == SpotProgram.Dx ? enriched : enriched with { SpotKey = DuplicateRule.ActivationKey(enriched) };
    }

    /// <summary>xOTA feeds report the mode; the DX cluster only has the comment and the frequency.</summary>
    private static (string Mode, ModeSource? Source) ResolveMode(Spot spot, int? dxcc, string? continent)
    {
        if (spot.Program != SpotProgram.Dx)
        {
            return spot.Mode.Length > 0 ? (spot.Mode, ModeSource.Reported) : ("", null);
        }
        var resolved = BandPlan.Resolve(spot.FrequencyKhz, spot.Comment, spot.Spotters.Count > 0 ? spot.Spotters[0] : null, BandPlan.RegionOf(dxcc, continent));
        return (resolved.Mode, resolved.Source);
    }
}
