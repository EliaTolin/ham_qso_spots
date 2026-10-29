using HamQso.Spots.Application.Limits;

namespace HamQso.Spots.Application.Proxy;

/// <summary>Proxy calls per user: 120 a minute, enough for a log upload or a SOTA session, not for a crawler.</summary>
public sealed class ProxyRateLimiter(TimeProvider time)
{
    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> Limits =
    [
        (120, TimeSpan.FromMinutes(1)),
    ];

    private readonly SlidingWindowLimiter users = new(time, Limits);

    /// <summary>Counts the call when allowed (null); otherwise how long to wait.</summary>
    public TimeSpan? TryAcquire(string userId) => users.TryAcquire(userId);
}
