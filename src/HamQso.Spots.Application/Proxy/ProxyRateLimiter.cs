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
/// minute per client address (<see cref="AddressKey"/>; anonymous Supabase
/// users are free to create, so a new user per call must not get around the
/// user limit), and a server budget split in two so anonymous traffic cannot
/// starve real accounts: 2000 a minute for users with an account, 1000 for
/// anonymous users. Every call that gets past a limit counts for it, even if a
/// later one refuses it.
/// </summary>
public sealed class ProxyRateLimiter(TimeProvider time)
{
    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> UserLimits = [(120, TimeSpan.FromMinutes(1))];

    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> AddressLimits = [(300, TimeSpan.FromMinutes(1))];

    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> AccountServerLimits = [(2000, TimeSpan.FromMinutes(1))];

    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> AnonymousServerLimits = [(1000, TimeSpan.FromMinutes(1))];

    private const string ServerKey = "";

    private readonly SlidingWindowLimiter users = new(time, UserLimits);
    private readonly SlidingWindowLimiter addresses = new(time, AddressLimits);
    private readonly SlidingWindowLimiter accounts = new(time, AccountServerLimits);
    private readonly SlidingWindowLimiter anonymous = new(time, AnonymousServerLimits);

    /// <summary>Counts the call; when refused, why and how long until it would pass.</summary>
    public (ProxyAdmission Admission, TimeSpan? RetryAfter) TryAcquire(string userId, bool isAnonymous, string clientAddress)
    {
        if (users.TryAcquire(userId) is { } userWait) return (ProxyAdmission.RateLimited, userWait);
        if (addresses.TryAcquire(clientAddress) is { } addressWait) return (ProxyAdmission.RateLimited, addressWait);
        if ((isAnonymous ? anonymous : accounts).TryAcquire(ServerKey) is { } serverWait) return (ProxyAdmission.Busy, serverWait);
        return (ProxyAdmission.Allowed, null);
    }
}
