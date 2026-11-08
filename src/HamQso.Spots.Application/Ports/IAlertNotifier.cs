using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>Hands alertable spots to Supabase, which matches the alerts and sends the pushes.</summary>
public interface IAlertNotifier
{
    Task NotifyAsync(IReadOnlyList<AlertSpot> spots, CancellationToken cancellationToken);
}

/// <summary>
/// A row for the alerts. <paramref name="Respot"/> false: a new spot. True: a spot
/// already seen that a new spotter spotted again (F101): <see cref="Spot"/> is the
/// stored spot with that spotter and the time and comment of the new spot.
/// </summary>
public sealed record AlertSpot(Spot Spot, bool Respot);
