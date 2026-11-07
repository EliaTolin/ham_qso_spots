using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Rbn;

/// <summary>A skimmer listening on the band, where it is, and its spots in the window.</summary>
public sealed record ListeningSkimmerInfo(SkimmerInfo Skimmer, int Spots, DateTimeOffset LastSpotAt);

/// <summary>The answer of GET /v1/rbn/skimmers.</summary>
public sealed record ListeningSkimmers(string Band, DateTimeOffset From, DateTimeOffset To, RbnFeedStatus Feed, RbnNodesInfo Nodes, IReadOnlyList<ListeningSkimmerInfo> Skimmers);

/// <summary>The answer, or the error code for the client (invalid_band, invalid_window).</summary>
public sealed record ListeningSkimmersResult(ListeningSkimmers? Value, string? Error);

/// <summary>
/// GET /v1/rbn/skimmers (plan F71, 3.1): the skimmers listening on a band in
/// the last 10 minutes, or from 10 minutes before a test to its end (48 hours
/// back at most), each with its true position. A skimmer of unknown position
/// (no node, unknown entity) is left out: no client could place it. The
/// skimmers depend only on the band and the minutes of the window, the same for
/// everyone: they are kept <see cref="CacheFor"/> (security review F71).
/// </summary>
public sealed class GetListeningSkimmers(IRbnActivityStore store, IRbnFeed feed, RbnNodeCatalog catalog, LocateSkimmer locate, TimeProvider time)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly Dictionary<(string Band, DateTimeOffset From, DateTimeOffset To), (DateTimeOffset At, ListeningSkimmerInfo[] Skimmers)> cache = [];
    private readonly Lock gate = new();

    public async Task<ListeningSkimmersResult> ExecuteAsync(string? band, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (band is null || !Bands.Labels.Contains(band, StringComparer.Ordinal)) return new(null, "invalid_band");
        var now = time.GetUtcNow();
        if (SkimmerActivity.Window(from, to, now) is not { } window) return new(null, "invalid_window");

        var skimmers = await SkimmersAsync(band, window, now, cancellationToken).ConfigureAwait(false);
        return new(new ListeningSkimmers(band, window.From, window.To, feed.Status, catalog.Info(now), skimmers), null);
    }

    private async Task<ListeningSkimmerInfo[]> SkimmersAsync(string band, ActivityWindow window, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The rows read are those of the minutes from the window's first to its last: seconds change nothing.
        var key = (band, SkimmerActivity.MinuteOf(window.From), SkimmerActivity.MinuteOf(window.To));
        lock (gate)
        {
            if (cache.TryGetValue(key, out var hit) && now - hit.At < CacheFor) return hit.Skimmers;
        }
        var minutes = await store.ReadAsync(band, key.Item2, window.To, cancellationToken).ConfigureAwait(false);
        var skimmers = SkimmerActivity.Listening(minutes, band, window with { From = key.Item2 })
            .Select(s => new ListeningSkimmerInfo(locate.Locate(s.Call), s.Spots, s.LastSpotAt))
            .Where(s => s.Skimmer.Position is not null)
            .ToArray();
        lock (gate)
        {
            foreach (var old in cache.Where(c => now - c.Value.At >= CacheFor).Select(c => c.Key).ToArray()) cache.Remove(old);
            cache[key] = (now, skimmers);
        }
        return skimmers;
    }
}
