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

    [Fact]
    public void A_login_is_never_made_with_a_wrong_SSID_or_no_callsign()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClusterLogin.Create("IU4VRB", 0));
        Assert.Throws<ArgumentException>(() => ClusterLogin.Create(" ", 7));
    }
}
