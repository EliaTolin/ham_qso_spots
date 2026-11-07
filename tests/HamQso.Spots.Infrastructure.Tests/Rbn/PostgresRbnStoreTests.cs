using HamQso.Spots.Domain.Rbn;
using HamQso.Spots.Infrastructure.Rbn;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HamQso.Spots.Infrastructure.Tests.Rbn;

/// <summary>rbn_nodes and rbn_skimmer_activity against a real Postgres in Docker.</summary>
public sealed class PostgresRbnStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset M = new(2026, 11, 6, 14, 31, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private NpgsqlDataSource dataSource = null!;
    private PostgresRbnStore store = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(Ct);
        dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        store = new PostgresRbnStore(dataSource);
        await store.EnsureSchemaAsync(Ct);
        await store.EnsureSchemaAsync(Ct); // idempotent, as at every start
    }

    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Before_the_first_read_there_is_no_saved_list() => Assert.Null(await store.LoadAsync(Ct));

    [Fact]
    public async Task A_saved_list_comes_back_and_a_new_read_replaces_it_whole()
    {
        await store.ReplaceAsync([new RbnNode("OK4YL", "jn79fx", ["20m", "40m"], true, M), new RbnNode("KM7W", null, [], false, null)], M, Ct);
        await store.ReplaceAsync([new RbnNode("OK4YL", "JN79FX", ["20m"], true, M.AddMinutes(30))], M.AddMinutes(30), Ct);

        var saved = (await store.LoadAsync(Ct))!;

        Assert.Equal(M.AddMinutes(30), saved.FetchedAt);
        var node = Assert.Single(saved.Nodes);
        Assert.Equal(("OK4YL", "JN79FX", true, M.AddMinutes(30)), (node.Call, node.Locator, node.Online, node.LastSeenAt));
        Assert.Equal(["20m"], node.Bands);
    }

    [Fact]
    public async Task Activity_of_the_same_skimmer_band_and_minute_adds_up_with_the_latest_spot()
    {
        await store.AddAsync([new ActivityMinute("OK4YL", "20m", M, 10, M.AddSeconds(20)), new ActivityMinute("OK4YL", "40m", M, 1, M)], Ct);
        await store.AddAsync([new ActivityMinute("OK4YL", "20m", M, 32, M.AddSeconds(40))], Ct);
        await store.AddAsync([new ActivityMinute("OK4YL", "20m", M, 1, M.AddSeconds(5))], Ct);

        var rows = await store.ReadAsync("20m", M, M.AddMinutes(1), Ct);

        Assert.Equal(new ActivityMinute("OK4YL", "20m", M, 43, M.AddSeconds(40)), Assert.Single(rows));
    }

    [Fact]
    public async Task Activity_is_read_for_one_band_from_a_minute_to_a_moment()
    {
        await store.AddAsync(
        [
            new ActivityMinute("A1A", "20m", M.AddMinutes(-1), 1, M.AddMinutes(-1)),
            new ActivityMinute("B1B", "20m", M, 1, M),
            new ActivityMinute("C1C", "20m", M.AddMinutes(2), 1, M.AddMinutes(2)),
            new ActivityMinute("D1D", "20m", M.AddMinutes(3), 1, M.AddMinutes(3)),
        ], Ct);

        var rows = await store.ReadAsync("20m", M, M.AddMinutes(2).AddSeconds(30), Ct);

        Assert.Equal(["B1B", "C1C"], rows.Select(r => r.Skimmer).Order());
    }

    [Fact]
    public async Task Activity_older_than_asked_is_deleted()
    {
        await store.AddAsync([new ActivityMinute("A1A", "20m", M.AddHours(-49), 1, M.AddHours(-49)), new ActivityMinute("B1B", "20m", M, 1, M)], Ct);

        Assert.Equal(1, await store.DeleteOlderThanAsync(M.AddHours(-48), Ct));
        Assert.Equal(["B1B"], (await store.ReadAsync("20m", M.AddHours(-50), M.AddMinutes(1), Ct)).Select(r => r.Skimmer));
    }

    [Fact]
    public async Task A_big_batch_is_written_at_once()
    {
        var rows = Enumerable.Range(0, 3000).Select(i => new ActivityMinute($"S{i}", "20m", M, 1, M)).ToArray();

        await store.AddAsync(rows, Ct);

        Assert.Equal(3000, (await store.ReadAsync("20m", M, M.AddMinutes(1), Ct)).Count);
    }
}
