using HamQso.Spots.Application.Proxy;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Proxy;

public class ProxyRateLimiterTests
{
    private readonly FakeTimeProvider time = new();
    private readonly ProxyRateLimiter limiter;

    public ProxyRateLimiterTests() => limiter = new ProxyRateLimiter(time);

    private ProxyAdmission Call(string user, string address) => limiter.TryAcquire(user, address).Admission;

    [Fact]
    public void A_user_makes_120_calls_a_minute()
    {
        for (var i = 0; i < 120; i++) Assert.Equal(ProxyAdmission.Allowed, Call("u", $"10.0.0.{i}"));

        Assert.Equal((ProxyAdmission.RateLimited, TimeSpan.FromMinutes(1)), limiter.TryAcquire("u", "10.0.1.1"));
    }

    [Fact]
    public void An_address_makes_300_calls_a_minute_whatever_users_it_creates()
    {
        // A new anonymous user per call must not get around the limit.
        for (var i = 0; i < 300; i++) Assert.Equal(ProxyAdmission.Allowed, Call($"anon-{i}", "203.0.113.7"));

        Assert.Equal(ProxyAdmission.RateLimited, Call("anon-new", "203.0.113.7"));
        Assert.Equal(ProxyAdmission.Allowed, Call("anon-new", "203.0.113.8"));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ProxyAdmission.Allowed, Call("anon-other", "203.0.113.7"));
    }

    [Fact]
    public void The_whole_server_makes_3000_calls_a_minute_then_busy()
    {
        for (var i = 0; i < 3000; i++) Assert.Equal(ProxyAdmission.Allowed, Call($"u-{i}", $"addr-{i}"));

        Assert.Equal((ProxyAdmission.Busy, TimeSpan.FromMinutes(1)), limiter.TryAcquire("u-new", "addr-new"));
    }
}
