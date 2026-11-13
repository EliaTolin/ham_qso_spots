using System.Globalization;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Domain.Protection;

/// <summary>
/// The rule "never bulk" (F107): the hearings of a protected callsign are
/// gathered per callsign, band and minute (never per signal: a spot, a skimmer
/// and a PSKReporter report of the same minute are one use), and leave as one
/// use per minute. The use carries the most significant signal of that minute
/// (spot, then skimmer, then PSKReporter) and, in <c>raw.by_signal</c>, how many
/// receivers each signal had. A FT8 signal heard by 100 receivers is one use
/// with the number of receivers and the first <see cref="MaxListed"/> of them,
/// not 100 rows. The same skimmer heard by the RBN and by a cluster is counted
/// once. Memory is bounded: at most <see cref="MaxReceivers"/> distinct receivers
/// per signal of a use are counted, at most <see cref="MaxOpen"/> uses are open at once.
/// </summary>
public sealed class UseAggregator
{
    public const int MaxListed = 10;
    public const int MaxReceivers = 500;
    public const int MaxOpen = 5000;

    private readonly Lock gate = new();
    private readonly Dictionary<(string Base, string Band, DateTimeOffset Minute), Bucket> open = [];

    /// <summary>Adds a hearing of <paramref name="baseCallsign"/>. False when too many uses are open (dropped).</summary>
    public bool Add(string baseCallsign, Hearing hearing)
    {
        ArgumentNullException.ThrowIfNull(hearing);
        var key = (baseCallsign, hearing.Band, SkimmerActivity.MinuteOf(hearing.At));
        lock (gate)
        {
            if (!open.TryGetValue(key, out var bucket))
            {
                if (open.Count >= MaxOpen) return false;
                open[key] = bucket = new Bucket();
            }
            bucket.Add(hearing);
            return true;
        }
    }

    /// <summary>The uses of the minutes that are over at <paramref name="now"/> (all of them with <paramref name="everything"/>), oldest first.</summary>
    public IReadOnlyList<ProtectedUse> Drain(DateTimeOffset now, bool everything = false)
    {
        var current = SkimmerActivity.MinuteOf(now);
        List<KeyValuePair<(string Base, string Band, DateTimeOffset Minute), Bucket>> closed;
        lock (gate)
        {
            closed = [.. open.Where(kv => everything || kv.Key.Minute < current)];
            foreach (var kv in closed) open.Remove(kv.Key);
        }
        return [.. closed.OrderBy(kv => kv.Key.Minute).ThenBy(kv => kv.Key.Base, StringComparer.Ordinal).ThenBy(kv => kv.Key.Band, StringComparer.Ordinal)
            .Select(kv => kv.Value.ToUse(kv.Key.Base, kv.Key.Minute))];
    }

    private sealed class Bucket
    {
        private readonly Dictionary<ProtectionSignal, Group> groups = [];

        public void Add(Hearing h)
        {
            if (!groups.TryGetValue(h.Signal, out var group)) groups[h.Signal] = group = new Group(h);
            group.Add(h);
        }

        public ProtectedUse ToUse(string baseCallsign, DateTimeOffset minute)
        {
            // Spot before Skimmer before PskReporter: the enum order is the significance.
            var best = groups.OrderBy(g => g.Key).First();
            var first = best.Value.First;
            var raw = best.Value.ToRaw();
            raw["by_signal"] = groups.OrderBy(g => g.Key).ToDictionary(
                g => g.Key.Wire(),
                g => (object?)new Dictionary<string, object?> { ["reports"] = g.Value.Reports, ["receivers"] = g.Value.Receivers });
            return new ProtectedUse(
                first.Callsign,
                best.Key,
                first.At,
                first.Band,
                first.FrequencyKhz,
                first.Mode,
                first.HeardBy,
                first.HeardLocator,
                raw,
                string.Create(CultureInfo.InvariantCulture, $"{best.Key.Wire()}:{baseCallsign}:{first.Band}:{minute.ToUnixTimeSeconds()}"));
        }
    }

    private sealed class Group(Hearing first)
    {
        private readonly List<Hearing> listed = [];
        private readonly HashSet<string> receivers = new(StringComparer.Ordinal);

        public Hearing First { get; } = first;

        public int Reports { get; private set; }

        public int Receivers => receivers.Count;

        public void Add(Hearing h)
        {
            Reports++;
            var by = h.HeardBy.ToUpperInvariant();
            if (receivers.Count >= MaxReceivers && !receivers.Contains(by)) return;
            if (receivers.Add(by) && listed.Count < MaxListed) listed.Add(h);
        }

        public Dictionary<string, object?> ToRaw() => new()
        {
            ["reports"] = Reports,
            ["receivers"] = receivers.Count,
            ["heard"] = listed.Select(h => new Dictionary<string, object?>
            {
                ["by"] = h.HeardBy,
                ["locator"] = h.HeardLocator,
                ["snr"] = h.SnrDb,
            }).ToArray(),
        };
    }
}
