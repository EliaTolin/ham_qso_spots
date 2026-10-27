using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>A feed read by HTTP polling: where to ask and how to read the answer.</summary>
public interface IPolledFeed
{
    string Name { get; }

    Uri Url { get; }

    /// <summary>Set when every answer is the whole list of active spots of a program.</summary>
    SpotProgram? SnapshotOf { get; }

    /// <summary>The spots in a response body; malformed rows are skipped. Pure.</summary>
    IReadOnlyList<Spot> Parse(string body, DateTimeOffset now);
}
