using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Time.Testing;
using HamQso.Spots.Infrastructure.Mqtt;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Mqtt;

/// <summary>A broker that accepts TCP and never answers (B21: Mosquitto restarting on Coolify).</summary>
public sealed class SilentBrokerTests : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> accepted = [];
    private readonly CancellationTokenSource stop = new();

    public SilentBrokerTests()
    {
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                lock (accepted) accepted.Add(client);
            }
        });
    }

    public void Dispose()
    {
        stop.Cancel();
        lock (accepted) accepted.ForEach(c => c.Dispose());
        listener.Dispose();
        stop.Dispose();
    }

    private MqttOptions Options => new() { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, Username = "ingestor", Password = "x" };

    private static Spot Spot() => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = "k",
        Callsign = "JA1ABC",
        FrequencyKhz = 14025,
        SpotTime = DateTimeOffset.UtcNow,
        Feeds = ["DXFun"],
    });

    [Fact]
    public async Task With_the_broker_down_the_first_publish_fails_within_5_s_and_the_next_ones_at_once()
    {
        await using var publisher = new MqttSpotPublisher(Microsoft.Extensions.Options.Options.Create(Options), new RecordingHealth(), TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(15));

        var first = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<Exception>(() => publisher.PublishAsync("spots/dx/20m/cw", [Spot()], guard.Token));
        first.Stop();

        var next = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<Exception>(() => publisher.PublishAsync("spots/dx/20m/cw", [Spot()], guard.Token));
        next.Stop();

        Assert.False(guard.IsCancellationRequested, "the publish waited for the 15 s guard: no timeout of its own");
        Assert.InRange(first.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(6));
        Assert.InRange(next.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task With_the_broker_down_health_shows_broker_failing_and_recovers_after_10_s()
    {
        var health = new RecordingHealth();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var publisher = new MqttSpotPublisher(Microsoft.Extensions.Options.Options.Create(Options), health, time);

        await Assert.ThrowsAnyAsync<Exception>(() => publisher.PublishAsync("spots/dx/20m/cw", [Spot()], TestContext.Current.CancellationToken));
        Assert.Equal(["broker"], health.Failures);

        // After the backoff it tries the broker again (and fails again here).
        time.Advance(MqttSpotPublisher.RetryAfter);
        await Assert.ThrowsAnyAsync<Exception>(() => publisher.PublishAsync("spots/dx/20m/cw", [Spot()], TestContext.Current.CancellationToken));
        Assert.Equal(2, health.Failures.Count);
    }
}
