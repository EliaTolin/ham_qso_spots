namespace HamQso.Spots.Application.Ports;

/// <summary>Alerts off: without the Supabase secret key (a local run) no spot goes to alert_spots.</summary>
public sealed class NullAlertNotifier : IAlertNotifier
{
    public Task NotifyAsync(IReadOnlyList<AlertSpot> spots, CancellationToken cancellationToken) => Task.CompletedTask;
}
