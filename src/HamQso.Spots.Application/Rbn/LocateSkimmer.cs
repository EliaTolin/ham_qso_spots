using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Rbn;

/// <summary>A skimmer with its position (null when neither node nor entity is known), DXCC entity and continent.</summary>
public sealed record SkimmerInfo(string Call, SkimmerPosition? Position, int? Dxcc, string? Continent);

/// <summary>Where a skimmer is: its node's locator, else the centre of its country (<see cref="SkimmerPosition"/>).</summary>
public sealed class LocateSkimmer(RbnNodeCatalog catalog)
{
    public SkimmerInfo Locate(string skimmer)
    {
        var call = RbnSpotLine.SkimmerCall(skimmer);
        var dxcc = Dxcc.OfCallsign(call);
        var position = SkimmerPosition.Of(catalog.Find(call)?.Locator, Dxcc.CentreOf(dxcc));
        return new SkimmerInfo(call, position, dxcc, Dxcc.ContinentOf(dxcc));
    }
}
