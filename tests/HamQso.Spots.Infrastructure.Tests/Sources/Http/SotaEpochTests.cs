using System.Collections.Concurrent;
using System.Net;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sources;
using HamQso.Spots.Infrastructure.Sources.Http;
using HamQso.Spots.Infrastructure.Sources.Telnet;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Http;

/// <summary>
/// B26, the rules SOTA gave Elia (28/10/2026): "No more than one request every
/// 60 seconds. […] always store the returned epoch and compare with the epoch
/// endpoint. Only fetch new spots if the epochs are different."
/// </summary>
public class SotaEpochTests
{
    private const string OneSpot = """[{"id":1,"activatorCallsign":"IU4ABC","frequency":14.062,"mode":"cw","summitCode":"I/ER-001","timeStamp":"2026-10-28T10:00:00"}]""";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Every_60_s_only_the_epoch_is_asked_the_list_once()
    {
        var sota = new FakeSota { Epoch = "A" };

        await Rounds(sota, 3);

        Assert.Equal(3, sota.EpochRequests);
        Assert.Equal(1, sota.ListRequests);
    }

    [Fact]
    public async Task The_spots_are_fetched_only_when_the_epoch_is_different()
    {
        var sota = new FakeSota { Epoch = "A" };

        await Rounds(sota, 3, round => sota.Epoch = round == 3 ? "B" : "A");

        Assert.Equal([1, 3], sota.ListRounds);
    }

    [Fact]
    public async Task The_epoch_is_stored_only_with_a_list_that_arrived()
    {
        var sota = new FakeSota { Epoch = "A", ListFails = true };

        await Rounds(sota, 2, round => sota.ListFails = round == 1);

        Assert.Equal([1, 2], sota.ListRounds);
    }

    [Fact]
    public async Task An_epoch_that_does_not_answer_asks_no_list_and_SOTA_is_failing()
    {
        var sota = new FakeSota { Epoch = null };
        var health = new RecordingHealth();

        await Rounds(sota, 2, health: health);

        Assert.Equal(0, sota.ListRequests);
        Assert.Equal(["SOTA", "SOTA"], health.Failures);
    }

    [Fact]
    public async Task With_the_same_epoch_SOTA_is_ok_and_the_spots_already_sent_stay()
    {
        // The apps' list comes from the archive: no batch removes nothing from it.
        var sota = new FakeSota { Epoch = "A", List = OneSpot };
        var health = new RecordingHealth();

        var batches = await Rounds(sota, 3, health: health);

        Assert.NotEmpty(batches);
        Assert.All(batches, b => Assert.Contains(b.Spots, s => s.Callsign == "IU4ABC"));
        Assert.Equal([true, true, true], health.Calls.Select(c => c.Ok));
    }

    [Fact]
    public void A_SOTA_interval_under_60_s_is_refused_at_start()
    {
        var feeds = new Dictionary<string, FeedOptions> { ["SOTA"] = new() { Interval = TimeSpan.FromSeconds(30) } };

        var e = Assert.Throws<InvalidOperationException>(() =>
            SpotSourceFactory.Create(new TelnetOptions(), feeds, _ => new HttpClient(), time));

        Assert.Contains("SOTA", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_SOTA_interval_of_60_s_or_more_is_accepted()
    {
        var feeds = new Dictionary<string, FeedOptions> { ["SOTA"] = new() { Interval = TimeSpan.FromMinutes(2) } };

        var sources = SpotSourceFactory.Create(new TelnetOptions(), feeds, _ => new HttpClient(), time);

        Assert.Contains(sources, s => s.Name == "SOTA");
    }

    /// <summary>
    /// Runs the SOTA source for <paramref name="rounds"/> intervals of 60 s;
    /// <paramref name="before"/> sets the feed up for each round (1-based).
    /// Every round ends with one report to /health, ok or failing.
    /// </summary>
    private async Task<IReadOnlyList<SpotBatch>> Rounds(FakeSota sota, int rounds, Action<int>? before = null, RecordingHealth? health = null)
    {
        health ??= new RecordingHealth();
        using var http = new HttpClient(sota);
        var source = new PolledSpotSource(new SotaFeed(), http, TimeSpan.FromMinutes(1), time);
        var batches = new ConcurrentQueue<SpotBatch>();
        using var cts = new CancellationTokenSource();
        Task? reading = null;

        for (var round = 1; round <= rounds; round++)
        {
            sota.Round = round;
            before?.Invoke(round);
            reading ??= Consume(source, health, batches, cts.Token);
            for (var i = 0; i < 5000 && health.Calls.Count < round; i++)
            {
                if (round > 1) time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }
        await cts.CancelAsync();
        await reading!;
        return [.. batches];
    }

    private static async Task Consume(ISpotSource source, ISourceHealth health, ConcurrentQueue<SpotBatch> batches, CancellationToken token)
    {
        try
        {
            await foreach (var batch in source.ReadAsync(health, token)) batches.Enqueue(batch);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class FakeSota : HttpMessageHandler
    {
        public int Round { get; set; }

        /// <summary>Null: the epoch endpoint fails.</summary>
        public string? Epoch { get; set; }

        public string List { get; set; } = "[]";

        public bool ListFails { get; set; }

        public int EpochRequests { get; private set; }

        public int ListRequests => ListRounds.Count;

        public List<int> ListRounds { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/spots/epoch")
            {
                EpochRequests++;
                return Task.FromResult(Epoch is null ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Ok(Epoch));
            }
            ListRounds.Add(Round);
            return Task.FromResult(ListFails ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Ok(List));
        }

        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
