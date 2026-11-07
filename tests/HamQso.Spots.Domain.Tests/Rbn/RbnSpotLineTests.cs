using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Domain.Tests.Rbn;

public class RbnSpotLineTests
{
    [Fact]
    public void A_CW_spot_gives_every_field()
    {
        var spot = RbnSpotLine.Parse("DX de OK4YL-#:   14045.1  iu2demo        CW    17 dB  23 WPM  CQ      1432Z");

        Assert.Equal(new RbnSpot("OK4YL", "IU2DEMO", 14045.1, "CW", 17, 23, "WPM", "CQ", "1432Z"), spot);
    }

    [Theory]
    [InlineData("DX de KM3T-2-#:  14025.0  ea8/in3fob/p   CW    12 dB  25 WPM  CQ      0000Z", "KM3T-2", "EA8/IN3FOB/P", 12, 25, "WPM", "CQ")]
    [InlineData("DX de W3OA-#:    14080.0  K1ABC          RTTY  -3 dB  45 BPS  CQ      1259Z\a\a\r", "W3OA", "K1ABC", -3, 45, "BPS", "CQ")]
    [InlineData("DX de VE6WZ-#:   28200.0  VE8AT/B        CW    18 dB  22 WPM  BEACON  1801Z", "VE6WZ", "VE8AT/B", 18, 22, "WPM", "BEACON")]
    [InlineData("DX de DK9IP-#:   14100.0  4U1UN          CW    20 dB  22 WPM  NCDXF B 1803Z", "DK9IP", "4U1UN", 20, 22, "WPM", "NCDXF B")]
    public void Skimmer_without_the_hash_station_snr_speed_and_kind(string line, string skimmer, string call, int snr, int speed, string unit, string kind)
    {
        var spot = RbnSpotLine.Parse(line)!;

        Assert.Equal((skimmer, call, snr, speed, unit, kind), (spot.Skimmer, spot.Call, spot.SnrDb, spot.Speed, spot.SpeedUnit, spot.Kind));
    }

    [Theory]
    [InlineData("Welcome to the Reverse Beacon Network")]
    [InlineData("Please enter your call: ")]
    [InlineData("DX de EA4RCH-5:   14025.0  JA1ABC       CW CQ             2009Z")]
    [InlineData("")]
    public void Anything_else_is_not_a_spot(string line) => Assert.Null(RbnSpotLine.Parse(line));

    [Fact]
    public void The_speed_is_words_per_minute_only_for_WPM()
    {
        Assert.Equal(23, RbnSpotLine.Parse("DX de OK4YL-#:   7025.10  G0BQV   CW   7 dB  23 WPM  CQ   2055Z")!.Wpm);
        Assert.Null(RbnSpotLine.Parse("DX de W3OA-#:    14080.0  K1ABC   RTTY  -3 dB  45 BPS  CQ   1259Z")!.Wpm);
    }

    [Theory]
    [InlineData("OK4YL-#", "OK4YL")]
    [InlineData("km3t-2-#", "KM3T-2")]
    [InlineData("PA5KT-4", "PA5KT-4")]
    [InlineData(" GM0UDL ", "GM0UDL")]
    public void A_skimmer_call_is_written_without_the_hash_in_capitals(string raw, string call) => Assert.Equal(call, RbnSpotLine.SkimmerCall(raw));
}
