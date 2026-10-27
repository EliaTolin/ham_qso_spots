using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Mqtt;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace HamQso.Spots.Infrastructure.Tests.Mqtt;

/// <summary>Against a real Mosquitto in Docker.</summary>
public sealed class MqttSpotPublisherTests : IAsyncLifetime
{
    private const string Config = "listener 1883\nallow_anonymous true\npersistence false\n";

    private readonly IContainer mosquitto = new ContainerBuilder("eclipse-mosquitto:2.0")
        .WithPortBinding(1883, true)
        .WithResourceMapping(Encoding.UTF8.GetBytes(Config), "/mosquitto/config/mosquitto.conf")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883))
        .Build();

    private readonly RecordingHealth health = new();
    private MqttSpotPublisher publisher = null!;

    public async ValueTask InitializeAsync()
    {
        await mosquitto.StartAsync(TestContext.Current.CancellationToken);
        publisher = new MqttSpotPublisher(Options.Create(new MqttOptions
        {
            Host = mosquitto.Hostname,
            Port = mosquitto.GetMappedPublicPort(1883),
            Username = "ingestor",
            Password = "test",
        }), health, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        await publisher.DisposeAsync();
        await mosquitto.DisposeAsync();
    }

    private static Spot Dx(string call, long seq) => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = call,
        Callsign = call,
        FrequencyKhz = 14025.0,
        SpotTime = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        Feeds = ["DXFun"],
        Comment = "CW 599",
        Seq = seq,
    });

    [Fact]
    public async Task A_connected_app_gets_the_news_with_their_seq()
    {
        await using var app = await Subscriber.ConnectAsync(mosquitto, "spots/dx/#");

        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("VK2XYZ", 41), Dx("JA1ABC", 42)], TestContext.Current.CancellationToken);

        var (topic, json) = Assert.Single(await app.ReceivedAsync());
        Assert.Equal("spots/dx/20m/cw", topic);
        var page = SpotJson.Deserialize(json);
        Assert.Equal((42L, false), (page.Seq, page.Full));
        Assert.Equal(["VK2XYZ", "JA1ABC"], page.Spots.Select(s => s.Callsign));
    }

    [Fact]
    public async Task After_publishing_on_the_broker_health_shows_broker_ok()
    {
        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("JA1ABC", 1)], TestContext.Current.CancellationToken);

        Assert.True(health.LastOf("broker"));
        Assert.Empty(health.Failures);
    }

    [Fact]
    public async Task The_news_are_not_kept_an_app_that_connects_later_gets_nothing()
    {
        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("JA1ABC", 1)], TestContext.Current.CancellationToken);

        await using var late = await Subscriber.ConnectAsync(mosquitto, "spots/#");

        Assert.Empty(await late.ReceivedAsync());
    }

    [Fact]
    public async Task Messages_use_the_snake_case_fields_of_the_dx_spots_table()
    {
        await using var app = await Subscriber.ConnectAsync(mosquitto, "spots/dx/20m/cw");

        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("JA1ABC", 7)], TestContext.Current.CancellationToken);

        var (_, json) = Assert.Single(await app.ReceivedAsync());
        Assert.Contains("\"seq\":7", json, StringComparison.Ordinal);
        Assert.Contains("\"full\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"program\":\"dx\"", json, StringComparison.Ordinal);
        Assert.Contains("\"spot_key\":\"JA1ABC\"", json, StringComparison.Ordinal);
        Assert.Contains("\"frequency_khz\":14025", json, StringComparison.Ordinal);
        Assert.Contains("\"mode_source\":\"reported\"", json, StringComparison.Ordinal);
        Assert.Contains("\"mode_category\":\"cw\"", json, StringComparison.Ordinal);
    }

    /// <summary>An app on the broker: subscribes, then collects what arrives.</summary>
    private sealed class Subscriber : IAsyncDisposable
    {
        private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
        private readonly List<(string Topic, string Json)> received = [];

        public static async Task<Subscriber> ConnectAsync(DotNet.Testcontainers.Containers.IContainer broker, string filter)
        {
            var s = new Subscriber();
            s.client.ApplicationMessageReceivedAsync += e =>
            {
                lock (s.received) s.received.Add((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString()));
                return Task.CompletedTask;
            };
            await s.client.ConnectAsync(
                new MqttClientOptionsBuilder().WithTcpServer(broker.Hostname, broker.GetMappedPublicPort(1883)).Build(),
                TestContext.Current.CancellationToken);
            await s.client.SubscribeAsync(filter, cancellationToken: TestContext.Current.CancellationToken);
            return s;
        }

        public async Task<List<(string Topic, string Json)>> ReceivedAsync()
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            lock (received) return [.. received];
        }

        public async ValueTask DisposeAsync()
        {
            if (client.IsConnected) await client.DisconnectAsync();
            client.Dispose();
        }
    }
}
