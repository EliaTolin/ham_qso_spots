using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>The broker (Mosquitto): one retained message per topic, the whole list.</summary>
public interface ISpotPublisher
{
    Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken);
}
