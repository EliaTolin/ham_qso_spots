using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Callsigns;

namespace HamQso.Spots.Application.Rbn;

/// <summary>
/// GET /v1/rbn/{callsign}: the coverage test of the web app (no telnet in a
/// browser). The RBN lines of the user's station, matched as the app does: the
/// base call of the spot equals the base call asked (any portable prefix or
/// suffix). At most <see cref="MaxStreamsPerUser"/> streams per user at once.
/// </summary>
public sealed class WatchRbn(IRbnFeed feed)
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
