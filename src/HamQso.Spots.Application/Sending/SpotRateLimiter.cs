namespace HamQso.Spots.Application.Sending;

public enum RateDecision
{
    Allowed,

    /// <summary>The user is over <see cref="SpotRateLimiter.UserLimits"/>.</summary>
    UserLimited,

    /// <summary>The whole server is over <see cref="SpotRateLimiter.ServerLimits"/>.</summary>
    ServerBusy,
}

/// <summary>
/// How many spots go out, over sliding windows: per user 3 a minute and 20 an
/// hour, for the whole server 60 a minute (so many users together cannot flood
/// the node). Every attempt that gets this far counts, sent or not, so a user
/// cannot hammer the node or Supabase by failing; an attempt refused here does
/// not count. Not per callsign: profile callsigns are not verified, so anyone
/// could use up someone else's.
/// </summary>
public sealed class SpotRateLimiter(TimeProvider time)
{
    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> UserLimits =
    [
        (3, TimeSpan.FromMinutes(1)),
        (20, TimeSpan.FromHours(1)),
    ];

    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> ServerLimits =
    [
        (60, TimeSpan.FromMinutes(1)),
    ];

    private static readonly TimeSpan LongestUserWindow = UserLimits.Max(l => l.Window);
    private static readonly TimeSpan LongestServerWindow = ServerLimits.Max(l => l.Window);

    private readonly Dictionary<string, List<DateTimeOffset>> attempts = new(StringComparer.Ordinal);
    private readonly List<DateTimeOffset> server = [];
    private readonly Lock gate = new();
    private DateTimeOffset lastSweep = DateTimeOffset.MinValue;

    /// <summary>Counts the attempt when allowed; otherwise why not and, for the user limit, how long to wait.</summary>
    public (RateDecision Decision, TimeSpan? RetryAfter) TryAcquire(string userId)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            if (!attempts.TryGetValue(userId, out var times)) attempts[userId] = times = [];
            times.RemoveAll(t => t <= now - LongestUserWindow);
            if (RetryAfter(UserLimits, times, now) is { } wait) return (RateDecision.UserLimited, wait);
            server.RemoveAll(t => t <= now - LongestServerWindow);
            if (RetryAfter(ServerLimits, server, now) is { } busy) return (RateDecision.ServerBusy, busy);
            times.Add(now);
            server.Add(now);
            return (RateDecision.Allowed, null);
        }
    }

    /// <summary>The longest wait among the exceeded limits: when the oldest attempt of the window leaves it.</summary>
    public static TimeSpan? RetryAfter(IReadOnlyList<(int Count, TimeSpan Window)> limits, IReadOnlyList<DateTimeOffset> times, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(times);
        TimeSpan? wait = null;
        foreach (var (count, window) in limits)
        {
            var inWindow = times.Where(t => t > now - window).Order().ToArray();
            if (inWindow.Length < count) continue;
            var free = inWindow[^count] + window - now;
            if (wait is null || free > wait) wait = free;
        }
        return wait;
    }

    /// <summary>Forgets users with no attempt in the longest window, at most once per window.</summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - lastSweep < LongestUserWindow) return;
        lastSweep = now;
        foreach (var user in attempts.Where(a => a.Value.All(t => t <= now - LongestUserWindow)).Select(a => a.Key).ToArray())
        {
            attempts.Remove(user);
        }
    }
}
