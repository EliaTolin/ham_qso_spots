namespace HamQso.Spots.Domain.Radio;

/// <summary>Amateur bands (kHz), same edges as the app (lib/common/utils/band.dart) and match_alerts.</summary>
public static class Bands
{
    private static readonly (string Label, double Min, double Max)[] All =
    [
        ("160m", 1800, 2000),
        ("80m", 3500, 4000),
        ("60m", 5250, 5450),
        ("40m", 7000, 7300),
        ("30m", 10100, 10150),
        ("20m", 14000, 14350),
        ("17m", 18068, 18168),
        ("15m", 21000, 21450),
        ("12m", 24890, 24990),
        ("10m", 28000, 29700),
        ("6m", 50000, 54000),
        ("4m", 70000, 70500),
        ("2m", 144000, 148000),
        ("70cm", 420000, 450000),
        ("23cm", 1240000, 1300000),
    ];

    /// <summary>Every band label, lowest first ("160m" ... "23cm").</summary>
    public static IReadOnlyList<string> Labels { get; } = [.. All.Select(b => b.Label)];

    /// <summary>The band of a frequency, or null outside the amateur bands.</summary>
    public static string? Of(double khz)
    {
        foreach (var (label, min, max) in All)
        {
            if (khz >= min && khz <= max) return label;
        }
        return null;
    }
}
