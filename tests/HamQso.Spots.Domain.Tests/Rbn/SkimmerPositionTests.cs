using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Domain.Tests.Rbn;

/// <summary>Plan F71, section 4: the node's 6-character locator when valid, otherwise the centre of the country.</summary>
public class SkimmerPositionTests
{
    private static readonly (double Lat, double Lon) Czechia = (50, 16);

    [Fact]
    public void A_valid_node_locator_gives_its_centre()
    {
        var p = SkimmerPosition.Of("jn79fx", Czechia)!;

        Assert.Equal(("JN79FX", 49.98, 14.46, SkimmerPosition.Node), (p.Locator, p.Lat, p.Lon, p.Source));
    }

    [Theory]
    [InlineData("JN79")]
    [InlineData("XX99ZZ")]
    [InlineData("")]
    [InlineData(null)]
    public void Without_a_valid_node_locator_the_centre_of_the_country(string? locator)
    {
        var p = SkimmerPosition.Of(locator, Czechia)!;

        Assert.Equal((null, 50.0, 16.0, SkimmerPosition.Country), (p.Locator, p.Lat, p.Lon, p.Source));
    }

    [Fact]
    public void Without_either_there_is_no_position() => Assert.Null(SkimmerPosition.Of("JN79", null));

    [Fact]
    public void Coordinates_have_two_decimals_as_in_the_contract()
    {
        var p = SkimmerPosition.Of(null, (42.666, 21.174))!;

        Assert.Equal((42.67, 21.17), (p.Lat, p.Lon));
    }
}
