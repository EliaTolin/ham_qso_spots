using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace HamQso.Spots.Infrastructure.Mqtt;

/// <summary>
/// Publishes every topic list to Mosquitto as one retained message (QoS 1), so an
/// app that connects gets the current lists at once. Connects on first use and
/// again after a drop.
/// </summary>
public sealed class MqttSpotPublisher(IOptions<MqttOptions> options) : ISpotPublisher, IAsyncDisposable
{
    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private readonly SemaphoreSlim connecting = new(1, 1);

    public async Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(SpotJson.Serialize(spots)))
            .WithContentType("application/json")
            .WithRetainFlag()
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        var result = await client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidOperationException($"MQTT publish to {topic} failed: {result.ReasonCode}");
    }

    public async ValueTask DisposeAsync()
    {
        if (client.IsConnected) await client.DisconnectAsync().ConfigureAwait(false);
        client.Dispose();
        connecting.Dispose();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (client.IsConnected) return;
        await connecting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (client.IsConnected) return;
            var o = options.Value;
            var connect = new MqttClientOptionsBuilder()
                .WithTcpServer(o.Host, o.Port)
                .WithClientId(o.ClientId)
                .WithCredentials(o.Username, o.Password)
                .WithCleanSession()
                .Build();
            await client.ConnectAsync(connect, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connecting.Release();
        }
    }
}
