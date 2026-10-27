using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using HamQso.Spots.Application.Ports;
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
        }), new NoHealth(), TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        await publisher.DisposeAsync();
        await mosquitto.DisposeAsync();
    }

    private static Spot Dx(string call) => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = call,
        Callsign = call,
        FrequencyKhz = 14025.0,
        SpotTime = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        Feeds = ["DXFun"],
        Comment = "CW 599",
    });

    [Fact]
    public async Task An_app_that_connects_later_gets_the_whole_list_of_its_topic_at_once()
    {
        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("JA1ABC")], TestContext.Current.CancellationToken);
        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("VK2XYZ"), Dx("JA1ABC")], TestContext.Current.CancellationToken);

        var received = await ReceiveRetained("spots/dx/#");

        var (topic, json) = Assert.Single(received);
        Assert.Equal("spots/dx/20m/cw", topic);
        Assert.Equal(["VK2XYZ", "JA1ABC"], SpotJson.Deserialize(json).Select(s => s.Callsign));
    }

    [Fact]
    public async Task Messages_use_the_snake_case_fields_of_the_dx_spots_table()
    {
        await publisher.PublishAsync("spots/dx/20m/cw", [Dx("JA1ABC")], TestContext.Current.CancellationToken);

        var (_, json) = Assert.Single(await ReceiveRetained("spots/dx/20m/cw"));

        Assert.Contains("\"program\":\"dx\"", json, StringComparison.Ordinal);
        Assert.Contains("\"spot_key\":\"JA1ABC\"", json, StringComparison.Ordinal);
        Assert.Contains("\"frequency_khz\":14025", json, StringComparison.Ordinal);
        Assert.Contains("\"mode_source\":\"reported\"", json, StringComparison.Ordinal);
        Assert.Contains("\"mode_category\":\"cw\"", json, StringComparison.Ordinal);
    }

    /// <summary>A new client subscribes and collects what the broker sends in the first second.</summary>
    private async Task<List<(string Topic, string Json)>> ReceiveRetained(string filter)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        var received = new List<(string, string)>();
        client.ApplicationMessageReceivedAsync += e =>
        {
            lock (received) received.Add((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString()));
            return Task.CompletedTask;
        };
        await client.ConnectAsync(
            new MqttClientOptionsBuilder().WithTcpServer(mosquitto.Hostname, mosquitto.GetMappedPublicPort(1883)).Build(),
            TestContext.Current.CancellationToken);
        await client.SubscribeAsync(filter, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        lock (received) return [.. received];
    }

    private sealed class NoHealth : ISourceHealth
    {
        public void Succeeded(string source)
        {
        }

        public void Failed(string source, string reason)
        {
        }
    }
}
