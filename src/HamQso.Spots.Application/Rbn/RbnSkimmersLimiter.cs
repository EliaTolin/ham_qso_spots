using HamQso.Spots.Application.Limits;
using HamQso.Spots.Application.Proxy;

namespace HamQso.Spots.Application.Rbn;

/// <summary>
/// GET /v1/rbn/skimmers (security review F71): the proxy's values, per user
/// (<see cref="ProxyRateLimiter.UserLimits"/>) and for all anonymous users
/// together (<see cref="ProxyRateLimiter.AnonymousServerLimits"/>). No limit per
/// client address: the forecast function of Supabase calls with the user's token
/// from Supabase's addresses, which all users share.
/// </summary>
public sealed class RbnSkimmersLimiter(TimeProvider time)
{
    private const string ServerKey = "";

    private readonly SlidingWindowLimiter users = new(time, ProxyRateLimiter.UserLimits);
    private readonly SlidingWindowLimiter anonymous = new(time, ProxyRateLimiter.AnonymousServerLimits);

    public (ProxyAdmission Admission, TimeSpan? RetryAfter) TryAcquire(string userId, bool isAnonymous)
    {
        if (users.TryAcquire(userId) is { } userWait) return (ProxyAdmission.RateLimited, userWait);
        if (isAnonymous && anonymous.TryAcquire(ServerKey) is { } serverWait) return (ProxyAdmission.Busy, serverWait);
        return (ProxyAdmission.Allowed, null);
    }
}
