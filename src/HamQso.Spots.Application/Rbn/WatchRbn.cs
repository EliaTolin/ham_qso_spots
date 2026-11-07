using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Rbn;

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

    private readonly Dictionary<string, int> streams = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <summary>A place for one more stream of the user (dispose it at the end), or null when they have the maximum.</summary>
    public IDisposable? TryStart(string userId)
    {
        lock (gate)
        {
            var open = streams.GetValueOrDefault(userId);
            if (open >= MaxStreamsPerUser) return null;
            streams[userId] = open + 1;
        }
        return new Lease(this, userId);
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

    private void End(string userId)
    {
        lock (gate)
        {
            if (streams.GetValueOrDefault(userId) <= 1) streams.Remove(userId);
            else streams[userId]--;
        }
    }

    private sealed class Lease(WatchRbn owner, string userId) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.End(userId);
        }
    }
}
