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
    public async Task A_spot_arrives_on_the_topic_of_its_band_and_mode_with_the_whole_list()
    {
        await Receive(Dx("JA1ABC", 14025.0, "DXFun", minutesAgo: 1));
        await Receive(Dx("VK2XYZ", 14030.0, "DXFun"));

        var message = publisher.Last("spots/dx/20m/cw");
        Assert.Equal(["VK2XYZ", "JA1ABC"], message.Select(s => s.Callsign));
    }

    [Fact]
    public async Task The_retained_DX_lists_hold_the_last_50_spots_in_all()
    {
        for (var i = 0; i < IngestSpots.RecentDxCapacity + 10; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Receive(Dx($"K{i}AA", 14025.0 + i, "DXFun", minutesAgo: 0) with { SpotTime = time.GetUtcNow() });
        }

        var total = publisher.Topics.Where(t => t.StartsWith("spots/dx/", StringComparison.Ordinal)).Sum(t => publisher.Last(t).Count);
        Assert.Equal(IngestSpots.RecentDxCapacity, total);
    }

    [Fact]
    public async Task An_xOTA_snapshot_is_published_whole_and_only_when_it_changes()
    {
        await ingest.HandleAsync(new SpotBatch("POTA", [Pota("1"), Pota("2", "DF5WC", 14058.5)], SpotProgram.Pota), CancellationToken.None);
        await ingest.HandleAsync(new SpotBatch("POTA", [Pota("1"), Pota("2", "DF5WC", 14058.5)], SpotProgram.Pota), CancellationToken.None);
        await ingest.HandleAsync(new SpotBatch("POTA", [Pota("2", "DF5WC", 14058.5)], SpotProgram.Pota), CancellationToken.None);

        var messages = publisher.All("spots/pota");
        Assert.Equal(2, messages.Length);
        Assert.Equal(["DF5WC"], messages[^1].Select(s => s.Callsign));
    }

    [Fact]
    public async Task An_old_spot_from_a_telnet_backlog_does_not_trigger_an_alert()
    {
        await Receive(Dx("JA1ABC", 14025.0, "DXFun", minutesAgo: 30));

        Assert.Empty(alerts.Notified);
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task The_broker_gets_the_spot_even_when_the_archive_is_down()
    {
        store.Fail = true;

        await Receive(Dx("JA1ABC", 14025.0, "DXFun"));

        Assert.Single(publisher.Last("spots/dx/20m/cw"));
        Assert.Single(alerts.Notified);
    }

    [Fact]
    public async Task After_a_restart_spots_already_archived_are_not_alerted_again()
    {
        store.Archive.Add(SpotEnrichment.Enrich(Dx("JA1ABC", 14025.0, "DXFun", minutesAgo: 2)));

        await ingest.StartAsync(CancellationToken.None);
        await Receive(Dx("JA1ABC", 14025.0, "HamQTH"));

        Assert.Empty(alerts.Notified);
        Assert.Single(publisher.Last("spots/dx/20m/cw"));
    }

    private sealed class FakeStore : ISpotStore
    {
        public List<Spot> Archive { get; } = [];

        public List<Spot> Saved { get; } = [];

        public bool Fail { get; set; }

        public Task SaveAsync(IReadOnlyCollection<Spot> spots, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Postgres down");
            Saved.AddRange(spots);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Spot>> LoadSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spot>>(Archive.Where(s => s.SpotTime >= since).ToArray());

        public Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakePublisher : ISpotPublisher
    {
        private readonly List<(string Topic, IReadOnlyList<Spot> Spots)> messages = [];

        public IEnumerable<string> Topics => messages.Select(m => m.Topic).Distinct();

        public IReadOnlyList<Spot> Last(string topic) => messages.Last(m => m.Topic == topic).Spots;

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
