using Dapper;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.References;
using Npgsql;

namespace HamQso.Spots.Infrastructure.Catalog;

/// <summary>
/// The catalog file in the archive's Postgres (F53.3): one row, replaced at
/// every rebuild, so a restart serves it at once without reading Supabase.
/// Creates its table when needed (idempotent).
/// </summary>
public sealed class PostgresReferenceCatalogStore(NpgsqlDataSource dataSource) : IReferenceCatalogStore
{
    private const string Schema =
        "create table if not exists reference_catalog (id integer primary key check (id = 1), " +
        "generated_at timestamptz not null, etag text not null, reference_count integer not null, body bytea not null)";

    public async Task<ReferenceCatalogFile?> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(Schema, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "select body as Body, etag as ETag, generated_at as GeneratedAt, reference_count as Count from reference_catalog where id = 1",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : new ReferenceCatalogFile(row.Body, row.ETag, new DateTimeOffset(row.GeneratedAt, TimeSpan.Zero), row.Count);
    }

    public async Task SaveAsync(ReferenceCatalogFile file, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(Schema, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "insert into reference_catalog (id, generated_at, etag, reference_count, body) values (1, @GeneratedAt, @ETag, @Count, @Gzip) " +
            "on conflict (id) do update set generated_at = excluded.generated_at, etag = excluded.etag, " +
            "reference_count = excluded.reference_count, body = excluded.body",
            new { GeneratedAt = file.GeneratedAt.UtcDateTime, file.ETag, file.Count, file.Gzip },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed record Row(byte[] Body, string ETag, DateTime GeneratedAt, int Count);
}
