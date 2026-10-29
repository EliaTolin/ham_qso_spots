using HamQso.Spots.Domain.Spotting;

namespace HamQso.Spots.Domain.Tests.Spotting;

public class ClusterLoginTests
{
    [Theory]
    [InlineData("IU4VRB", 7, "IU4VRB-7")]
    [InlineData(" iu4vrb ", 1, "IU4VRB-1")]
    [InlineData("IU4VRB/P", 99, "IU4VRB/P-99")]
    public void The_login_is_the_profile_callsign_with_the_SSID(string callsign, int ssid, string login) =>
        Assert.Equal(login, ClusterLogin.Create(callsign, ssid));

    [Theory]
    [InlineData(1, true)]
    [InlineData(99, true)]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(-7, false)]
    [InlineData(null, false)]
    public void The_SSID_goes_from_1_to_99(int? ssid, bool valid) => Assert.Equal(valid, ClusterLogin.IsValidSsid(ssid));

    [Theory]
    [InlineData("IU4VRB", "IU4VRB")]
    [InlineData(" iu4vrb ", "IU4VRB")]
    [InlineData("EA8/DL1ABC/P", "EA8/DL1ABC/P")]
    [InlineData("ABCDEFGH", "ABCDEFGH")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("ABCDEFGHI", null)]
    [InlineData("IU4VRB-7", null)]
    [InlineData("IU4VRB\r\nSET/PASSWORD", null)]
    // Trimmed, as the database trigger does: the newline never reaches the login.
    [InlineData("IU4VRB\n", "IU4VRB")]
    [InlineData("A/B/C/D", null)]
    [InlineData("IU4VRB/", null)]
    [InlineData("IU 4VRB", null)]
    [InlineData("IU4VRB\u00FF", null)]
    public void Only_a_callsign_of_the_profiles_pattern_becomes_a_login(string? callsign, string? normalized) =>
        Assert.Equal(normalized, ClusterLogin.NormalizeCallsign(callsign));

    [Fact]
    public void A_login_is_never_made_with_a_wrong_SSID_or_callsign()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClusterLogin.Create("IU4VRB", 0));
        Assert.Throws<ArgumentException>(() => ClusterLogin.Create(" ", 7));
        Assert.Throws<ArgumentException>(() => ClusterLogin.Create("IU4VRB\r\nBYE", 7));
    }
}
