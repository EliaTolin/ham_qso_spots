using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ingestion;

/// <summary>
/// The last <c>capacity</c> DX spots, split by topic: what the retained
/// messages on spots/dx/&lt;band&gt;/&lt;mode&gt; contain.
/// </summary>
public sealed class RecentDxSpots(int capacity)
{
    private readonly Dictionary<string, Spot> byKey = [];

    /// <summary>Adds or replaces spots (by key); returns the topics whose list changed.</summary>
    public IReadOnlySet<string> Apply(IEnumerable<Spot> spots)
    {
        var touched = new HashSet<string>();
        foreach (var spot in spots)
        {
            if (byKey.TryGetValue(spot.SpotKey, out var old)) touched.Add(SpotTopics.Of(old));
            byKey[spot.SpotKey] = spot;
            touched.Add(SpotTopics.Of(spot));
        }
        foreach (var dropped in byKey.Values.OrderByDescending(s => s.SpotTime).Skip(capacity).ToArray())
        {
            byKey.Remove(dropped.SpotKey);
            touched.Add(SpotTopics.Of(dropped));
        }
        return touched;
    }

    /// <summary>The spots of a topic, newest first.</summary>
    public IReadOnlyList<Spot> ListOf(string topic) =>
        byKey.Values.Where(s => SpotTopics.Of(s) == topic).OrderByDescending(s => s.SpotTime).ToArray();
}
