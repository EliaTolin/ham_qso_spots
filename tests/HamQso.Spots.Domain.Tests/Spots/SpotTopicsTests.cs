using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Domain.Tests.Spots;

public class SpotTopicsTests
{
    private static Spot Spot(SpotProgram program, string? band, ModeCategory? mode) => new()
    {
        Program = program,
        SpotKey = "k",
        Callsign = "JA1ABC",
        FrequencyKhz = 14025,
        SpotTime = DateTimeOffset.UnixEpoch,
        Feeds = ["DXFun"],
        Band = band,
        ModeCategory = mode,
    };

    [Theory]
    [InlineData("20m", ModeCategory.Cw, "spots/dx/20m/cw")]
    [InlineData("40m", ModeCategory.Phone, "spots/dx/40m/phone")]
    [InlineData("6m", ModeCategory.Digital, "spots/dx/6m/digital")]
    [InlineData("20m", null, "spots/dx/20m/other")]
    [InlineData(null, ModeCategory.Cw, "spots/dx/other/cw")]
    public void A_DX_spot_goes_on_the_topic_of_its_band_and_mode(string? band, ModeCategory? mode, string topic) =>
        Assert.Equal(topic, SpotTopics.Of(Spot(SpotProgram.Dx, band, mode)));

    [Theory]
    [InlineData(SpotProgram.Pota, "spots/pota")]
    [InlineData(SpotProgram.Sota, "spots/sota")]
    [InlineData(SpotProgram.Wwff, "spots/wwff")]
    public void An_xOTA_spot_goes_on_the_topic_of_its_program_whatever_the_band(SpotProgram program, string topic) =>
        Assert.Equal(topic, SpotTopics.Of(Spot(program, "20m", ModeCategory.Cw)));
}
