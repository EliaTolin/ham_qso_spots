using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.References;

namespace HamQso.Spots.Application.Catalog;

/// <summary>The catalog file the server gives the apps now; null until the first one exists.</summary>
public sealed class CurrentReferenceCatalog
{
    private volatile ReferenceCatalogFile? file;

    public ReferenceCatalogFile? File
    {
        get => file;
        set => file = value;
    }
}

/// <summary>
/// F53.3: the world reference catalog for the apps. At start the saved file is
/// served; when there is none, or it is older than <see cref="Every"/>, the
/// active references are read again from HamQSO and the file rebuilt (Supabase
/// is on the Free plan: one read a week for everyone instead of one per
/// phone). A read with fewer than <see cref="MinimumCount"/> references (a
/// broken import) or a failure keeps the file there is. The same references
/// keep the same ETag: only the time is saved again.
/// </summary>
public sealed class RefreshReferenceCatalog(
    IReferenceCatalogSource source,
    IReferenceCatalogStore store,
    CurrentReferenceCatalog current,
    TimeProvider time)
{
    public static readonly TimeSpan Every = TimeSpan.FromDays(7);

    /// <summary>Fewer than this is a broken read (338,767 active on 29/10/2026).</summary>
    public int MinimumCount { get; init; } = 100_000;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        current.File ??= await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (current.File is { } file && now - file.GeneratedAt < Every) return;

        var references = await source.ReadActiveAsync(cancellationToken).ConfigureAwait(false);
        if (references.Count < MinimumCount) return;
        var built = ReferenceCatalogFormat.Write(references, now);
        await store.SaveAsync(built, cancellationToken).ConfigureAwait(false);
        current.File = built;
    }
}
