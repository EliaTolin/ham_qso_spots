using HamQso.Spots.Infrastructure.Sources.Telnet;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Telnet;

/// <summary>Same cases as the app's test/features/dx_cluster/parsers_test.dart.</summary>
public class DxClusterLineParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Live_DX_de_line_with_skimmer_spotter_and_locator()
    {
        var spot = DxClusterLineParser.Parse("DXFun", "DX de EA4RCH-#:   14025.0  JA1ABC       CW 12 dB 25 WPM CQ             2009Z IN80", Now)!;

        Assert.Equal("EA4RCH-#", spot.Spotters[0]);
        Assert.Equal(14025.0, spot.FrequencyKhz);
        Assert.Equal("JA1ABC", spot.Callsign);
        Assert.Equal("CW 12 dB 25 WPM CQ", spot.Comment);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 20, 9, 0, TimeSpan.Zero), spot.SpotTime);
        Assert.Equal("IN80", spot.SpotterLocator);
        Assert.Equal(["DXFun"], spot.Feeds);
    }

    [Fact]
    public void Live_line_without_comment_nor_locator()
    {
        var spot = DxClusterLineParser.Parse("DXFun", "DX de IK2ABC:     7074.0  F4LHL                                      2015Z", Now)!;

        Assert.Equal("IK2ABC", spot.Spotters[0]);
        Assert.Null(spot.Comment);
        Assert.Null(spot.SpotterLocator);
    }

    [Fact]
    public void BEL_characters_at_the_end_of_a_DXFun_line_are_ignored()
    {
        var spot = DxClusterLineParser.Parse("DXFun", "DX de K4CAE:     14323.0  K1DLA        US-3014                        2139Z\a\a", Now.AddHours(1))!;

        Assert.Equal("K1DLA", spot.Callsign);
        Assert.Equal("US-3014", spot.Comment);
    }

    [Fact]
    public void A_time_later_than_now_belongs_to_yesterday()
    {
        var spot = DxClusterLineParser.Parse("DXFun", "DX de IK2ABC:  7074.0  F4LHL  ft8  2359Z", Now)!;

        Assert.Equal(new DateTimeOffset(2026, 10, 5, 23, 59, 0, TimeSpan.Zero), spot.SpotTime);
    }

    [Fact]
    public void Sh_dx_history_line()
    {
        var spot = DxClusterLineParser.Parse("DXFun", "  14025.0  JA1ABC       6-Oct-2026 2009Z CW 12 dB 25 WPM CQ         <EA4RCH-#>", Now)!;

        Assert.Equal("JA1ABC", spot.Callsign);
        Assert.Equal("EA4RCH-#", spot.Spotters[0]);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 20, 9, 0, TimeSpan.Zero), spot.SpotTime);
        Assert.Equal("CW 12 dB 25 WPM CQ", spot.Comment);
    }

    [Theory]
    [InlineData("login: ")]
    [InlineData("Hello IU2ABC, this is EA4RCH-5 in Madrid")]
    [InlineData("")]
    [InlineData("DX de IK2ABC:  abc  F4LHL  ft8  2359Z")]
    [InlineData("DX de IK2ABC:  7074.0  F4LHL  ft8  2599Z")]
    [InlineData("DX de IK2ABC:  7074.0  F4LHL  ft8  1275Z")]
    public void Ignores_everything_else(string line) => Assert.Null(DxClusterLineParser.Parse("DXFun", line, Now));
}
