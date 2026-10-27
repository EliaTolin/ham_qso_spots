using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>The broker (Mosquitto): the news of a topic, saved spots with their seq.</summary>
public interface ISpotPublisher
{
    Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken);
}
