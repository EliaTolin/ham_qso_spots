using System.Net;
using HamQso.Spots.Application.Proxy;

namespace HamQso.Spots.Application.Tests.Proxy;

public class AddressKeyTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    [InlineData("::1", "::/64")]
    public void IPv4_counts_by_address_and_IPv6_by_its_64(string address, string key) =>
        Assert.Equal(key, AddressKey.Of(IPAddress.Parse(address)));

    [Fact]
    public void No_address_is_one_key() => Assert.Equal("unknown", AddressKey.Of(null));
}
