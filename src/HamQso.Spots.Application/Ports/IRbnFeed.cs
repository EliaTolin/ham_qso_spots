namespace HamQso.Spots.Application.Ports;

/// <summary>
/// The Reverse Beacon Network live feed, shared: one connection for all the
/// readers, open only while someone reads.
/// </summary>
public interface IRbnFeed
{
    /// <summary>Every spot from now until <paramref name="cancellationToken"/>; one reader per enumeration.</summary>
    IAsyncEnumerable<RbnLine> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>A spot line of the RBN as received (<see cref="Raw"/>) and the station it decoded (<see cref="Call"/>, uppercase).</summary>
public sealed record RbnLine(string Raw, string Call);
