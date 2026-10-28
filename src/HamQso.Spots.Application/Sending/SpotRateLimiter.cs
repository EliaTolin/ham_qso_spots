namespace HamQso.Spots.Application.Sending;

/// <summary>
/// How many spots go out, over sliding windows: per user 3 a minute and 20 an
/// hour, for the whole server 60 a minute (so many users together cannot flood
/// the node). Every attempt of a user that gets this far counts, sent or not,
/// so a user cannot hammer the node or Supabase by failing; an attempt refused
/// here does not count. The server budget counts only spots that passed every
/// other check, so failed or refused attempts cannot use it up for everyone. Not per callsign: profile callsigns are not verified, so anyone
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

    /// <summary>Counts the attempt of the user when allowed (null); otherwise how long to wait.</summary>
    public TimeSpan? TryAcquireUser(string userId)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            if (!attempts.TryGetValue(userId, out var times)) attempts[userId] = times = [];
            times.RemoveAll(t => t <= now - LongestUserWindow);
            var wait = RetryAfter(UserLimits, times, now);
            if (wait is null) times.Add(now);
            return wait;
        }
    }

    /// <summary>
    /// Counts a spot that is about to go out against the server budget when it
    /// fits (null); otherwise how long until it does. Asked last, only for spots
    /// that passed every other check, so refused attempts never use the budget.
    /// </summary>
    public TimeSpan? TryAcquireServer()
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            server.RemoveAll(t => t <= now - LongestServerWindow);
            var wait = RetryAfter(ServerLimits, server, now);
            if (wait is null) server.Add(now);
            return wait;
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
