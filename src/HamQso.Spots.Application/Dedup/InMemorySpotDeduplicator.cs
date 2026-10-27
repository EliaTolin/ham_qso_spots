using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Dedup;

/// <summary>
/// Dedup in the process memory, reloaded from the archive at startup. Entries
/// older than twice the window are forgotten. Not thread safe: the ingestion
/// handles one batch at a time.
/// </summary>
public sealed class InMemorySpotDeduplicator(TimeProvider time) : ISpotDeduplicator
{
    private readonly Dictionary<string, List<Spot>> byKey = [];
    private DateTimeOffset lastSweep = DateTimeOffset.MinValue;

    public void Seed(IEnumerable<Spot> spots)
    {
        foreach (var spot in spots) Accept(spot);
    }

    public DedupResult Accept(Spot spot)
    {
        Sweep();
        var key = DuplicateRule.KeyOf(spot);
        if (!byKey.TryGetValue(key, out var seen))
        {
            byKey[key] = [spot];
            return new DedupResult(DedupOutcome.New, spot);
        }
        var index = seen.FindIndex(s => DuplicateRule.IsDuplicate(s, spot));
        if (index < 0)
        {
            seen.Add(spot);
            return new DedupResult(DedupOutcome.New, spot);
        }
        var merged = DuplicateRule.Merge(seen[index], spot);
        if (SameContent(seen[index], merged)) return new DedupResult(DedupOutcome.Unchanged, seen[index]);
        seen[index] = merged;
        return new DedupResult(DedupOutcome.Merged, merged);
    }

    private static bool SameContent(Spot a, Spot b) =>
        a == b with { Feeds = a.Feeds, Spotters = a.Spotters }
        && a.Feeds.SequenceEqual(b.Feeds)
        && a.Spotters.SequenceEqual(b.Spotters);

    private void Sweep()
    {
        var now = time.GetUtcNow();
        if (now - lastSweep < DuplicateRule.Window) return;
        lastSweep = now;
        var horizon = now - (2 * DuplicateRule.Window);
        foreach (var (key, seen) in byKey.ToArray())
        {
            seen.RemoveAll(s => s.SpotTime < horizon);
            if (seen.Count == 0) byKey.Remove(key);
        }
    }
}
