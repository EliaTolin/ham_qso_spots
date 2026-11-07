using HamQso.Spots.Domain.Geo;

namespace HamQso.Spots.Domain.Rbn;

/// <summary>
/// Where a skimmer is (plan F71, section 4, the only place of this rule): the
/// centre of the 6-character locator of its node in the RBN node list when it
/// is valid, otherwise the centre of its country (cty.dat). Two decimals, as
/// the contract (about 1 km). Locator is the node's, null for a
/// <see cref="Country"/> source.
/// </summary>
public sealed record SkimmerPosition(string? Locator, double Lat, double Lon, string Source)
{
    public const string Node = "node";
    public const string Country = "country";

    /// <summary>The position from the node's locator, else from the country's centre, else null.</summary>
    public static SkimmerPosition? Of(string? nodeLocator, (double Lat, double Lon)? countryCentre)
    {
        if (Maidenhead.Centre6(nodeLocator) is { } c) return new(Maidenhead.Normalize6(nodeLocator), Round(c.Lat), Round(c.Lon), Node);
        return countryCentre is { } k ? new(null, Round(k.Lat), Round(k.Lon), Country) : null;
    }

    private static double Round(double degrees) => Math.Round(degrees, 2, MidpointRounding.AwayFromZero);
}
