using Dapper;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Rbn;
using Npgsql;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>rbn_nodes and rbn_skimmer_activity in the spots' Postgres (Rbn/RbnSchema.sql).</summary>
public sealed class PostgresRbnStore(NpgsqlDataSource dataSource) : IRbnNodeStore, IRbnActivityStore
{
    /// <summary>Creates the tables when missing (RbnSchema.sql, idempotent).</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        using var stream = typeof(PostgresRbnStore).Assembly.GetManifestResourceStream("RbnSchema.sql")
            ?? throw new InvalidOperationException("RbnSchema.sql is not embedded");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<SavedRbnNodes?> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await connection.QueryAsync<NodeRow>(new CommandDefinition(
            "select call, locator, bands, online, last_seen_at as LastSeenAt, fetched_at as FetchedAt from rbn_nodes",
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();
        if (rows.Length == 0) return null;
        return new SavedRbnNodes(
            [.. rows.Select(r => new RbnNode(r.Call, r.Locator, r.Bands, r.Online, Utc(r.LastSeenAt)))],
            Utc(rows.Max(r => r.FetchedAt)));
    }

    public async Task ReplaceAsync(IReadOnlyList<RbnNode> nodes, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("delete from rbn_nodes", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        foreach (var n in nodes)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into rbn_nodes (call, locator, bands, online, last_seen_at, fetched_at) values (@Call, @Locator, @Bands, @Online, @LastSeenAt, @FetchedAt) on conflict (call) do nothing",
                new { n.Call, n.Locator, Bands = n.Bands.ToArray(), n.Online, LastSeenAt = n.LastSeenAt?.UtcDateTime, FetchedAt = fetchedAt.UtcDateTime },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(IReadOnlyCollection<ActivityMinute> minutes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        if (minutes.Count == 0) return;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "insert into rbn_skimmer_activity (skimmer, band, minute, spots, last_spot_at) " +
            "select * from unnest(@Skimmers, @Bands, @Minutes, @Spots, @Lasts) " +
            "on conflict (band, minute, skimmer) do update set spots = rbn_skimmer_activity.spots + excluded.spots, " +
            "last_spot_at = greatest(rbn_skimmer_activity.last_spot_at, excluded.last_spot_at)",
            new
            {
                Skimmers = minutes.Select(m => m.Skimmer).ToArray(),
                Bands = minutes.Select(m => m.Band).ToArray(),
                Minutes = minutes.Select(m => m.Minute.UtcDateTime).ToArray(),
                Spots = minutes.Select(m => m.Spots).ToArray(),
                Lasts = minutes.Select(m => m.LastSpotAt.UtcDateTime).ToArray(),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityMinute>> ReadAsync(string band, DateTimeOffset fromMinute, DateTimeOffset until, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ActivityRow>(new CommandDefinition(
            "select skimmer, band, minute, spots, last_spot_at as LastSpotAt from rbn_skimmer_activity where band = @Band and minute >= @From and minute <= @Until",
            new { Band = band, From = fromMinute.UtcDateTime, Until = until.UtcDateTime },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return [.. rows.Select(r => new ActivityMinute(r.Skimmer, r.Band, Utc(r.Minute), r.Spots, Utc(r.LastSpotAt)))];
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition("delete from rbn_skimmer_activity where minute < @Before", new { Before = before.UtcDateTime }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static DateTimeOffset Utc(DateTime t) => new(DateTime.SpecifyKind(t, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? t) => t is { } v ? Utc(v) : null;

    private sealed class NodeRow
    {
        public string Call { get; init; } = "";

        public string? Locator { get; init; }

        public string[] Bands { get; init; } = [];

        public bool Online { get; init; }

        public DateTime? LastSeenAt { get; init; }

        public DateTime FetchedAt { get; init; }
    }

    private sealed record ActivityRow(string Skimmer, string Band, DateTime Minute, int Spots, DateTime LastSpotAt);
}
