using HamQso.Spots.Domain.References;

namespace HamQso.Spots.Application.Ports;

/// <summary>The active references of HamQSO (Supabase <c>xota_references</c>), every one (F53.3).</summary>
public interface IReferenceCatalogSource
{
    Task<IReadOnlyList<CatalogReference>> ReadActiveAsync(CancellationToken cancellationToken);
}

/// <summary>Where the catalog file is kept across restarts.</summary>
public interface IReferenceCatalogStore
{
    Task<ReferenceCatalogFile?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ReferenceCatalogFile file, CancellationToken cancellationToken);
}
