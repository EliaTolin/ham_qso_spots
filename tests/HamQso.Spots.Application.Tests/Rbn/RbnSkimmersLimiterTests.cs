using HamQso.Spots.Application.Proxy;
using HamQso.Spots.Application.Rbn;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Rbn;

/// <summary>
/// GET /v1/rbn/skimmers (security review F71): the proxy's limits per user and
/// the proxy's budget for all anonymous users. No limit per address: the forecast
/// function calls from Supabase, so every user would share its address.
/// </summary>
public class RbnSkimmersLimiterTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_user_has_the_proxy_limit_per_minute()
    {
        var limiter = new RbnSkimmersLimiter(time);
        var count = ProxyRateLimiter.UserLimits[0].Count;
        for (var i = 0; i < count; i++) Assert.Equal(ProxyAdmission.Allowed, limiter.TryAcquire("u", true).Admission);

        var (admission, wait) = limiter.TryAcquire("u", true);

        Assert.Equal(ProxyAdmission.RateLimited, admission);
        Assert.Equal(TimeSpan.FromMinutes(1), wait);
        Assert.Equal(ProxyAdmission.Allowed, limiter.TryAcquire("v", true).Admission);
    }

    [Fact]
    public void All_anonymous_users_together_have_the_proxy_budget_accounts_do_not_count_in_it()
    {
        var limiter = new RbnSkimmersLimiter(time);
        var budget = ProxyRateLimiter.AnonymousServerLimits[0].Count;
        for (var i = 0; i < budget; i++) Assert.Equal(ProxyAdmission.Allowed, limiter.TryAcquire($"anon{i}", true).Admission);

        Assert.Equal(ProxyAdmission.Busy, limiter.TryAcquire("anon-new", true).Admission);
        Assert.Equal(ProxyAdmission.Allowed, limiter.TryAcquire("account", false).Admission);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ProxyAdmission.Allowed, limiter.TryAcquire("anon-new", true).Admission);
    }
}
