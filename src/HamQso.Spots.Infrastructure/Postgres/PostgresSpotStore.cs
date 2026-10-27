using Dapper;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Npgsql;

namespace HamQso.Spots.Infrastructure.Postgres;

/// <summary>The archive in Postgres (dx_spots, 7 days), next to the server on Coolify.</summary>
public sealed class PostgresSpotStore(NpgsqlDataSource dataSource) : ISpotStore
{
    private const string Columns =
        "program, spot_key, callsign, frequency_khz, spot_time, feeds, spotters, band, mode, mode_source, mode_category, " +
        "reference, location, dxcc, continent, latitude, longitude, comment, spotter_locator";

    private const string Upsert =
        $"insert into dx_spots ({Columns}) values (@Program, @SpotKey, @Callsign, @FrequencyKhz, @SpotTime, @Feeds, @Spotters, " +
        "@Band, @Mode, @ModeSource, @ModeCategory, @Reference, @Location, @Dxcc, @Continent, @Latitude, @Longitude, @Comment, @SpotterLocator) " +
        "on conflict (program, spot_key) do update set callsign = excluded.callsign, frequency_khz = excluded.frequency_khz, " +
        "spot_time = excluded.spot_time, feeds = excluded.feeds, spotters = excluded.spotters, band = excluded.band, " +
        "mode = excluded.mode, mode_source = excluded.mode_source, mode_category = excluded.mode_category, " +
        "reference = excluded.reference, location = excluded.location, dxcc = excluded.dxcc, continent = excluded.continent, " +
        "latitude = excluded.latitude, longitude = excluded.longitude, comment = excluded.comment, spotter_locator = excluded.spotter_locator";

    private const string Select =
        "select program, spot_key as SpotKey, callsign, frequency_khz as FrequencyKhz, spot_time as SpotTime, feeds, spotters, band, mode, " +
        "mode_source as ModeSource, mode_category as ModeCategory, reference, location, dxcc, continent, latitude, longitude, comment, " +
        "spotter_locator as SpotterLocator from dx_spots where spot_time >= @Since order by spot_time";

    /// <summary>Creates the table and indexes when missing (Postgres/Schema.sql).</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        using var stream = typeof(PostgresSpotStore).Assembly.GetManifestResourceStream("Schema.sql")
            ?? throw new InvalidOperationException("Schema.sql is not embedded");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SaveAsync(IReadOnlyCollection<Spot> spots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spots);
        if (spots.Count == 0) return;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(Upsert, spots.Select(SpotRow.From).ToArray(), transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Spot>> LoadSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SpotRow>(new CommandDefinition(Select, new { Since = since.UtcDateTime }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(r => r.ToSpot()).ToArray();
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition("delete from dx_spots where spot_time < @Before", new { Before = before.UtcDateTime }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
