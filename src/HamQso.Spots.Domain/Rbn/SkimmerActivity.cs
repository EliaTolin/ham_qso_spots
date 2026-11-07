namespace HamQso.Spots.Domain.Rbn;

/// <summary>The spots a skimmer sent on a band in one minute, and the time of the last one.</summary>
public sealed record ActivityMinute(string Skimmer, string Band, DateTimeOffset Minute, int Spots, DateTimeOffset LastSpotAt);

/// <summary>A skimmer listening on a band in a window: its spots there and the last one.</summary>
public sealed record ListeningSkimmer(string Call, int Spots, DateTimeOffset LastSpotAt);

/// <summary>The time span in which a skimmer counts as listening.</summary>
public sealed record ActivityWindow(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Which skimmers listen on a band (plan F71, section 4, the only place of
/// this rule): at least one spot on that band from <see cref="Lead"/> before
/// the start of the test to its end. Activity is kept per minute for
/// <see cref="History"/>.
/// </summary>
public static class SkimmerActivity
{
    /// <summary>How long before the start of the test a spot still counts.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(10);

    /// <summary>How far back the activity is kept and can be asked.</summary>
    public static readonly TimeSpan History = TimeSpan.FromHours(48);

    /// <summary>A client clock this far ahead of the server is accepted.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The window for a test from <paramref name="from"/> to <paramref name="to"/>
    /// (both or neither; neither = the last 10 minutes), or null when not valid:
    /// only one of them, from after to, in the future, or older than 48 hours.
    /// </summary>
    public static ActivityWindow? Window(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        if (from is null && to is null) return new ActivityWindow(now - Lead, now);
        if (from is not { } f || to is not { } t) return null;
        if (f > t || t > now + ClockSkew || f - Lead < now - History) return null;
        return new ActivityWindow(f - Lead, t);
    }

    /// <summary>The start of the minute of <paramref name="moment"/> (UTC).</summary>
    public static DateTimeOffset MinuteOf(DateTimeOffset moment)
    {
        var utc = moment.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// The skimmers with spots on <paramref name="band"/> in the minutes of the
    /// window (the minute of its start included), most spots first, then by call.
    /// </summary>
    public static IReadOnlyList<ListeningSkimmer> Listening(IEnumerable<ActivityMinute> minutes, string band, ActivityWindow window)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ArgumentNullException.ThrowIfNull(window);
        var first = MinuteOf(window.From);
        return [.. minutes
            .Where(m => m.Band == band && m.Minute >= first && m.Minute <= window.To && m.Spots > 0)
            .GroupBy(m => m.Skimmer, StringComparer.Ordinal)
            .Select(g => new ListeningSkimmer(g.Key, g.Sum(m => m.Spots), g.Max(m => m.LastSpotAt)))
            .OrderByDescending(s => s.Spots)
            .ThenBy(s => s.Call, StringComparer.Ordinal)];
    }
}
