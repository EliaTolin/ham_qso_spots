using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Spots;

/// <summary>
/// When two spots are the same spot.
/// DX: same call, same kHz rounded, at most <see cref="Window"/> apart.
/// xOTA: the same activation on the same band (<see cref="ActivationKey"/>),
/// at any distance in time: a new spot of it updates it, an older one is ignored.
/// </summary>
public static class DuplicateRule
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a spot is remembered to recognise its duplicates: DX twice the
    /// window; xOTA 6 hours, an activation's length and as long as Supabase keeps
    /// the alerted keys (processed_spots), so a respot never alerts again.
    /// </summary>
    public static TimeSpan MemoryOf(SpotProgram program) =>
        program == SpotProgram.Dx ? 2 * Window : TimeSpan.FromHours(6);

    /// <summary>The longest memory of all programs: what the dedup reloads at startup.</summary>
    public static readonly TimeSpan LongestMemory = TimeSpan.FromHours(6);

    /// <summary>The key two duplicates share (time is compared with <see cref="IsDuplicate"/>).</summary>
    public static string KeyOf(Spot spot) => spot.Program == SpotProgram.Dx
        ? FormattableString.Invariant($"{spot.Callsign}_{Math.Round(spot.FrequencyKhz, MidpointRounding.AwayFromZero):0}")
        : FormattableString.Invariant($"{spot.Program}_{spot.SpotKey}");

    public static bool IsDuplicate(Spot first, Spot next) =>
        KeyOf(first) == KeyOf(next) && (first.Program != SpotProgram.Dx || (next.SpotTime - first.SpotTime).Duration() <= Window);

    /// <summary>
    /// The spot_key of an xOTA spot: one per activation and band ("K8RLE_US-6701_40m"),
    /// so that every new spot of it (each with a new id at POTA) updates the same row.
    /// </summary>
    public static string ActivationKey(Spot spot) =>
        $"{spot.Callsign}_{spot.Reference}_{spot.Band ?? SpotTopics.Other}";

    /// <summary>
    /// DX: the first spot with the feeds and spotters of the next one added; a
    /// mode reported by the next one wins over a band plan guess. xOTA: the newer.
    /// </summary>
    public static Spot Merge(Spot first, Spot next)
    {
        if (first.Program != SpotProgram.Dx)
        {
            // xOTA: the newer spot of the activation wins; feeds are kept together.
            return next.SpotTime > first.SpotTime ? next with { Feeds = first.Feeds.Union(next.Feeds).ToArray() } : first;
        }

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
            Lotw = first.Lotw ?? next.Lotw,
            Eqsl = first.Eqsl ?? next.Eqsl,
        };
    }
}
