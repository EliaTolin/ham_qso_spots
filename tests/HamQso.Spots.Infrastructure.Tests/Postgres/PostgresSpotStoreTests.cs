using Dapper;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HamQso.Spots.Infrastructure.Tests.Postgres;

/// <summary>Against a real Postgres in Docker (the same major version as on Coolify).</summary>
public sealed class PostgresSpotStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private NpgsqlDataSource dataSource = null!;
    private readonly RecordingHealth health = new();
    private PostgresSpotStore store = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        store = new PostgresSpotStore(dataSource, health);
        await store.EnsureSchemaAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private static Spot Dx(string call, int minutesAgo = 0) => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = $"{call}_{minutesAgo}",
        Callsign = call,
        FrequencyKhz = 14025.0,
        SpotTime = Now.AddMinutes(-minutesAgo),
        Feeds = ["DXFun"],
        Spotters = ["EA4RCH-#"],
        Comment = "CW 12 dB 25 WPM CQ",
        SpotterLocator = "IN80",
    });

    [Fact]
    public async Task A_saved_spot_comes_back_the_same()
    {
        var spot = Dx("JA1ABC");
        var pota = new Spot
        {
            Program = SpotProgram.Pota,
            SpotKey = "59545154",
            Callsign = "K8RLE",
            FrequencyKhz = 7074,
            SpotTime = Now,
            Feeds = ["POTA"],
            Mode = "FT8",
            ModeSource = ModeSource.Reported,
            ModeCategory = ModeCategory.Digital,
            Reference = "US-6701",
            ReferenceName = "Haymarsh Lake State Game Land",
            Location = "US-MI",
            Latitude = 43.7281,
            Longitude = -85.3554,
        };

        await store.SaveAsync([spot, pota], TestContext.Current.CancellationToken);
        var loaded = await store.LoadSinceAsync(Now.AddHours(-1), TestContext.Current.CancellationToken);

        Assert.Equal(2, loaded.Count);
        AssertSame(spot, loaded.Single(s => s.Program == SpotProgram.Dx));
        AssertSame(pota, loaded.Single(s => s.Program == SpotProgram.Pota));
    }

    [Fact]
    public async Task A_merged_duplicate_updates_the_row_with_both_feeds()
    {
        var first = Dx("JA1ABC");
        await store.SaveAsync([first], TestContext.Current.CancellationToken);

        await store.SaveAsync([first with { Feeds = ["DXFun", "HamQTH"], Spotters = ["EA4RCH-#", "DL1HBT"] }], TestContext.Current.CancellationToken);

        var loaded = Assert.Single(await store.LoadSinceAsync(Now.AddHours(-1), TestContext.Current.CancellationToken));
        Assert.Equal(["DXFun", "HamQTH"], loaded.Feeds);
        Assert.Equal(["EA4RCH-#", "DL1HBT"], loaded.Spotters);
    }

    [Fact]
    public async Task Spots_older_than_7_days_are_deleted()
    {
        await store.SaveAsync([Dx("OLD1", minutesAgo: 8 * 24 * 60), Dx("OLD2", minutesAgo: 7 * 24 * 60 + 1), Dx("NEW", minutesAgo: 6 * 24 * 60)], TestContext.Current.CancellationToken);

        var deleted = await store.DeleteOlderThanAsync(Now.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(2, deleted);
        var left = await store.LoadSinceAsync(DateTimeOffset.MinValue, TestContext.Current.CancellationToken);
        Assert.Equal(["NEW"], left.Select(s => s.Callsign));
    }

    [Fact]
    public async Task The_schema_can_be_applied_at_every_startup() =>
        await store.EnsureSchemaAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Every_save_gets_a_growing_seq_and_an_update_gets_a_new_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await store.SaveAsync([Dx("JA1ABC"), Dx("VK2XYZ")], ct);
        var update = await store.SaveAsync([first[0] with { Feeds = ["DXFun", "HamQTH"] }], ct);

        Assert.True(first[0].Seq < first[1].Seq);
        Assert.True(update[0].Seq > first[1].Seq);
        var loaded = await store.LoadSinceAsync(Now.AddHours(-1), ct);
        Assert.Equal(update[0].Seq, loaded.Single(s => s.Callsign == "JA1ABC").Seq);
    }

    [Fact]
    public async Task The_latest_spots_of_a_filter_come_newest_first_with_the_current_seq()
    {
        var ct = TestContext.Current.CancellationToken;
        await store.SaveAsync([Dx("OLDER", minutesAgo: 5), Dx("NEWER", minutesAgo: 1), Dx("ON40", minutesAgo: 0) with { FrequencyKhz = 7025, Band = null }], ct);
        var pota = await store.SaveAsync([Pota("1")], ct);

        var page = await store.LatestAsync(new SpotFilter(SpotProgram.Dx, ["20m"], ["cw"]), 10, ct);

        Assert.Equal(["NEWER", "OLDER"], page.Spots.Select(s => s.Callsign));
        Assert.Equal(pota[0].Seq, page.Seq);
        Assert.Equal(["NEWER"], (await store.LatestAsync(new SpotFilter(SpotProgram.Dx, [], []), 2, ct)).Spots.Skip(1).Select(s => s.Callsign));
    }

    [Fact]
    public async Task Other_band_and_other_mode_are_the_spots_without_them()
    {
        var ct = TestContext.Current.CancellationToken;
        await store.SaveAsync([Dx("INBAND"), Dx("OUTBAND") with { SpotKey = "out", FrequencyKhz = 472.0, Band = null }], ct);

        var other = await store.LatestAsync(new SpotFilter(SpotProgram.Dx, ["other"], []), 10, ct);

        Assert.Equal(["OUTBAND"], other.Spots.Select(s => s.Callsign));
    }

    [Fact]
    public async Task Since_seq_gives_only_what_was_saved_after_it_oldest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await store.SaveAsync([Dx("A")], ct);
        await store.SaveAsync([Dx("B")], ct);
        await store.SaveAsync([Dx("C")], ct);

        var page = await store.ChangedSinceAsync(new SpotFilter(SpotProgram.Dx, [], []), before[0].Seq!.Value, 10, ct);

        Assert.Equal(["B", "C"], page.Spots.Select(s => s.Callsign));
    }

    [Fact]
    public async Task The_table_already_in_production_without_seq_upgrades_itself()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        {
            // The table as deployed on 27/10/2026 (b341b02), with a row in it.
            await connection.ExecuteAsync("""
                drop table dx_spots;
                drop sequence dx_spots_seq_counter;
                create table dx_spots (program text not null, spot_key text not null, callsign text not null, frequency_khz double precision not null,
                  spot_time timestamptz not null, feeds text[] not null, spotters text[] not null default '{}', band text, mode text not null default '',
                  mode_source text, mode_category text, reference text, location text, dxcc integer, continent text, latitude double precision,
                  longitude double precision, comment text, spotter_locator text, received_at timestamptz not null default now(), primary key (program, spot_key));
                insert into dx_spots (program, spot_key, callsign, frequency_khz, spot_time, feeds) values ('dx', 'old', 'OLD1', 14025, now(), '{DXFun}');
                """);
        }

        await store.EnsureSchemaAsync(ct);
        var saved = await store.SaveAsync([Dx("NEW1")], ct);

        var page = await store.LatestAsync(new SpotFilter(SpotProgram.Dx, [], []), 10, ct);
        Assert.Equal(2, page.Spots.Count);
        Assert.All(page.Spots, s => Assert.NotNull(s.Seq));
        Assert.Equal(saved[0].Seq, page.Seq);
    }

    [Fact]
    public async Task A_save_shows_archive_ok_in_health()
    {
        await store.SaveAsync([Dx("JA1ABC")], TestContext.Current.CancellationToken);

        Assert.True(health.LastOf("archive"));
    }

    [Fact]
    public async Task With_Postgres_unreachable_a_save_fails_and_health_shows_archive_failing()
    {
        var down = new RecordingHealth();
        await using var nowhere = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=2;Database=spots;Username=spots;Password=x");
        var unreachable = new PostgresSpotStore(nowhere, down);

        await Assert.ThrowsAnyAsync<Exception>(() => unreachable.SaveAsync([Dx("JA1ABC")], TestContext.Current.CancellationToken));

        Assert.Equal(["archive"], down.Failures);
    }

    [Fact]
    public async Task An_older_spot_of_the_same_activation_never_overwrites_the_newer_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var newer = Pota("2") with { FrequencyKhz = 7076, SpotTime = Now.AddMinutes(30) };
        await store.SaveAsync([newer], ct);

        // SOTA sends its last 50 spots every minute: an old one comes back after the dedup forgot it.
        var saved = await store.SaveAsync([Pota("1")], ct);

        Assert.Empty(saved);
        var row = Assert.Single(await store.LoadSinceAsync(Now.AddHours(-1), ct));
        Assert.Equal((7076.0, Now.AddMinutes(30)), (row.FrequencyKhz, row.SpotTime));
    }

    [Fact]
    public async Task Saving_the_same_spot_again_changes_nothing_and_gives_no_new_seq()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await store.SaveAsync([Pota("1")], ct);

        // The POTA feed keeps sending the latest spot of an activation; the dedup forgets it after 20 min.
        var again = await store.SaveAsync([Pota("1")], ct);

        Assert.Empty(again);
        Assert.Equal(first[0].Seq, Assert.Single(await store.LoadSinceAsync(Now.AddHours(-1), ct)).Seq);
    }

    [Fact]
    public async Task A_DX_spot_merged_with_another_feed_at_the_same_time_is_saved_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await store.SaveAsync([Dx("JA1ABC")], ct);

        var merged = await store.SaveAsync([first[0] with { Feeds = ["DXFun", "HamQTH"] }], ct);

        Assert.True(Assert.Single(merged).Seq > first[0].Seq);
    }

    [Fact]
    public async Task At_startup_the_xOTA_rows_keyed_by_the_program_id_are_removed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        {
            // Rows saved before the activation key (27/10/2026): the POTA spot id as key.
            await connection.ExecuteAsync("""
                insert into dx_spots (program, spot_key, callsign, frequency_khz, spot_time, feeds) values
                  ('pota', '59545154', 'K8RLE', 7074, now(), '{POTA}'),
                  ('dx', 'JA1ABC_14025.0_202610271200', 'JA1ABC', 14025, now(), '{DXFun}');
                """);
        }
        await store.SaveAsync([Pota("1")], ct);

        await store.EnsureSchemaAsync(ct);

        var keys = (await store.LoadSinceAsync(Now.AddDays(-1), ct)).Select(s => s.SpotKey).Order().ToArray();
        Assert.Equal(["JA1ABC_14025.0_202610271200", "K8RLE_US-6701_40m"], keys);
    }

    private static Spot Pota(string id) => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Pota,
        SpotKey = id,
        Callsign = "K8RLE",
        FrequencyKhz = 7074,
        SpotTime = Now,
        Feeds = ["POTA"],
        Mode = "FT8",
        Reference = "US-6701",
    });

    private static void AssertSame(Spot expected, Spot actual)
    {
        Assert.Equal(expected with { Feeds = [], Spotters = [], Seq = null }, actual with { Feeds = [], Spotters = [], Seq = null });
        Assert.Equal(expected.Feeds, actual.Feeds);
        Assert.Equal(expected.Spotters, actual.Spotters);
    }
}
