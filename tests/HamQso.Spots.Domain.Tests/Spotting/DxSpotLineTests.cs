using HamQso.Spots.Domain.Spotting;

namespace HamQso.Spots.Domain.Tests.Spotting;

public class DxSpotLineTests
{
    [Theory]
    [InlineData("DX 14025.0 K1ABC CQ CW")]
    [InlineData("DX 14025.0 K1ABC")]
    [InlineData("DX 7074.0 IU4VRB/P US-2907 FT8 tnx QSO")]
    [InlineData("DX 144300.0 EA8/DL1ABC/P 59 è grazie")]
    [InlineData("DX 1840.5 ve3xyz cq")]
    public void A_DX_line_as_the_app_builds_it_is_valid(string line) => Assert.True(DxSpotLine.IsValid(line));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SH/DX 50")]
    [InlineData("dx 14025.0 K1ABC CQ")]
    [InlineData("DX 14025 K1ABC CQ")]
    [InlineData("DX 14025.00 K1ABC CQ")]
    [InlineData("DX 0.0 K1ABC CQ")]
    [InlineData("DX 14025.0 K1-ABC CQ")]
    [InlineData("DX 14025.0  K1ABC CQ")]
    [InlineData("DX 14025.0 K1ABC CQ\n")]
    [InlineData("DX 14025.0 K1ABC CQ\r\nSET/PASSWORD x")]
    [InlineData("DX 14025.0 K1ABC CQ\u0007")]
    [InlineData("DX 14025.0 K1ABC \tCQ")]
    [InlineData(" DX 14025.0 K1ABC CQ")]
    [InlineData("DX 14025.0 K1ABC CQ €")]
    public void Anything_else_is_not(string? line) => Assert.False(DxSpotLine.IsValid(line));

    [Fact]
    public void A_line_longer_than_the_maximum_is_not_valid()
    {
        var prefix = "DX 14025.0 K1ABC ";
        Assert.True(DxSpotLine.IsValid(prefix + new string('x', DxSpotLine.MaxLength - prefix.Length)));
        Assert.False(DxSpotLine.IsValid(prefix + new string('x', DxSpotLine.MaxLength - prefix.Length + 1)));
    }
}
