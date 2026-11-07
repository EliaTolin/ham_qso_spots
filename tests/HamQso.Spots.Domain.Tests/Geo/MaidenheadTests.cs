using HamQso.Spots.Domain.Geo;

namespace HamQso.Spots.Domain.Tests.Geo;

public class MaidenheadTests
{
    [Theory]
    [InlineData("JN79FX", 49.98, 14.46)]
    [InlineData("jn79fx", 49.98, 14.46)]
    [InlineData("IO91ON", 51.56, -0.79)]
    [InlineData("KO10AA", 50.02, 22.04)]
    [InlineData("AA00AA", -89.98, -179.96)]
    [InlineData("RR99XX", 89.98, 179.96)]
    public void The_centre_of_a_6_character_locator(string locator, double lat, double lon)
    {
        var centre = Maidenhead.Centre6(locator);

        Assert.NotNull(centre);
        Assert.Equal(lat, centre.Value.Lat, 2);
        Assert.Equal(lon, centre.Value.Lon, 2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("JN45")]
    [InlineData("JN45ON12")]
    [InlineData("SN45ON")]
    [InlineData("JS45ON")]
    [InlineData("JNA5ON")]
    [InlineData("JN45OY")]
    [InlineData("JN45 N")]
    [InlineData(" JN45O")]
    public void Anything_but_a_valid_6_character_locator_has_no_centre(string? locator) => Assert.Null(Maidenhead.Centre6(locator));

    [Theory]
    [InlineData("io86ha", "IO86HA")]
    [InlineData("JN79FX", "JN79FX")]
    public void A_valid_locator_is_written_in_capitals(string locator, string normal) => Assert.Equal(normal, Maidenhead.Normalize6(locator));
}
