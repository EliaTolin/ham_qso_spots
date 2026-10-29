namespace HamQso.Spots.Application.Limits;

/// <summary>
/// At most <c>Count</c> attempts per key in every <c>Window</c> of the limits,
/// over sliding windows, in memory (a restart forgets the counts). An attempt
/// refused here does not count. Keys silent for the longest window are forgotten.
/// </summary>
public sealed class SlidingWindowLimiter(TimeProvider time, IReadOnlyList<(int Count, TimeSpan Window)> limits)
{
    private readonly TimeSpan longestWindow = limits.Max(l => l.Window);
    private readonly Dictionary<string, List<DateTimeOffset>> attempts = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private DateTimeOffset lastSweep = DateTimeOffset.MinValue;

    public IReadOnlyList<(int Count, TimeSpan Window)> Limits => limits;

    /// <summary>Counts the attempt when allowed (null); otherwise how long to wait.</summary>
    public TimeSpan? TryAcquire(string key)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            if (!attempts.TryGetValue(key, out var times)) attempts[key] = times = [];
            times.RemoveAll(t => t <= now - longestWindow);
            var wait = RetryAfter(limits, times, now);
            if (wait is null) times.Add(now);
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

    /// <summary>Forgets keys with no attempt in the longest window, at most once per window.</summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - lastSweep < longestWindow) return;
        lastSweep = now;
        foreach (var key in attempts.Where(a => a.Value.All(t => t <= now - longestWindow)).Select(a => a.Key).ToArray())
        {
            attempts.Remove(key);
        }
    }
}
