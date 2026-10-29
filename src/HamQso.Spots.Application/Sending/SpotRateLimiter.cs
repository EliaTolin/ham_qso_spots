using HamQso.Spots.Application.Limits;

namespace HamQso.Spots.Application.Sending;

/// <summary>
/// How many spots go out: per user 3 a minute and 20 an hour, for the whole
/// server 60 a minute (so many users together cannot flood the node). Every
/// attempt of a user that gets past the user limit counts, sent or not, so a
/// user cannot hammer the node or Supabase by failing. The server budget counts
/// only spots that passed every other check, so failed or refused attempts
/// cannot use it up for everyone. Not per callsign: profile callsigns are not
/// verified, so anyone could use up someone else's.
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

    private const string ServerKey = "";

    private readonly SlidingWindowLimiter users = new(time, UserLimits);
    private readonly SlidingWindowLimiter server = new(time, ServerLimits);

    /// <summary>Counts the attempt of the user when allowed (null); otherwise how long to wait.</summary>
    public TimeSpan? TryAcquireUser(string userId) => users.TryAcquire(userId);

    /// <summary>
    /// Counts a spot that is about to go out against the server budget when it
    /// fits (null); otherwise how long until it does. Asked last, only for spots
    /// that passed every other check.
    /// </summary>
    public TimeSpan? TryAcquireServer() => server.TryAcquire(ServerKey);
}
