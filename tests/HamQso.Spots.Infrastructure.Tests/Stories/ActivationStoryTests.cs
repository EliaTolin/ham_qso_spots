using HamQso.Spots.Application.Dedup;
using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ingestion;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HamQso.Spots.Infrastructure.Tests.Stories;

/// <summary>
/// User stories of an activation, end to end inside the server: the feed as the
/// sources give it, the real ingestion, the real archive (Postgres) and the
/// real list of the apps (GetSpots). Only the broker and the alerts are recorded.
/// </summary>
public sealed class ActivationStoryTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Ten = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly FakeTimeProvider time = new(Ten);
    private readonly Broker broker = new();
    private readonly Alerts alerts = new();
    private NpgsqlDataSource dataSource = null!;
    private PostgresSpotStore store = null!;
    private IngestSpots server = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        store = new PostgresSpotStore(dataSource, NullSourceHealth.Instance);
        await store.EnsureSchemaAsync(TestContext.Current.CancellationToken);
        server = new IngestSpots(new InMemorySpotDeduplicator(time), store, broker, alerts, time, NullLogger<IngestSpots>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        await postgres.DisposeAsync();
    }

    public void Dispose() => server?.Dispose();

    /// <summary>A row of the POTA feed, as PotaFeed reads it.</summary>
    private static Spot PotaRow(string id, double khz, DateTimeOffset at, string comment, string spotter) => new()
    {
        Program = SpotProgram.Pota,
        SpotKey = id,
        Callsign = "IU4ABC",
        FrequencyKhz = khz,
        SpotTime = at,
        Feeds = ["POTA"],
        Spotters = [spotter],
        Mode = "FT8",
        Reference = "I-0123",
        ReferenceName = "Parco del test",
        Location = "IT-ER",
        Comment = comment,
    };

    /// <summary>The server reads the POTA feed at <paramref name="at"/>.</summary>
    private async Task PollPota(DateTimeOffset at, params Spot[] feed)
    {
        time.SetUtcNow(at);
        broker.Clear();
        alerts.Clear();
        await server.HandleAsync(new SpotBatch("POTA", feed), TestContext.Current.CancellationToken);
    }

    private Task<SpotPage> AppOpens(long? sinceSeq = null) =>
        new GetSpots(store).ExecuteAsync(new SpotFilter(SpotProgram.Pota, [], []), null, sinceSeq, TestContext.Current.CancellationToken);

    [Fact]
    public async Task IU4ABC_activates_a_park_is_respotted_changes_band_and_the_apps_always_see_one_row_per_band()
    {
        // 1. 10:00 IU4ABC spots himself on POTA from I-0123, 40 m FT8. The server reads the feed 30 s later:
        //    a new spot, saved, sent to the apps, alerted.
        var selfSpot = PotaRow("1001", 7074, Ten, "CQ POTA", "IU4ABC");
        await PollPota(Ten.AddSeconds(30), selfSpot);
        var first = Assert.Single(broker.Sent("spots/pota"));
        Assert.Equal(("IU4ABC_I-0123_40m", 7074.0, 1), (first.SpotKey, first.FrequencyKhz, first.SpotCount));
        Assert.Single(alerts.Sent);

        // 2. 10:01 the feed sends the same row again (every 30 s): nothing new anywhere.
        await PollPota(Ten.AddMinutes(1), selfSpot);
        Assert.Empty(broker.All);
        Assert.Empty(alerts.Sent);

        // 3. 10:20 a hunter respots him on 7.076 (POTA gives the spot a new id): the same row is updated,
        //    the apps get it again with a new seq; no new alert for the same activation.
        var respot = PotaRow("1002", 7076, Ten.AddMinutes(20), "TNX 599", "IK5PWJ");
        await PollPota(Ten.AddMinutes(20).AddSeconds(30), respot);
        var update = Assert.Single(broker.Sent("spots/pota"));
        Assert.Equal(("IU4ABC_I-0123_40m", 7076.0, "TNX 599"), (update.SpotKey, update.FrequencyKhz, update.Comment));
        Assert.True(update.Seq > first.Seq);
        Assert.Equal(2, update.SpotCount);
        Assert.Empty(alerts.Sent);

        // 4. 10:45 the feed still shows the 10:20 spot: nothing goes out.
        await PollPota(Ten.AddMinutes(45), respot);
        Assert.Empty(broker.All);


        // 5. 10:50 IU4ABC moves to 20 m: another band, a new spot, sent and alerted.
        await PollPota(Ten.AddMinutes(50).AddSeconds(30), PotaRow("1003", 14074, Ten.AddMinutes(50), "QSY 20m", "IU4ABC"));
        var qsy = Assert.Single(broker.Sent("spots/pota"));
        Assert.Equal("IU4ABC_I-0123_20m", qsy.SpotKey);
        Assert.Single(alerts.Sent);

        // 6. An app opens: one row per band, newest first, with the latest data.
        var list = await AppOpens();
        Assert.True(list.Full);
        Assert.Equal(["IU4ABC_I-0123_20m", "IU4ABC_I-0123_40m"], list.Spots.Select(s => s.SpotKey));
        Assert.Equal((7076.0, 2), (list.Spots[1].FrequencyKhz, list.Spots[1].SpotCount));
        Assert.Equal((1, "Parco del test"), (list.Spots[0].SpotCount, list.Spots[0].ReferenceName));

        // 7. An app disconnected since the respot asks what changed: only the QSY.
        var since = await AppOpens(update.Seq);
        Assert.False(since.Full);
        Assert.Equal(["IU4ABC_I-0123_20m"], since.Spots.Select(s => s.SpotKey));

        // 8. 17:00, seven hours later, the server has forgotten the activation (it remembers 6 hours) and the
        //    feed still lists the 10:20 spot: the archive sees nothing changed, nothing goes out, no alert.
        await PollPota(Ten.AddHours(7), respot);
        Assert.Empty(broker.All);
        Assert.Empty(alerts.Sent);

        // 9. The next day IU4ABC activates the same park on the same band again: same row (same key), newer,
        //    so it goes out to the apps and to the alerts. (Supabase forgets the alerted key after 6 hours,
        //    cleanup_alert_data in ham_qso_supabase, so the push is sent again.)
        await PollPota(Ten.AddDays(1).AddSeconds(30), PotaRow("2001", 7074, Ten.AddDays(1), "CQ POTA", "IU4ABC"));
        var nextDay = Assert.Single(broker.Sent("spots/pota"));
        Assert.Equal(("IU4ABC_I-0123_40m", Ten.AddDays(1)), (nextDay.SpotKey, nextDay.SpotTime));
        Assert.Single(alerts.Sent);
    }

    [Fact]
    public async Task A_SOTA_activator_spotted_twice_is_one_row_and_the_old_spot_sent_again_never_comes_back()
    {
        // SOTAwatch gives its last 50 spots every minute: the older spot of an activation keeps coming.
        Spot Sota(string id, DateTimeOffset at, double khz) => new()
        {
            Program = SpotProgram.Sota,
            SpotKey = id,
            Callsign = "HB9CBR/P",
            FrequencyKhz = khz,
            SpotTime = at,
            Feeds = ["SOTA"],
            Mode = "CW",
            Reference = "HB/SO-023",
        };
        var early = Sota("416600", Ten, 14062);
        var later = Sota("416665", Ten.AddMinutes(40), 14061.5);

        time.SetUtcNow(Ten.AddMinutes(41));
        await server.HandleAsync(new SpotBatch("SOTA", [later, early]), TestContext.Current.CancellationToken);
        time.SetUtcNow(Ten.AddMinutes(90));
        broker.Clear();
        await server.HandleAsync(new SpotBatch("SOTA", [later, early]), TestContext.Current.CancellationToken);

        Assert.Empty(broker.All);
        var row = Assert.Single((await new GetSpots(store).ExecuteAsync(new SpotFilter(SpotProgram.Sota, [], []), null, null, TestContext.Current.CancellationToken)).Spots);
        Assert.Equal((14061.5, Ten.AddMinutes(40)), (row.FrequencyKhz, row.SpotTime));
    }

    [Fact]
    public async Task A_DX_station_heard_by_three_sources_is_one_spot_that_grows_and_is_alerted_once()
    {
        Spot Dx(string feed, string spotter, int second) => new()
        {
            Program = SpotProgram.Dx,
            SpotKey = $"VK2XYZ_14025.0_{feed}",
            Callsign = "VK2XYZ",
            FrequencyKhz = 14025.0,
            SpotTime = Ten,
            Feeds = [feed],
            Spotters = [spotter],
            Comment = second == 0 ? "CW 599" : null,
        };

        time.SetUtcNow(Ten);
        foreach (var (feed, spotter, at) in new[] { ("DXFun", "EA4RCH-#", 0), ("HamQTH", "DL1HBT", 15), ("DXSummit", "OH8X", 30) })
        {
            await server.HandleAsync(new SpotBatch(feed, [Dx(feed, spotter, at)]), TestContext.Current.CancellationToken);
        }

        Assert.Single(alerts.All);
        var row = Assert.Single((await new GetSpots(store).ExecuteAsync(new SpotFilter(SpotProgram.Dx, [], []), null, null, TestContext.Current.CancellationToken)).Spots);
        Assert.Equal(["DXFun", "HamQTH", "DXSummit"], row.Feeds);
        Assert.Equal(["EA4RCH-#", "DL1HBT", "OH8X"], row.Spotters);
        Assert.Equal(3, broker.AllEver.Count);
    }

    private sealed class Broker : ISpotPublisher
    {
        private readonly List<(string Topic, Spot Spot)> sent = [];

        public List<(string Topic, Spot Spot)> AllEver { get; } = [];

        public IReadOnlyList<(string Topic, Spot Spot)> All => sent;

        public IReadOnlyList<Spot> Sent(string topic) => [.. sent.Where(m => m.Topic == topic).Select(m => m.Spot)];

        public void Clear() => sent.Clear();

        public Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
        {
            foreach (var s in spots)
            {
                sent.Add((topic, s));
                AllEver.Add((topic, s));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Alerts : IAlertNotifier
    {
        private readonly List<Spot> sent = [];

        public List<Spot> All { get; } = [];

        public IReadOnlyList<Spot> Sent => sent;

        public void Clear() => sent.Clear();

        public Task NotifyAsync(IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
        {
            sent.AddRange(spots);
            All.AddRange(spots);
            return Task.CompletedTask;
        }
    }
}
