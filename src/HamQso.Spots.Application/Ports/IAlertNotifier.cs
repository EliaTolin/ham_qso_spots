using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>Hands new spots to Supabase, which matches the alerts and sends the pushes.</summary>
public interface IAlertNotifier
{
    Task NotifyAsync(IReadOnlyList<Spot> spots, CancellationToken cancellationToken);
}
