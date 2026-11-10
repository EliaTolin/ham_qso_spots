namespace HamQso.Spots.VoacapCalibration;

/// <summary>One event: every listening skimmer with the probability VOACAP gave and whether it heard.</summary>
public sealed record CalibrationOutcome(IReadOnlyList<(double Rel, bool Heard)> Skimmers);

/// <summary>
/// How well a setting predicts: Brier score of every skimmer (lower is better),
/// expected (sum of probabilities) against heard, and the margin of the app
/// (plan F71, section 4: (heard − expected) / expected, only with at least 3
/// expected; above ≥ +5%, below ≤ −5%).
/// </summary>
public sealed record Score(int Events, double Expected, int Heard, double Brier, double MedianMargin, int Above, int Near, int Below, double P10Margin, double P90Margin, int WithMargin)
{
    public const double MinExpected = 3;
    public const double NearBand = 0.05;

    public static Score Of(IReadOnlyList<CalibrationOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var all = outcomes.SelectMany(o => o.Skimmers).ToArray();
        var margins = outcomes
            .Select(o => (Expected: o.Skimmers.Sum(s => s.Rel), Heard: o.Skimmers.Count(s => s.Heard)))
            .Where(o => o.Expected >= MinExpected)
            .Select(o => (o.Heard - o.Expected) / o.Expected)
            .Order()
            .ToArray();
        return new Score(
            outcomes.Count,
            all.Sum(s => s.Rel),
            all.Count(s => s.Heard),
            all.Length == 0 ? 0 : all.Average(s => Math.Pow(s.Rel - (s.Heard ? 1 : 0), 2)),
            Median(margins),
            margins.Count(m => m >= NearBand),
            margins.Count(m => m > -NearBand && m < NearBand),
            margins.Count(m => m <= -NearBand),
            Percentile(margins, 0.1),
            Percentile(margins, 0.9),
            margins.Length);
    }

    /// <summary>
    /// The best settings of a group: the lowest Brier among those whose median margin is within ±10%;
    /// if none is, the median margin nearest 0; settings with no margin at all (NaN) only when no other has one.
    /// </summary>
    public static (T Settings, Score Score) Best<T>(IReadOnlyList<(T Settings, Score Score)> scored)
    {
        ArgumentNullException.ThrowIfNull(scored);
        var withMargin = scored.Where(x => !double.IsNaN(x.Score.MedianMargin)).ToArray();
        var near = withMargin.Where(x => Math.Abs(x.Score.MedianMargin) <= 0.10).ToArray();
        return near.Length > 0 ? near.MinBy(x => x.Score.Brier)
            : withMargin.Length > 0 ? withMargin.MinBy(x => Math.Abs(x.Score.MedianMargin))
            : scored.MinBy(x => x.Score.Brier);
    }

    private static double Median(double[] sorted) => Percentile(sorted, 0.5);

    /// <summary>Linear interpolation between the closest ranks (as numpy's default).</summary>
    public static double Percentile(double[] sorted, double p)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Length == 0) return double.NaN;
        var rank = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
