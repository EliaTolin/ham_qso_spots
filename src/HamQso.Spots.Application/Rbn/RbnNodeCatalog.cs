using System.Collections.Frozen;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Rbn;

/// <summary>What the clients are told about the node list: last good read, nodes in it, stale.</summary>
public sealed record RbnNodesInfo(DateTimeOffset? FetchedAt, int Count, bool Stale);

/// <summary>
/// The node list in use: the last good read. Stale when that read is older
/// than <see cref="StaleAfter"/>, when there is none, or when the last read
/// found the page changed (plan F71, 3.1).
/// </summary>
public sealed class RbnNodeCatalog
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    private volatile Snapshot current = new(FrozenDictionary<string, RbnNode>.Empty, null, false);

    /// <summary>The node of a skimmer (call without "-#"), or null.</summary>
    public RbnNode? Find(string skimmer) => current.Nodes.GetValueOrDefault(skimmer.ToUpperInvariant());

    public RbnNodesInfo Info(DateTimeOffset now)
    {
        var s = current;
        var stale = s.FetchedAt is not { } at || now - at > StaleAfter || s.FormatChanged;
        return new RbnNodesInfo(s.FetchedAt, s.Nodes.Count, stale);
    }

    /// <summary>A good read: used from now on.</summary>
    public void Set(IEnumerable<RbnNode> nodes, DateTimeOffset fetchedAt)
    {
        var byCall = new Dictionary<string, RbnNode>(StringComparer.Ordinal);
        foreach (var n in nodes) byCall.TryAdd(n.Call.ToUpperInvariant(), n);
        current = new Snapshot(byCall.ToFrozenDictionary(StringComparer.Ordinal), fetchedAt, false);
    }

    /// <summary>The page changed: the last good read stays, marked stale.</summary>
    public void MarkFormatChanged() => current = current with { FormatChanged = true };

    private sealed record Snapshot(FrozenDictionary<string, RbnNode> Nodes, DateTimeOffset? FetchedAt, bool FormatChanged);
}
