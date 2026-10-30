using HamQso.Spots.Domain.References;
using HamQso.Spots.Infrastructure.Catalog;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HamQso.Spots.Infrastructure.Tests.Catalog;

/// <summary>F53.3: the catalog file survives a restart, in the archive's Postgres.</summary>
public sealed class PostgresReferenceCatalogStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private NpgsqlDataSource dataSource = null!;
    private PostgresReferenceCatalogStore store = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        store = new PostgresReferenceCatalogStore(dataSource);
    }

    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private static ReferenceCatalogFile File(string code, int day) => ReferenceCatalogFormat.Write(
        [new CatalogReference(code, "POTA", "Park", null, 248, null, 45, 11, null, null)],
        new DateTimeOffset(2026, 10, day, 3, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Nothing_saved_yet_is_null()
    {
        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_saved_file_comes_back_and_a_new_one_replaces_it()
    {
        await store.SaveAsync(File("IT-0001", 23), TestContext.Current.CancellationToken);
        var newer = File("IT-0002", 30);
        await store.SaveAsync(newer, TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(newer.Gzip, loaded!.Gzip);
        Assert.Equal((newer.ETag, newer.GeneratedAt, newer.Count), (loaded.ETag, loaded.GeneratedAt, loaded.Count));
    }
}
