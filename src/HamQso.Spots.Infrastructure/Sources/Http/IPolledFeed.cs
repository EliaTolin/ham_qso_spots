using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>A feed read by HTTP polling: where to ask and how to read the answer.</summary>
public interface IPolledFeed
{
    string Name { get; }

    Uri Url { get; }

    /// <summary>
    /// Changes whenever the list at <see cref="Url"/> changes: when set, the list
    /// is asked only if this answers differently from last time. Null: no epoch.
    /// </summary>
    Uri? EpochUrl => null;

    /// <summary>The spots in a response body; malformed rows are skipped. Pure.</summary>
    IReadOnlyList<Spot> Parse(string body, DateTimeOffset now);
}
