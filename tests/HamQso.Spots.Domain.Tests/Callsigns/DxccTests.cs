using HamQso.Spots.Domain.Callsigns;

namespace HamQso.Spots.Domain.Tests.Callsigns;

public class DxccTests
{
    [Theory]
    [InlineData("IU4VRB", 248)]
    [InlineData("IK2ABC/P", 248)]
    [InlineData("EA8/IK2ABC", 29)]
    [InlineData("K1ABC", 291)]
    [InlineData("KG4AB", 105)]
    [InlineData("KG4ABC", 291)]
    [InlineData("DL1ABC-#", 230)]
    [InlineData("K1ABC/MM", null)]
    [InlineData("", null)]
    public void Finds_the_entity_from_the_prefix(string call, int? expected) => Assert.Equal(expected, Dxcc.OfCallsign(call));

    [Theory]
    [InlineData(248, "EU")]
    [InlineData(291, "NA")]
    [InlineData(null, null)]
    public void Knows_the_continent_of_an_entity(int? dxcc, string? expected) => Assert.Equal(expected, Dxcc.ContinentOf(dxcc));
}
