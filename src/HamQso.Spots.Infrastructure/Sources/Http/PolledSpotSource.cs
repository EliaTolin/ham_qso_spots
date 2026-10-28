using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>
/// Asks one <see cref="IPolledFeed"/> every <c>interval</c>, whatever the number
/// of apps connected: one request per interval for the whole of HamQSO.
/// A failed request is reported and retried at the next interval. With an
/// <see cref="IPolledFeed.EpochUrl"/> only the epoch is asked every interval,
/// and the list only when the epoch differs from the one of the last list read.
/// </summary>
public sealed class PolledSpotSource(IPolledFeed feed, HttpClient http, TimeSpan interval, TimeProvider time) : ISpotSource
{
    private string? epoch;

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
            var current = feed.EpochUrl is { } epochUrl
                ? (await http.GetStringAsync(epochUrl, cancellationToken).ConfigureAwait(false)).Trim()
                : null;
            if (current is not null && current == epoch)
            {
                health.Succeeded(Name);
                return null;
            }
            var body = await http.GetStringAsync(feed.Url, cancellationToken).ConfigureAwait(false);
            var spots = feed.Parse(body, time.GetUtcNow());
            epoch = current;
            health.Succeeded(Name);
            return new SpotBatch(Name, spots);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            health.Failed(Name, e.Message);
            return null;
        }
    }
}
