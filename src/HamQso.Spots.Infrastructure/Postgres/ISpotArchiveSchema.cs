namespace HamQso.Spots.Infrastructure.Postgres;

/// <summary>Creates or upgrades the archive tables at startup.</summary>
public interface ISpotArchiveSchema
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);
}
