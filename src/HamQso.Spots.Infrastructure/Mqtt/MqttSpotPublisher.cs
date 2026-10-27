using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace HamQso.Spots.Infrastructure.Mqtt;

/// <summary>
/// Publishes the news of a topic to Mosquitto (QoS 1, not retained: the apps get
/// the starting list from GET /v1/spots). Connects on first use and
/// again after a drop. A broker that does not answer fails fast (B21): at most
/// <see cref="ConnectTimeout"/> per attempt, then no new attempt for
/// <see cref="RetryAfter"/>; /health shows it as "broker".
/// </summary>
public sealed class MqttSpotPublisher(IOptions<MqttOptions> options, ISourceHealth health, TimeProvider time) : ISpotPublisher, IAsyncDisposable
{
    /// <summary>Name of the broker in /health.</summary>
    public const string HealthName = "broker";

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private readonly SemaphoreSlim connecting = new(1, 1);
    private DateTimeOffset retryAt = DateTimeOffset.MinValue;

    public async Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(SpotJson.Serialize(new SpotPage(spots.Max(s => s.Seq) ?? 0, false, spots))))
            .WithContentType("application/json")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        var result = await client.PublishAsync(message, timeout.Token).ConfigureAwait(false);
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
            if (time.GetUtcNow() < retryAt) throw new InvalidOperationException("MQTT broker unavailable, next attempt later");
            var o = options.Value;
            var connect = new MqttClientOptionsBuilder()
                .WithTcpServer(o.Host, o.Port)
                .WithClientId(o.ClientId)
                .WithCredentials(o.Username, o.Password)
                .WithCleanSession()
                .WithTimeout(ConnectTimeout)
                .Build();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(connect, timeout.Token).ConfigureAwait(false);
                health.Succeeded(HealthName);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                retryAt = time.GetUtcNow() + RetryAfter;
                health.Failed(HealthName, e.Message);
                throw new InvalidOperationException($"MQTT broker {o.Host}:{o.Port} not reachable", e);
            }
        }
        finally
        {
            connecting.Release();
        }
    }
}
