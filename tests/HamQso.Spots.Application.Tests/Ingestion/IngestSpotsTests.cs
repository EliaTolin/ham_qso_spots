using HamQso.Spots.Application.Dedup;
using HamQso.Spots.Application.Ingestion;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Ingestion;

public sealed class IngestSpotsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Now);
    private readonly FakeStore store = new();
    private readonly FakePublisher publisher = new();
    private readonly FakeAlerts alerts = new();
    private readonly IngestSpots ingest;

    public IngestSpotsTests()
    {
        ingest = new IngestSpots(new InMemorySpotDeduplicator(time), store, publisher, alerts, time, NullLogger<IngestSpots>.Instance);
    }

    public void Dispose() => ingest.Dispose();

    private static Spot Dx(string call, double khz, string feed, string comment = "CW 599", int minutesAgo = 0, string spotter = "IK5PWJ") => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = FormattableString.Invariant($"{call}_{khz}_{minutesAgo}_{feed}"),
        Callsign = call,
        FrequencyKhz = khz,
        SpotTime = Now.AddMinutes(-minutesAgo),
        Feeds = [feed],
        Spotters = [spotter],
        Comment = comment,
    };

    private static Spot Pota(string id, string call = "K8RLE", double khz = 7074) => new()
    {
        Program = SpotProgram.Pota,
        SpotKey = id,
        Callsign = call,
        FrequencyKhz = khz,
        SpotTime = Now,
        Feeds = ["POTA"],
        Mode = "FT8",
        Reference = "US-6701",
    };

    private Task Receive(params Spot[] spots) => ingest.HandleAsync(new SpotBatch(spots[0].Feeds[0], spots), CancellationToken.None);

    [Fact]
    public async Task The_same_spot_from_DXFun_and_HamQTH_is_saved_and_alerted_once_with_both_feeds()
    {
        await Receive(Dx("JA1ABC", 14025.0, "DXFun"));
        await Receive(Dx("JA1ABC", 14025.1, "HamQTH", spotter: "DL1HBT"));

        var saved = store.Saved.Last(s => s.Callsign == "JA1ABC");
        Assert.Equal(["DXFun", "HamQTH"], saved.Feeds);
        Assert.Single(alerts.Notified, s => s.Callsign == "JA1ABC");
    }

    [Fact]
    public async Task Only_the_new_spot_goes_out_on_its_topic_with_the_seq_of_the_archive()
    {
        await Receive(Dx("JA1ABC", 14025.0, "DXFun"));
        await Receive(Dx("VK2XYZ", 14030.0, "DXFun"));

        var messages = publisher.All("spots/dx/20m/cw");
        Assert.Equal(2, messages.Length);
        var last = Assert.Single(messages[^1]);
        Assert.Equal(("VK2XYZ", 2L), (last.Callsign, last.Seq));
    }

    [Fact]
    public async Task A_merged_duplicate_goes_out_again_with_a_new_seq()
    {
        await Receive(Dx("JA1ABC", 14025.0, "DXFun"));
        await Receive(Dx("JA1ABC", 14025.1, "HamQTH", spotter: "DL1HBT"));

        var update = Assert.Single(publisher.All("spots/dx/20m/cw")[^1]);
        Assert.Equal(["DXFun", "HamQTH"], update.Feeds);
        Assert.Equal(2L, update.Seq);
    }

    [Fact]
    public async Task An_xOTA_feed_read_again_without_changes_publishes_nothing_new()
    {
        await Receive(Pota("1"), Pota("2", "DF5WC", 14058.5));
        await Receive(Pota("1"), Pota("2", "DF5WC", 14058.5));
        await Receive(Pota("1"), Pota("2", "DF5WC", 14058.5), Pota("3", "IU4ABC", 7100));

        var messages = publisher.All("spots/pota");
        Assert.Equal(2, messages.Length);
        Assert.Equal(["IU4ABC"], messages[^1].Select(s => s.Callsign));
    }

    [Fact]
    public async Task An_old_spot_from_a_telnet_backlog_does_not_trigger_an_alert()
    {
        await Receive(Dx("JA1ABC", 14025.0, "DXFun", minutesAgo: 30));

        Assert.Empty(alerts.Notified);
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task With_the_archive_down_nothing_is_published_but_the_alerts_go_out()
    {
        store.Fail = true;

        await Receive(Dx("JA1ABC", 14025.0, "DXFun"));

        Assert.Empty(publisher.Topics);
        Assert.Single(alerts.Notified);
    }

    [Fact]
    public async Task After_a_restart_spots_already_archived_are_neither_alerted_nor_published_again()
    {
        store.Archive.Add(SpotEnrichment.Enrich(Dx("JA1ABC", 14025.0, "DXFun", minutesAgo: 2)));

        await ingest.StartAsync(CancellationToken.None);
        await Receive(Dx("JA1ABC", 14025.0, "DXFun", minutesAgo: 2));

        Assert.Empty(alerts.Notified);
        Assert.Empty(publisher.Topics);
    }

    [Fact]
    public async Task With_the_broker_not_answering_a_new_spot_reaches_the_alerts_within_seconds()
    {
        var hanging = new HangingPublisher();
        using var withHangingBroker = new IngestSpots(new InMemorySpotDeduplicator(time), store, hanging, alerts, time, NullLogger<IngestSpots>.Instance);

        var handling = withHangingBroker.HandleAsync(new SpotBatch("DXFun", [Dx("JA1ABC", 14025.0, "DXFun")]), CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (alerts.Notified.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Single(alerts.Notified);
        hanging.Release();
        await handling;
    }

    private sealed class HangingPublisher : ISpotPublisher
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => released.TrySetResult();

        public Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken) => released.Task;
    }

    /// <summary>Gives every saved spot the next seq, as Postgres does.</summary>
    private sealed class FakeStore : ISpotStore
    {
        private long seq;

        public List<Spot> Archive { get; } = [];

        public List<Spot> Saved { get; } = [];

        public bool Fail { get; set; }

        public Task<IReadOnlyList<Spot>> SaveAsync(IReadOnlyCollection<Spot> spots, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Postgres down");
            var saved = spots.Select(s => s with { Seq = ++seq }).ToArray();
            Saved.AddRange(saved);
            return Task.FromResult<IReadOnlyList<Spot>>(saved);
        }

        public Task<IReadOnlyList<Spot>> LoadSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spot>>(Archive.Where(s => s.SpotTime >= since).ToArray());

        public Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakePublisher : ISpotPublisher
    {
        private readonly List<(string Topic, IReadOnlyList<Spot> Spots)> messages = [];

        public IEnumerable<string> Topics => messages.Select(m => m.Topic).Distinct();

        public IReadOnlyList<Spot>[] All(string topic) => messages.Where(m => m.Topic == topic).Select(m => m.Spots).ToArray();

        public Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
        {
            messages.Add((topic, spots));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAlerts : IAlertNotifier
    {
        public List<Spot> Notified { get; } = [];

        public Task NotifyAsync(IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
        {
            Notified.AddRange(spots);
            return Task.CompletedTask;
        }
    }
}
