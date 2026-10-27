using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>
/// One feed of spots (a telnet node, HamQTH, DX Summit, POTA, SOTA, WWFF). It does
/// not know the others: the Host starts every configured source the same way.
/// </summary>
public interface ISpotSource
{
    /// <summary>Name in feeds, logs and /health ("DXFun", "HamQTH", "POTA").</summary>
    string Name { get; }

    /// <summary>
    /// Batches of spots until cancelled. Reconnecting and retrying are the
    /// source's job; a failure is reported to <paramref name="health"/>.
    /// </summary>
    IAsyncEnumerable<SpotBatch> ReadAsync(ISourceHealth health, CancellationToken cancellationToken);
}

/// <summary>
/// Spots from one read of a source. A snapshot is the whole list of active
/// spots of an xOTA program; DX batches carry only what arrived.
/// </summary>
public sealed record SpotBatch(string Feed, IReadOnlyList<Spot> Spots, SpotProgram? SnapshotOf = null);
