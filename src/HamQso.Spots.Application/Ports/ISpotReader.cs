using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>The archive read for the apps (GET /v1/spots). Every answer carries the current seq.</summary>
public interface ISpotReader
{
    /// <summary>The newest spots of the filter, by spot time.</summary>
    Task<SpotPage> LatestAsync(SpotFilter filter, int limit, CancellationToken cancellationToken);

    /// <summary>Spots of the filter saved after <paramref name="sinceSeq"/>, oldest first, at most <paramref name="limit"/>.</summary>
    Task<SpotPage> ChangedSinceAsync(SpotFilter filter, long sinceSeq, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Which spots: one program, optionally some bands and modes. Bands and modes
/// are the topic segments ("20m", "cw"; "other" when unknown); empty means all.
/// </summary>
public sealed record SpotFilter(SpotProgram Program, IReadOnlyList<string> Bands, IReadOnlyList<string> Modes);

/// <summary>
/// A list of spots and the archive seq it is current to. The same shape goes
/// to the apps by REST and by MQTT: full = the whole current list, otherwise
/// only what changed.
/// </summary>
public sealed record SpotPage(long Seq, bool Full, IReadOnlyList<Spot> Spots);
