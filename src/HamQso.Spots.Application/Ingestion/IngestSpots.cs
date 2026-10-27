using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Ingestion;

/// <summary>
/// Receive spots, drop duplicates, enrich, save, publish, alert. One batch at
/// a time, whatever source it comes from. A failing step is logged and the
/// others go on: the broker keeps serving even when the archive is down.
/// </summary>
public sealed partial class IngestSpots(
    ISpotDeduplicator deduplicator,
    ISpotStore store,
    ISpotPublisher publisher,
    IAlertNotifier alerts,
    TimeProvider time,
    ILogger<IngestSpots> logger) : IDisposable
{
    /// <summary>Spots in the retained DX lists, all topics together.</summary>
    public const int RecentDxCapacity = 200;

    /// <summary>Older spots (a telnet backlog after a reconnect) never trigger an alert.</summary>
    public static readonly TimeSpan AlertFreshness = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly RecentDxSpots recentDx = new(RecentDxCapacity);
    private readonly Dictionary<SpotProgram, Spot[]> snapshots = [];

    /// <summary>Reloads the dedup and the DX lists from the archive, then republishes the DX topics.</summary>
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
            var topics = recentDx.Apply(archived.Where(s => s.Program == SpotProgram.Dx));
            await PublishDxAsync(topics, cancellationToken).ConfigureAwait(false);
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

            if (changed.Length > 0) await RunAsync("save", () => store.SaveAsync(changed, cancellationToken)).ConfigureAwait(false);

            if (batch.SnapshotOf is { } program)
            {
                await PublishSnapshotAsync(program, results.Select(r => r.Spot).ToArray(), cancellationToken).ConfigureAwait(false);
            }
            else if (changed.Length > 0)
            {
                await PublishDxAsync(recentDx.Apply(changed), cancellationToken).ConfigureAwait(false);
            }

            var alertable = fresh.Where(s => time.GetUtcNow() - s.SpotTime <= AlertFreshness).ToArray();
            if (alertable.Length > 0) await RunAsync("alerts", () => alerts.NotifyAsync(alertable, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task PublishDxAsync(IReadOnlySet<string> topics, CancellationToken cancellationToken)
    {
        foreach (var topic in topics)
        {
            await RunAsync("publish", () => publisher.PublishAsync(topic, recentDx.ListOf(topic), cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>An xOTA list is republished only when something in it changed.</summary>
    private async Task PublishSnapshotAsync(SpotProgram program, IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
    {
        var ordered = spots.OrderByDescending(s => s.SpotTime).ToArray();
        if (snapshots.TryGetValue(program, out var last) && SameList(last, ordered)) return;
        snapshots[program] = ordered;
        await RunAsync("publish", () => publisher.PublishAsync(SpotTopics.OfProgram(program), ordered, cancellationToken)).ConfigureAwait(false);
    }

    private static bool SameList(Spot[] a, Spot[] b) =>
        a.Length == b.Length && a.Zip(b).All(p => p.First.SpotKey == p.Second.SpotKey
            && p.First.FrequencyKhz.Equals(p.Second.FrequencyKhz)
            && p.First.Mode == p.Second.Mode
            && p.First.Comment == p.Second.Comment
            && p.First.SpotTime == p.Second.SpotTime);

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
