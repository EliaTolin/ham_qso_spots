namespace HamQso.Spots.Domain.Callsigns;

/// <summary>Callsign rules shared with the app and the backend.</summary>
public static class Callsign
{
    /// <summary>
    /// The base (home) callsign: "EA8/IK2ABC/P" → "IK2ABC". Parts split on "/",
    /// empty parts dropped; one part is the base; otherwise the longest part
    /// with a digit, on a tie the later one (the prefix comes first); no part
    /// with a digit: the first part. Uppercase. Same rule as the app's
    /// baseCallsign and public.base_callsign (shared cases: base_callsign_cases.json).
    /// </summary>
    public static string Base(string callsign)
    {
        ArgumentNullException.ThrowIfNull(callsign);
        var parts = callsign.ToUpperInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return parts[0];
        string? best = null;
        foreach (var p in parts)
        {
            if (p.Any(char.IsAsciiDigit) && (best is null || p.Length >= best.Length)) best = p;
        }
        return best ?? parts[0];
    }

    /// <summary>
    /// Can be a callsign: letters and digits in parts separated by "/", at least
    /// one letter and one digit (IN3FOB, EA8/IN3FOB/P, 3Y0J), at most
    /// <see cref="MaxLength"/> characters. The app's isPlausibleCallsign.
    /// </summary>
    public static bool IsPlausible(string? callsign)
    {
        var c = callsign?.Trim().ToUpperInvariant();
        if (c is null || c.Length is 0 or > MaxLength) return false;
        var parts = c.Split('/');
        return parts.All(p => p.Length > 0 && p.All(char.IsAsciiLetterOrDigit))
            && c.Any(char.IsAsciiLetter)
            && c.Any(char.IsAsciiDigit);
    }

    public const int MaxLength = 20;
}
