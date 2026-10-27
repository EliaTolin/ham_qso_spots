using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Spots;

/// <summary>
/// When two DX spots are the same spot: same call, same kHz rounded, at most
/// <see cref="Window"/> apart. xOTA spots carry their own id and never merge.
/// </summary>
public static class DuplicateRule
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>The key two duplicates share (time is compared with <see cref="IsDuplicate"/>).</summary>
    public static string KeyOf(Spot spot) => spot.Program == SpotProgram.Dx
        ? FormattableString.Invariant($"{spot.Callsign}_{Math.Round(spot.FrequencyKhz, MidpointRounding.AwayFromZero):0}")
        : FormattableString.Invariant($"{spot.Program}_{spot.SpotKey}");

    public static bool IsDuplicate(Spot first, Spot next) =>
        KeyOf(first) == KeyOf(next) && (next.SpotTime - first.SpotTime).Duration() <= Window;

    /// <summary>
    /// The first spot with the feeds and spotters of the next one added. A mode
    /// reported by the next one wins over a band plan guess.
    /// </summary>
    public static Spot Merge(Spot first, Spot next)
    {
        var nextMode = first.ModeSource != ModeSource.Reported && next.ModeSource == ModeSource.Reported;
        return first with
        {
            Feeds = first.Feeds.Union(next.Feeds).ToArray(),
            Spotters = first.Spotters.Union(next.Spotters).ToArray(),
            Comment = string.IsNullOrWhiteSpace(first.Comment) ? next.Comment : first.Comment,
            Mode = nextMode ? next.Mode : first.Mode,
            ModeSource = nextMode ? next.ModeSource : first.ModeSource,
            ModeCategory = nextMode ? next.ModeCategory : first.ModeCategory,
            Dxcc = first.Dxcc ?? next.Dxcc,
            Continent = first.Continent ?? next.Continent,
            SpotterLocator = first.SpotterLocator ?? next.SpotterLocator,
        };
    }
}
