using System.Net;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sources.Http;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Http;

public class PolledSpotSourceTests
{
    [Fact]
    public async Task SOTA_is_asked_once_a_minute_however_many_apps_are_connected()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        var handler = new CountingHandler("[]");
        using var http = new HttpClient(handler);
        var source = new PolledSpotSource(new SotaFeed(), http, TimeSpan.FromMinutes(1), time);
        using var cts = new CancellationTokenSource();

        var reading = Consume(source, new FakeHealth(), cts.Token);
        for (var second = 0; second < 10 * 60; second++)
        {
            await Task.Yield();
            time.Advance(TimeSpan.FromSeconds(1));
        }
        await cts.CancelAsync();
        await reading;

        // 1 at start + 1 per minute; never more, with 1 or 1.000 MQTT clients.
        Assert.InRange(handler.Requests, 10, 11);
    }

    [Fact]
    public async Task A_failing_feed_is_reported_and_tried_again()
    {
        var time = new FakeTimeProvider();
        var handler = new CountingHandler(null);
        using var http = new HttpClient(handler);
        var source = new PolledSpotSource(new PotaFeed(), http, TimeSpan.FromSeconds(30), time);
        var health = new FakeHealth();
        using var cts = new CancellationTokenSource();

        var reading = Consume(source, health, cts.Token);
        for (var i = 0; i < 100 && handler.Requests < 2; i++)
        {
            await Task.Yield();
            time.Advance(TimeSpan.FromSeconds(1));
        }
        await cts.CancelAsync();
        await reading;

        Assert.True(handler.Requests >= 2);
        Assert.Contains("POTA", health.Failures);
    }

    private static async Task Consume(ISpotSource source, ISourceHealth health, CancellationToken token)
    {
        try
        {
            await foreach (var _ in source.ReadAsync(health, token))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class CountingHandler(string? body) : HttpMessageHandler
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class FakeHealth : ISourceHealth
    {
        public List<string> Failures { get; } = [];

        public void Succeeded(string source)
        {
        }

        public void Failed(string source, string reason) => Failures.Add(source);
    }
}
