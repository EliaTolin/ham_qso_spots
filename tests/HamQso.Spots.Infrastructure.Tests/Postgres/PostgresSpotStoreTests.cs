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
    private PostgresSpotStore store = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        store = new PostgresSpotStore(dataSource);
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

    private static void AssertSame(Spot expected, Spot actual)
    {
        Assert.Equal(expected with { Feeds = [], Spotters = [] }, actual with { Feeds = [], Spotters = [] });
        Assert.Equal(expected.Feeds, actual.Feeds);
        Assert.Equal(expected.Spotters, actual.Spotters);
    }
}
