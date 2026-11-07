using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Rbn;

/// <summary>A stream let in (<see cref="Lease"/>, dispose it at the end), or the error code and how long to wait.</summary>
public sealed record StreamAdmission(IDisposable? Lease, string? Error, TimeSpan? RetryAfter);

/// <summary>A spot of the stream v2 (plan F71, 3.2): read by the server, with the skimmer's true position.</summary>
public sealed record RbnSpotEvent(
    string Skimmer,
    string Call,
    double Khz,
    int SnrDb,
    int? Wpm,
    string Kind,
    string? Band,
    string RbnTime,
    DateTimeOffset ReceivedAt,
    SkimmerPosition? Position,
    int? Dxcc,
    string? Continent);

/// <summary>
/// GET /v1/rbn/{callsign}: the coverage test (no telnet in a browser; F71: the
/// phone too). The RBN lines of the user's station, matched as the app does: the
/// base call of the spot equals the base call asked (any portable prefix or
/// suffix). At most <see cref="MaxStreamsPerUser"/> streams per user at once.
/// </summary>
public sealed class WatchRbn(IRbnFeed feed, LocateSkimmer locate)
{
    public const int MaxStreamsPerUser = 2;

    /// <summary>Anonymous users of one client address (<see cref="Proxy.AddressKey"/>): a new anonymous user per stream gets no more.</summary>
    public const int MaxAnonymousStreamsPerAddress = 4;

    /// <summary>All the anonymous streams of the server together; accounts are not counted in it.</summary>
    public const int MaxAnonymousStreams = 300;

    /// <summary>What a refused client is told to wait (Retry-After).</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, int> users = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> addresses = new(StringComparer.Ordinal);
    private int anonymous;
    private readonly Lock gate = new();

    /// <summary>
    /// A place for one more stream (dispose its lease at the end), or why not:
    /// "too_many_streams" (2 per user; for anonymous users also 4 per client
    /// address) or "busy" (300 anonymous streams on the server).
    /// </summary>
    public StreamAdmission TryStart(string userId, bool isAnonymous, string address)
    {
        lock (gate)
        {
            if (users.GetValueOrDefault(userId) >= MaxStreamsPerUser) return new(null, "too_many_streams", RetryAfter);
            if (isAnonymous && addresses.GetValueOrDefault(address) >= MaxAnonymousStreamsPerAddress) return new(null, "too_many_streams", RetryAfter);
            if (isAnonymous && anonymous >= MaxAnonymousStreams) return new(null, "busy", RetryAfter);
            users[userId] = users.GetValueOrDefault(userId) + 1;
            if (isAnonymous)
            {
                addresses[address] = addresses.GetValueOrDefault(address) + 1;
                anonymous++;
            }
        }
        return new(new Lease(this, userId, isAnonymous ? address : null), null, null);
    }

    /// <summary>The RBN lines of <paramref name="callsign"/>'s station from now on.</summary>
    public async IAsyncEnumerable<RbnLine> WatchAsync(string callsign, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var station = Callsign.Base(callsign);
        await foreach (var line in feed.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Callsign.Base(line.Call) == station) yield return line;
        }
    }

    /// <summary>The state of the shared RBN connection.</summary>
    public RbnFeedStatus Status => feed.Status;

    /// <summary>Completes at the next change of <see cref="Status"/>.</summary>
    public Task WaitForChangeAsync(CancellationToken cancellationToken) => feed.WaitForChangeAsync(cancellationToken);

    /// <summary>The v2 event of a line: its fields, its band, where the skimmer is.</summary>
    public RbnSpotEvent Describe(RbnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var s = line.Spot;
        var skimmer = locate.Locate(s.Skimmer);
        return new RbnSpotEvent(s.Skimmer, s.Call, s.Khz, s.SnrDb, s.Wpm, s.Kind, Bands.Of(s.Khz), s.RbnTime, line.ReceivedAt, skimmer.Position, skimmer.Dxcc, skimmer.Continent);
    }

    private void End(string userId, string? anonymousAddress)
    {
        lock (gate)
        {
            Release(users, userId);
            if (anonymousAddress is null) return;
            Release(addresses, anonymousAddress);
            anonymous--;
        }
    }

    private static void Release(Dictionary<string, int> counts, string key)
    {
        if (counts.GetValueOrDefault(key) <= 1) counts.Remove(key);
        else counts[key]--;
    }

    private sealed class Lease(WatchRbn owner, string userId, string? anonymousAddress) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.End(userId, anonymousAddress);
        }
    }
}
