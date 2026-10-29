using HamQso.Spots.Infrastructure.Rbn;

namespace HamQso.Spots.Infrastructure.Tests.Rbn;

public class RbnLineParserTests
{
    [Theory]
    [InlineData("DX de OK4YL-#:   7025.10  G0BQV          CW     7 dB  23 WPM  CQ      2055Z", "G0BQV")]
    [InlineData("DX de KM3T-2-#:  14025.0  ea8/in3fob/p   CW    12 dB  25 WPM  CQ      0000Z", "EA8/IN3FOB/P")]
    [InlineData("DX de W3OA-#:    14080.0  K1ABC          RTTY  -3 dB  45 BPS  CQ      1259Z\a\a\r", "K1ABC")]
    [InlineData("DX de VE6WZ-#:   28200.0  VE8AT/B        CW    18 dB  22 WPM  BEACON  1801Z", "VE8AT/B")]
    [InlineData("DX de DK9IP-#:   14100.0  4U1UN          CW    20 dB  22 WPM  NCDXF B 1803Z", "4U1UN")]
    public void A_spot_line_gives_its_station_and_the_line_without_control_characters(string line, string call)
    {
        var spot = RbnLineParser.Parse(line);

        Assert.Equal(call, spot!.Call);
        Assert.DoesNotContain(spot.Raw, c => char.IsControl(c));
        Assert.StartsWith("DX de ", spot.Raw, StringComparison.Ordinal);
        Assert.EndsWith("Z", spot.Raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Welcome to the Reverse Beacon Network")]
    [InlineData("Please enter your call: ")]
    [InlineData("DX de EA4RCH-5:   14025.0  JA1ABC       CW CQ             2009Z")]
    [InlineData("")]
    public void Anything_else_is_not_a_spot(string line) => Assert.Null(RbnLineParser.Parse(line));
}
