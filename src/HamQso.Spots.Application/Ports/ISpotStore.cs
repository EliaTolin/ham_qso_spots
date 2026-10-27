using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>The archive (Postgres, 7 days).</summary>
public interface ISpotStore
{
    /// <summary>Inserts new spots and updates merged ones (same program and key).</summary>
    Task SaveAsync(IReadOnlyCollection<Spot> spots, CancellationToken cancellationToken);

    /// <summary>Spots from <paramref name="since"/> on, oldest first: reloads the dedup and the topics at startup.</summary>
    Task<IReadOnlyList<Spot>> LoadSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Deletes spots older than <paramref name="before"/>; returns how many.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
