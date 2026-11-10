using System.Globalization;

namespace HamQso.Spots.VoacapCalibration;

/// <summary>
/// An SSN for the day of the test: the observed 10.7 cm flux of that day (Penticton, via the GFZ
/// Kp/ap/F10.7 file, CC BY 4.0; its SN column is SILSO, non-commercial, and is never read),
/// turned into an SSN with the CCIR relation F = 63.7 + 0.728 R + 0.00089 R². NOAA SWPC
/// gives the same daily flux to the app (f107_cm_flux.json).
/// </summary>
public static class SsnByDay
{
    public static double FromF107(double f107)
    {
        if (f107 <= 63.7) return 0;
        const double a = 0.00089, b = 0.728;
        return (-b + Math.Sqrt(b * b + 4 * a * (f107 - 63.7))) / (2 * a);
    }

    public static IReadOnlyDictionary<DateOnly, double> FromGfz(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var days = new Dictionary<DateOnly, double>();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('#')) continue;
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 28) continue;
            if (!int.TryParse(f[0], CultureInfo.InvariantCulture, out var y) || !int.TryParse(f[1], CultureInfo.InvariantCulture, out var m) || !int.TryParse(f[2], CultureInfo.InvariantCulture, out var d)) continue;
            // Columns: ... Ap SN F10.7obs F10.7adj D: the observed flux is the third from the end.
            if (!double.TryParse(f[^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var flux) || flux <= 0) continue;
            days[new DateOnly(y, m, d)] = FromF107(flux);
        }
        return days;
    }

    /// <summary>The SSN of a day; a day without a flux (−1 in the file) takes the nearest day within 3 days (the earlier on a tie), else null.</summary>
    public static double? On(IReadOnlyDictionary<DateOnly, double> days, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(days);
        for (var d = 0; d <= 3; d++)
        {
            if (days.TryGetValue(day.AddDays(-d), out var before)) return before;
            if (days.TryGetValue(day.AddDays(d), out var after)) return after;
        }
        return null;
    }
}
