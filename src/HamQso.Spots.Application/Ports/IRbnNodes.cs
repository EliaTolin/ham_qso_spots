using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Ports;

/// <summary>The RBN node list as the site publishes it (reversebeacon.net/nodes).</summary>
public interface IRbnNodeSource
{
    Task<RbnNodeList> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>The nodes read, and whether the page no longer looks as expected.</summary>
public sealed record RbnNodeList(IReadOnlyList<RbnNode> Nodes, bool FormatChanged);

/// <summary>The last good node list, kept across restarts.</summary>
public interface IRbnNodeStore
{
    /// <summary>The list saved last, or null before the first good read.</summary>
    Task<SavedRbnNodes?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the saved list with a new good read.</summary>
    Task ReplaceAsync(IReadOnlyList<RbnNode> nodes, DateTimeOffset fetchedAt, CancellationToken cancellationToken);
}

public sealed record SavedRbnNodes(IReadOnlyList<RbnNode> Nodes, DateTimeOffset FetchedAt);

/// <summary>The skimmers' activity per minute and band, kept 48 hours (rbn_skimmer_activity).</summary>
public interface IRbnActivityStore
{
    /// <summary>Adds the counts to those already saved for the same skimmer, band and minute.</summary>
    Task AddAsync(IReadOnlyCollection<ActivityMinute> minutes, CancellationToken cancellationToken);

    /// <summary>The minutes of <paramref name="band"/> from the minute <paramref name="fromMinute"/> to <paramref name="until"/>.</summary>
    Task<IReadOnlyList<ActivityMinute>> ReadAsync(string band, DateTimeOffset fromMinute, DateTimeOffset until, CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
