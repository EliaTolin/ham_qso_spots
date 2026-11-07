using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Application.Rbn;

/// <summary>
/// Reads the RBN node list (every 30 minutes, by the Host) and keeps the last
/// good read, saved and in <see cref="RbnNodeCatalog"/>. A page that changed
/// format or has no node, or a site that does not answer, leaves the last good
/// read in place and reports "rbn_nodes" failing.
/// </summary>
public sealed class RefreshRbnNodes(IRbnNodeSource source, IRbnNodeStore store, RbnNodeCatalog catalog, ISourceHealth health, TimeProvider time)
{
    /// <summary>Name of the node list in /health.</summary>
    public const string HealthName = "rbn_nodes";

    /// <summary>At start: the saved list, until the first read.</summary>
    public async Task LoadSavedAsync(CancellationToken cancellationToken)
    {
        if (await store.LoadAsync(cancellationToken).ConfigureAwait(false) is { } saved) catalog.Set(saved.Nodes, saved.FetchedAt);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        RbnNodeList list;
        try
        {
            list = await source.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            health.Failed(HealthName, e.Message);
            return;
        }
        if (list.FormatChanged || list.Nodes.Count == 0)
        {
            catalog.MarkFormatChanged();
            health.Failed(HealthName, "format changed");
            return;
        }
        var now = time.GetUtcNow();
        await store.ReplaceAsync(list.Nodes, now, cancellationToken).ConfigureAwait(false);
        catalog.Set(list.Nodes, now);
        health.Succeeded(HealthName);
    }
}
