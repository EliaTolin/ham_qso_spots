using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Reading;

/// <summary>
/// GET /v1/spots/on-frequency (F88): the archive gives the recent spots around
/// the frequency, the rule (<see cref="SpotOnFrequency"/>) chooses. The frequency
/// is not kept and not logged.
/// </summary>
public sealed class GetSpotOnFrequency(ISpotReader reader, TimeProvider time)
{
    public async Task<Spot?> ExecuteAsync(double khz, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var radius = SpotOnFrequency.CandidateRadiusKhz;
        var candidates = await reader.RecentNearAsync(khz - radius, khz + radius, now - SpotOnFrequency.MaxAge, now + Spot.FutureTolerance, cancellationToken).ConfigureAwait(false);
        return SpotOnFrequency.Pick(khz, candidates, now);
    }
}
