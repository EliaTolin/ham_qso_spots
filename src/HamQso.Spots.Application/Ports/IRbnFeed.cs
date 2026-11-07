using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Ports;

/// <summary>
/// The Reverse Beacon Network live feed, shared: one connection for all the
/// readers. F71: always open (a background reader counts the skimmers'
/// activity), with its state for the clients.
/// </summary>
public interface IRbnFeed
{
    /// <summary>Every spot from now until <paramref name="cancellationToken"/>; one reader per enumeration.</summary>
    IAsyncEnumerable<RbnLine> ReadAsync(CancellationToken cancellationToken);

    /// <summary>The state of the connection now.</summary>
    RbnFeedStatus Status { get; }

    /// <summary>Completes at the next change of <see cref="Status"/> (up, reconnecting, down).</summary>
    Task WaitForChangeAsync(CancellationToken cancellationToken);
}

/// <summary>A spot line of the RBN as received (<see cref="Raw"/>, trimmed), what it says, and when it arrived here.</summary>
public sealed record RbnLine(string Raw, RbnSpot Spot, DateTimeOffset ReceivedAt)
{
    /// <summary>The station heard, in capitals.</summary>
    public string Call => Spot.Call;
}

/// <summary>Up: spots are arriving. Reconnecting: the connection dropped and is being reopened. Down: no connection.</summary>
public enum RbnFeedState
{
    Down,
    Reconnecting,
    Up,
}

/// <summary>The state of the feed and since when (null: since the server started).</summary>
public sealed record RbnFeedStatus(RbnFeedState State, DateTimeOffset? Since);
