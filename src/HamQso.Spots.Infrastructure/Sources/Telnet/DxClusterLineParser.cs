using System.Globalization;
using System.Text.RegularExpressions;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>
/// The two spot formats of DXSpider-compatible telnet nodes (AR-Cluster and CC
/// Cluster use the same layout). Port of the app's dx_cluster_line_parser.dart.
/// <code>
/// Live:  DX de EA4RCH-#:   14025.0  JA1ABC   CW 12 dB 25 WPM CQ     2009Z IN80
/// sh/dx:   14025.0  JA1ABC      6-Oct-2026 2009Z CW 12 dB 25 WPM    &lt;EA4RCH-#&gt;
/// </code>
/// The spotter is kept as sent ("EA4RCH-#" tells a skimmer). Times outside 0000-2359 are not spots.
/// </summary>
public static partial class DxClusterLineParser
{
    /// <summary>A live line in the future (HHMM only) is from yesterday.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static Spot? Parse(string feed, string line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = ControlChars().Replace(line, " ").TrimEnd();

        var live = Live().Match(text);
        if (live.Success)
        {
            if (!TryKhz(live.Groups[2].Value, out var khz)) return null;
            return DxSpot.Create(
                feed,
                live.Groups[3].Value.ToUpperInvariant(),
                khz,
                TimeToday(live.Groups[5].Value, now),
                live.Groups[1].Value.ToUpperInvariant(),
                Comment(live.Groups[4].Value)) with
            {
                SpotterLocator = live.Groups[6].Success ? live.Groups[6].Value.ToUpperInvariant() : null,
            };
        }

        var history = History().Match(text);
        if (!history.Success || !TryKhz(history.Groups[1].Value, out var hKhz)) return null;
        var date = $"{history.Groups[3].Value}-{history.Groups[4].Value}-{history.Groups[5].Value} {history.Groups[6].Value}";
        if (!DateTimeOffset.TryParseExact(date, ["d-MMM-yyyy HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)) return null;
        return DxSpot.Create(
            feed,
            history.Groups[2].Value.ToUpperInvariant(),
            hKhz,
            time,
            history.Groups[8].Value.ToUpperInvariant(),
            Comment(history.Groups[7].Value));
    }

    private static bool TryKhz(string value, out double khz) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out khz) && khz > 0;

    private static string? Comment(string value) => value.Trim() is { Length: > 0 } c ? c : null;

    private static DateTimeOffset TimeToday(string hhmm, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var t = new DateTimeOffset(utc.Year, utc.Month, utc.Day,
            int.Parse(hhmm[..2], CultureInfo.InvariantCulture), int.Parse(hhmm[2..], CultureInfo.InvariantCulture), 0, TimeSpan.Zero);
        return t > utc + FutureTolerance ? t.AddDays(-1) : t;
    }

    [GeneratedRegex(@"^DX de ([A-Z0-9/#-]+?):?\s+(\d+(?:\.\d+)?)\s+([A-Z0-9/]+)\s+(.*?)\s*((?:[01]\d|2[0-3])[0-5]\d)Z(?:\s+([A-R]{2}\d{2}))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Live();

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s+([A-Z0-9/]+)\s+(\d{1,2})-([A-Za-z]{3})-(\d{4})\s+((?:[01]\d|2[0-3])[0-5]\d)Z\s*(.*?)\s*<([A-Z0-9/#-]+)>\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex History();

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlChars();
}
