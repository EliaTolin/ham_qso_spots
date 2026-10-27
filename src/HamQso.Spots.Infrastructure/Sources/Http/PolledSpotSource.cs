using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>
/// Asks one <see cref="IPolledFeed"/> every <c>interval</c>, whatever the number
/// of apps connected: one request per interval for the whole of HamQSO.
/// A failed request is reported and retried at the next interval.
/// </summary>
public sealed class PolledSpotSource(IPolledFeed feed, HttpClient http, TimeSpan interval, TimeProvider time) : ISpotSource
{
    public string Name => feed.Name;

    public async IAsyncEnumerable<SpotBatch> ReadAsync(ISourceHealth health, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(health);
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await FetchAsync(health, cancellationToken).ConfigureAwait(false);
            if (batch is not null) yield return batch;
            await Task.Delay(interval, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SpotBatch?> FetchAsync(ISourceHealth health, CancellationToken cancellationToken)
    {
        try
        {
            var body = await http.GetStringAsync(feed.Url, cancellationToken).ConfigureAwait(false);
            var spots = feed.Parse(body, time.GetUtcNow());
            health.Succeeded(Name);
            return new SpotBatch(Name, spots, feed.SnapshotOf);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            health.Failed(Name, e.Message);
            return null;
        }
    }
}
