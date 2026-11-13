using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Protection;

namespace HamQso.Spots.Application.Protection;

/// <summary>
/// The protected callsigns now, read from Supabase at intervals. When a read fails the last set stays
/// (a protection that stops because Supabase blinked would be worse than a stale list).
/// </summary>
public sealed class ProtectedCallsignRegistry(IProtectedCallsignSource source, ISourceHealth health)
{
    public const string HealthName = "protection_callsigns";

    private volatile ProtectedCallsigns current = ProtectedCallsigns.None;
    private int version;

    public ProtectedCallsigns Current => current;

    /// <summary>Grows at every change of the set, so a reader knows to look again.</summary>
    public int Version => Volatile.Read(ref version);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var next = new ProtectedCallsigns(await source.LoadAsync(cancellationToken).ConfigureAwait(false));
            if (!next.Bases.ToHashSet().SetEquals(current.Bases))
            {
                current = next;
                Interlocked.Increment(ref version);
            }
            health.Succeeded(HealthName);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            health.Failed(HealthName, e.Message);
        }
    }
}
