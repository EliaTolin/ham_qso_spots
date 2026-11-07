using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Spots;

/// <summary>
/// F88: the spot a frequency suggests. Same tolerances as the app's tunedSpot
/// (lib/common/utils/tuned_spot.dart, shared cases in on_frequency_cases.json),
/// a different choice: among the spots on the frequency the most recent wins,
/// not the nearest. DX times have only the minute, so ties happen: then the
/// nearest, then the last saved (seq), then program and spot key, so that the
/// answer never depends on the order of the rows.
/// </summary>
public static class SpotOnFrequency
{
    /// <summary>How far back a spot still counts; a spot of exactly this age does. Ahead: <see cref="Spot.FutureTolerance"/>.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private const double AmFmToleranceKhz = 3;
    private const double CwToleranceKhz = 0.5;
    private const double PhoneToleranceKhz = 1.5;
    private const double DigitalToleranceKhz = 1;

    /// <summary>FT8/FT4/JS8 spots report the audio offset: they are compared on the calling dial.</summary>
    private const double CallingDialToleranceKhz = 0.1;

    /// <summary>Frequencies come from decimal text: differences below this are rounding.</summary>
    private const double RoundingKhz = 1e-9;

    private const int DistanceDecimals = 6;

    /// <summary>
    /// No spot farther than this from the request can be on its frequency: the
    /// widest tolerance, or a spot at the far edge of a calling window with the
    /// request on the other side of the dial. What the archive has to read.
    /// </summary>
    public static double CandidateRadiusKhz { get; } =
        Math.Max(AmFmToleranceKhz, BandPlan.CallingWindowReachKhz + CallingDialToleranceKhz) + RoundingKhz;

    /// <summary>How far (kHz) a request can be from a spot of this mode, outside the calling windows.</summary>
    public static double ToleranceKhz(string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var m = mode.Trim().ToUpperInvariant();
        if (m is "AM" or "FM") return AmFmToleranceKhz;
        return BandPlan.CategoryOf(m) switch
        {
            ModeCategory.Cw => CwToleranceKhz,
            ModeCategory.Phone => PhoneToleranceKhz,
            _ => DigitalToleranceKhz,
        };
    }

    /// <summary>The spot on <paramref name="khz"/> at <paramref name="now"/>; null when none.</summary>
    public static Spot? Pick(double khz, IEnumerable<Spot> spots, DateTimeOffset now)
    {
        var since = now - MaxAge;
        var until = now + Spot.FutureTolerance;
        return spots
            .Where(s => !IsQrt(s) && s.FrequencyKhz > 0 && s.SpotTime >= since && s.SpotTime <= until)
            .Select(s => (Spot: s, Distance: DistanceIfOnFrequency(khz, s)))
            .Where(c => c.Distance is not null)
            .OrderByDescending(c => c.Spot.SpotTime)
            .ThenBy(c => Math.Round(c.Distance!.Value, DistanceDecimals))
            .ThenByDescending(c => c.Spot.Seq)
            .ThenBy(c => c.Spot.Program)
            .ThenBy(c => c.Spot.SpotKey, StringComparer.Ordinal)
            .Select(c => c.Spot)
            .FirstOrDefault();
    }

    /// <summary>The source says so or the comment does, in every program, as the app's isQrt.</summary>
    private static bool IsQrt(Spot spot) => spot.Qrt || QrtComment.Says(spot.Comment);

    private static double? DistanceIfOnFrequency(double khz, Spot spot)
    {
        var dial = BandPlan.CallingDialFor(spot.FrequencyKhz, spot.Mode);
        var distance = Math.Abs((dial ?? spot.FrequencyKhz) - khz);
        var tolerance = dial is null ? ToleranceKhz(spot.Mode) : CallingDialToleranceKhz;
        return distance <= tolerance + RoundingKhz ? distance : null;
    }
}
