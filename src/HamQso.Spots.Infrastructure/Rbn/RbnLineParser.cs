using System.Text.RegularExpressions;
using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>
/// A spot line of the RBN telnet feed, as the app's RbnLineParser reads it:
/// <code>DX de OK4YL-#:   7025.10  G0BQV   CW   7 dB  23 WPM  CQ   2055Z</code>
/// Anything else (banner, prompt) is not a spot.
/// </summary>
public static partial class RbnLineParser
{
    /// <summary>The spot (the line trimmed, without control characters) or null.</summary>
    public static RbnLine? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = new string([.. line.Where(c => !char.IsControl(c))]).Trim();
        var m = Spot().Match(text);
        return m.Success ? new RbnLine(text, m.Groups[1].Value.ToUpperInvariant()) : null;
    }

    // The app's pattern; only the decoded station is kept apart.
    [GeneratedRegex(@"^DX de [A-Z0-9/]+(?:-\d+)?(?:-#)?:\s+\d+(?:\.\d+)?\s+([A-Z0-9/]+)\s+[A-Z0-9]+\s+-?\d+\s+dB\s+\d+\s+(?:WPM|BPS)\s+.+?\s+\d{4}Z", RegexOptions.IgnoreCase)]
    private static partial Regex Spot();
}
