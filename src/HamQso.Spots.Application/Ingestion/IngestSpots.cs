using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Ingestion;

/// <summary>
/// Receive spots, drop duplicates, enrich, save, publish what changed, alert.
/// One batch at a time, whatever source it comes from. The archive gives every
/// saved spot its seq, so only saved spots are published (as news: the apps
/// get the starting list from GET /v1/spots). The alerts run side by side and
/// never wait for the archive or the broker (B21).
/// </summary>
public sealed partial class IngestSpots(
    ISpotDeduplicator deduplicator,
    ISpotStore store,
    ISpotPublisher publisher,
    IAlertNotifier alerts,
    TimeProvider time,
    ILogger<IngestSpots> logger) : IDisposable
{
    /// <summary>Older spots (a telnet backlog after a reconnect) never trigger an alert.</summary>
    public static readonly TimeSpan AlertFreshness = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Reloads the dedup from the archive, so a restart neither republishes nor re-alerts.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - (2 * DuplicateRule.Window);
        IReadOnlyList<Spot> archived;
        try
        {
            archived = await store.LoadSinceAsync(since, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogStepFailed(logger, e, "load archive");
            return;
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            deduplicator.Seed(archived);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task HandleAsync(SpotBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = batch.Spots.Select(SpotEnrichment.Enrich).Select(deduplicator.Accept).ToArray();
            var changed = results
                .Where(r => r.Outcome != DedupOutcome.Unchanged)
                .GroupBy(r => (r.Spot.Program, r.Spot.SpotKey))
                .Select(g => g.Last().Spot)
                .ToArray();
            var fresh = results.Where(r => r.Outcome == DedupOutcome.New).Select(r => r.Spot).ToArray();

            var publishing = changed.Length > 0 ? SaveAndPublishAsync(changed, cancellationToken) : Task.CompletedTask;
            var alertable = fresh.Where(s => time.GetUtcNow() - s.SpotTime <= AlertFreshness).ToArray();
            var alerting = alertable.Length > 0 ? RunAsync("alerts", () => alerts.NotifyAsync(alertable, cancellationToken)) : Task.CompletedTask;

            await Task.WhenAll(publishing, alerting).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveAndPublishAsync(Spot[] changed, CancellationToken cancellationToken)
    {
        IReadOnlyList<Spot> saved;
        try
        {
            saved = await store.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Without a seq the news cannot be ordered: not published.
            LogStepFailed(logger, e, "save");
            return;
        }
        foreach (var topic in saved.GroupBy(SpotTopics.Of))
        {
            await RunAsync("publish", () => publisher.PublishAsync(topic.Key, [.. topic], cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(string step, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogStepFailed(logger, e, step);
        }
    }

    public void Dispose() => gate.Dispose();

    [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion step {Step} failed")]
    private static partial void LogStepFailed(ILogger logger, Exception exception, string step);
}
