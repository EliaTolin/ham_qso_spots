using System.Data;
using Dapper;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Npgsql;

namespace HamQso.Spots.Infrastructure.Postgres;

/// <summary>
/// The archive in Postgres (dx_spots, 7 days), next to the server on Coolify.
/// Every save gives the spot a new seq (dx_spots_seq_counter). Saves come one
/// batch at a time (IngestSpots), so a reader never sees a seq whose earlier
/// seqs are not committed yet. Saves tell /health ("archive").
/// </summary>
public sealed class PostgresSpotStore(NpgsqlDataSource dataSource, ISourceHealth health) : ISpotStore, ISpotReader
{
    /// <summary>Name of the archive in /health.</summary>
    public const string HealthName = "archive";

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
        "latitude = excluded.latitude, longitude = excluded.longitude, comment = excluded.comment, spotter_locator = excluded.spotter_locator, " +
        "seq = nextval('dx_spots_seq_counter') " +
        "returning seq";

    private const string Select =
        "select program, seq, spot_key as SpotKey, callsign, frequency_khz as FrequencyKhz, spot_time as SpotTime, feeds, spotters, band, mode, " +
        "mode_source as ModeSource, mode_category as ModeCategory, reference, location, dxcc, continent, latitude, longitude, comment, " +
        "spotter_locator as SpotterLocator from dx_spots";

    /// <summary>Program, bands and modes of a <see cref="SpotFilter"/>; "other" is a null band or mode.</summary>
    private const string Where =
        " where program = @Program" +
        " and (@AnyBand or band = any(@Bands) or (@OtherBand and band is null))" +
        " and (@AnyMode or mode_category = any(@Modes) or (@OtherMode and mode_category is null))";

    /// <summary>Creates the table and indexes when missing, upgrades an older one (Postgres/Schema.sql).</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        using var stream = typeof(PostgresSpotStore).Assembly.GetManifestResourceStream("Schema.sql")
            ?? throw new InvalidOperationException("Schema.sql is not embedded");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Spot>> SaveAsync(IReadOnlyCollection<Spot> spots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spots);
        if (spots.Count == 0) return [];
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var saved = new List<Spot>(spots.Count);
            foreach (var spot in spots)
            {
                var seq = await connection.ExecuteScalarAsync<long>(new CommandDefinition(Upsert, SpotRow.From(spot), transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                saved.Add(spot with { Seq = seq });
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            health.Succeeded(HealthName);
            return saved;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            health.Failed(HealthName, e.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<Spot>> LoadSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SpotRow>(new CommandDefinition($"{Select} where spot_time >= @Since order by spot_time", new { Since = since.UtcDateTime }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(r => r.ToSpot()).ToArray();
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition("delete from dx_spots where spot_time < @Before", new { Before = before.UtcDateTime }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task<SpotPage> LatestAsync(SpotFilter filter, int limit, CancellationToken cancellationToken) =>
        PageAsync($"{Select}{Where} order by spot_time desc, seq desc limit @Limit", filter, limit, 0, cancellationToken);

    public Task<SpotPage> ChangedSinceAsync(SpotFilter filter, long sinceSeq, int limit, CancellationToken cancellationToken) =>
        PageAsync($"{Select}{Where} and seq > @Since order by seq limit @Limit", filter, limit, sinceSeq, cancellationToken);

    /// <summary>The spots and the current seq, read in one snapshot so they agree.</summary>
    private async Task<SpotPage> PageAsync(string sql, SpotFilter filter, int limit, long since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var bands = filter.Bands.Where(b => b != SpotTopics.Other).ToArray();
        var modes = filter.Modes.Where(m => m != SpotTopics.Other).ToArray();
        var args = new
        {
            Program = SpotJson.Name(filter.Program),
            AnyBand = filter.Bands.Count == 0,
            Bands = bands,
            OtherBand = filter.Bands.Contains(SpotTopics.Other),
            AnyMode = filter.Modes.Count == 0,
            Modes = modes,
            OtherMode = filter.Modes.Contains(SpotTopics.Other),
            Limit = limit,
            Since = since,
        };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var seq = await connection.ExecuteScalarAsync<long>(new CommandDefinition("select coalesce(max(seq), 0) from dx_spots", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SpotRow>(new CommandDefinition(sql, args, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SpotPage(seq, false, [.. rows.Select(r => r.ToSpot())]);
    }
}
