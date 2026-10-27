using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Tests.Radio;

public class BandsTests
{
    [Theory]
    [InlineData(1800, "160m")]
    [InlineData(14025.5, "20m")]
    [InlineData(29700, "10m")]
    [InlineData(144300, "2m")]
    [InlineData(432200, "70cm")]
    [InlineData(13999.9, null)]
    [InlineData(0, null)]
    public void Tells_the_band_of_a_frequency(double khz, string? expected) => Assert.Equal(expected, Bands.Of(khz));
}
