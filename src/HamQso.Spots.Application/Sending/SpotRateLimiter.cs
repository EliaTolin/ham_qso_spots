namespace HamQso.Spots.Application.Sending;

/// <summary>
/// At most <see cref="Limits"/> spots per user: 3 a minute and 20 an hour,
/// counted over sliding windows. Every attempt that gets this far counts,
/// sent or not, so a user cannot hammer the node or Supabase by failing.
/// </summary>
public sealed class SpotRateLimiter(TimeProvider time)
{
    public static readonly IReadOnlyList<(int Count, TimeSpan Window)> Limits =
    [
        (3, TimeSpan.FromMinutes(1)),
        (20, TimeSpan.FromHours(1)),
    ];

    private static readonly TimeSpan LongestWindow = Limits.Max(l => l.Window);

    private readonly Dictionary<string, List<DateTimeOffset>> attempts = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private DateTimeOffset lastSweep = DateTimeOffset.MinValue;

    /// <summary>Null when the attempt is allowed (and counted); otherwise how long to wait.</summary>
    public TimeSpan? TryAcquire(string userId)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            if (!attempts.TryGetValue(userId, out var times)) attempts[userId] = times = [];
            times.RemoveAll(t => t <= now - LongestWindow);
            var wait = RetryAfter(times, now);
            if (wait is null) times.Add(now);
            return wait;
        }
    }

    /// <summary>The longest wait among the exceeded limits: when the oldest attempt of the window leaves it.</summary>
    public static TimeSpan? RetryAfter(IReadOnlyList<DateTimeOffset> times, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(times);
        TimeSpan? wait = null;
        foreach (var (count, window) in Limits)
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
        if (now - lastSweep < LongestWindow) return;
        lastSweep = now;
        foreach (var user in attempts.Where(a => a.Value.All(t => t <= now - LongestWindow)).Select(a => a.Key).ToArray())
        {
            attempts.Remove(user);
        }
    }
}
