using System.Globalization;
using System.Text.RegularExpressions;

namespace HamQso.Spots.Domain.Rbn;

/// <summary>
/// A spot of the Reverse Beacon Network telnet feed, as the app's RbnLineParser reads it:
/// <code>DX de OK4YL-#:   7025.10  G0BQV   CW   7 dB  23 WPM  CQ   2055Z</code>
/// Skimmer without "-#" ("KM3T-2" for "KM3T-2-#"); Call in capitals as sent
/// ("EA8/IN3FOB/P"); Speed in words per minute (SpeedUnit "WPM") or bauds
/// ("BPS"); Kind "CQ", "BEACON", "NCDXF B"…; RbnTime as the RBN wrote it, "1432Z".
/// </summary>
public sealed record RbnSpot(string Skimmer, string Call, double Khz, string Mode, int SnrDb, int Speed, string SpeedUnit, string Kind, string RbnTime)
{
    /// <summary>The CW speed, or null for a speed in bauds.</summary>
    public int? Wpm => SpeedUnit == "WPM" ? Speed : null;
}

/// <summary>Reads RBN spot lines; anything else (banner, prompt) is not a spot.</summary>
public static partial class RbnSpotLine
{
    /// <summary>The spot of <paramref name="line"/> (control characters ignored), or null.</summary>
    public static RbnSpot? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var m = Spot().Match(Clean(line));
        if (!m.Success) return null;
        return new RbnSpot(
            SkimmerCall(m.Groups["skimmer"].Value),
            m.Groups["call"].Value.ToUpperInvariant(),
            double.Parse(m.Groups["khz"].Value, CultureInfo.InvariantCulture),
            m.Groups["mode"].Value.ToUpperInvariant(),
            int.Parse(m.Groups["snr"].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups["speed"].Value, CultureInfo.InvariantCulture),
            m.Groups["unit"].Value.ToUpperInvariant(),
            Spaces().Replace(m.Groups["kind"].Value.Trim(), " ").ToUpperInvariant(),
            m.Groups["time"].Value.ToUpperInvariant());
    }

    /// <summary>The line trimmed, without control characters (what the v1 stream sends).</summary>
    public static string Clean(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new string([.. line.Where(c => !char.IsControl(c))]).Trim();
    }

    /// <summary>A skimmer's call in capitals without the RBN "-#": "OK4YL-#" is "OK4YL", "KM3T-2-#" is "KM3T-2".</summary>
    public static string SkimmerCall(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var call = raw.Trim().ToUpperInvariant();
        return call.EndsWith("-#", StringComparison.Ordinal) ? call[..^2] : call;
    }

    // The app's pattern, with every field in a group.
    [GeneratedRegex(@"^DX de (?<skimmer>[A-Z0-9/]+(?:-\d+)?(?:-#)?):\s+(?<khz>\d+(?:\.\d+)?)\s+(?<call>[A-Z0-9/]+)\s+(?<mode>[A-Z0-9]+)\s+(?<snr>-?\d+)\s+dB\s+(?<speed>\d+)\s+(?<unit>WPM|BPS)\s+(?<kind>.+?)\s+(?<time>\d{4}Z)", RegexOptions.IgnoreCase)]
    private static partial Regex Spot();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
