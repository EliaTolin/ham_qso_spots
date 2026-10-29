using HamQso.Spots.Application.Limits;

namespace HamQso.Spots.Application.Proxy;

public enum ProxyAdmission
{
    Allowed,

    /// <summary>This user, or this client address, made too many calls: 429.</summary>
    RateLimited,

    /// <summary>The whole server made too many calls: 503.</summary>
    Busy,
}

/// <summary>
/// Proxy calls, counted after authentication: 120 a minute per user, 300 a
/// minute per client address (anonymous Supabase users are free to create, so
/// a new user per call must not get around the user limit) and 3000 a minute
/// for the whole server. Every call that gets past a limit counts for it, even
/// if a later one refuses it.
/// </summary>
public sealed class ProxyRateLimiter(TimeProvider time)
{
    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> UserLimits = [(120, TimeSpan.FromMinutes(1))];

    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> AddressLimits = [(300, TimeSpan.FromMinutes(1))];

    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> ServerLimits = [(3000, TimeSpan.FromMinutes(1))];

    private const string ServerKey = "";

    private readonly SlidingWindowLimiter users = new(time, UserLimits);
    private readonly SlidingWindowLimiter addresses = new(time, AddressLimits);
    private readonly SlidingWindowLimiter server = new(time, ServerLimits);

    /// <summary>Counts the call; when refused, why and how long until it would pass.</summary>
    public (ProxyAdmission Admission, TimeSpan? RetryAfter) TryAcquire(string userId, string clientAddress)
    {
        if (users.TryAcquire(userId) is { } userWait) return (ProxyAdmission.RateLimited, userWait);
        if (addresses.TryAcquire(clientAddress) is { } addressWait) return (ProxyAdmission.RateLimited, addressWait);
        if (server.TryAcquire(ServerKey) is { } serverWait) return (ProxyAdmission.Busy, serverWait);
        return (ProxyAdmission.Allowed, null);
    }
}
