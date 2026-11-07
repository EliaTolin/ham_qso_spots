namespace HamQso.Spots.Domain.Geo;

/// <summary>Maidenhead locators (the 6-character ones of the RBN node list, F71).</summary>
public static class Maidenhead
{
    /// <summary>"io86ha" as "IO86HA" when it is a valid 6-character locator, otherwise null.</summary>
    public static string? Normalize6(string? locator)
    {
        if (locator is not { Length: 6 }) return null;
        var l = locator.ToUpperInvariant();
        return Between(l[0], 'A', 'R') && Between(l[1], 'A', 'R')
            && Between(l[2], '0', '9') && Between(l[3], '0', '9')
            && Between(l[4], 'A', 'X') && Between(l[5], 'A', 'X') ? l : null;
    }

    /// <summary>The centre of a valid 6-character locator (degrees, east and north positive), or null.</summary>
    public static (double Lat, double Lon)? Centre6(string? locator)
    {
        if (Normalize6(locator) is not { } l) return null;
        var lon = -180 + (l[0] - 'A') * 20 + (l[2] - '0') * 2 + (l[4] - 'A') * (5.0 / 60) + 2.5 / 60;
        var lat = -90 + (l[1] - 'A') * 10 + (l[3] - '0') + (l[5] - 'A') * (2.5 / 60) + 1.25 / 60;
        return (lat, lon);
    }

    private static bool Between(char c, char min, char max) => c >= min && c <= max;
}
