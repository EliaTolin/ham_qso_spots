using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Application.Reading;

/// <summary>
/// GET /v1/spots: the starting list of an app, or what changed since its last
/// seq. Never more than <see cref="MaxLimit"/> spots: with more changes than
/// that the app gets the current list (full) instead.
/// </summary>
public sealed class GetSpots(ISpotReader reader)
{
    public const int MaxLimit = 200;

    public async Task<SpotPage> ExecuteAsync(SpotFilter filter, int? limit, long? sinceSeq, CancellationToken cancellationToken)
    {
        var max = Math.Clamp(limit ?? MaxLimit, 1, MaxLimit);
        if (sinceSeq is { } since)
        {
            // One more than allowed tells whether the changes fit.
            var changed = await reader.ChangedSinceAsync(filter, since, max + 1, cancellationToken).ConfigureAwait(false);
            if (changed.Spots.Count <= max) return changed with { Full = false };
        }
        var latest = await reader.LatestAsync(filter, max, cancellationToken).ConfigureAwait(false);
        return latest with { Full = true };
    }
}
