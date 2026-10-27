using HamQso.Spots.Infrastructure.Sources.Telnet;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Telnet;

public class TelnetSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_node_that_never_sends_a_newline_does_not_grow_the_buffer_forever()
    {
        var session = new TelnetSession(new TelnetOptions(), Now);
        var chunk = new string('x', 4096);

        for (var i = 0; i < 1000; i++) session.OnText(chunk, "DXFun", Now);

        Assert.InRange(session.PendingLength, 0, TelnetSession.MaxPendingChars);
    }

    [Fact]
    public void Spots_still_arrive_after_an_overlong_line()
    {
        var session = new TelnetSession(new TelnetOptions(), Now);
        session.OnText(new string('x', TelnetSession.MaxPendingChars * 2), "DXFun", Now);

        var (spots, _) = session.OnText("\r\nDX de IK2ABC:     7074.0  F4LHL  ft8  1159Z\r\n", "DXFun", Now);

        Assert.Equal("F4LHL", Assert.Single(spots).Callsign);
    }
}
