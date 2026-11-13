using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Protection;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Protection;

/// <summary>
/// Takes the finished uses of <see cref="UseAggregator"/> to the backend, in the limits of
/// <see cref="ProtectionLimits"/>: one batch every 30 s of at most 500 uses (and under 256 KB); a batch that
/// cannot be sent is kept whole and retried with backoff, with the same source_id of every use (5xx or network
/// only: a 4xx is final and the batch is dropped); the queue overflow is dropped and reported once an hour
/// (in /health and as a <c>queue</c> failure in the next batch, which the backend turns into queue_overflow).
/// The health of the sources goes with every batch and at least every 5 minutes. <see cref="TickAsync"/> is
/// called every few seconds by the worker.
/// </summary>
public sealed partial class ProtectionDispatcher(
    UseAggregator aggregator,
    IProtectionUplink uplink,
    IEnumerable<IProtectionProbe> probes,
    ISourceHealth health,
    TimeProvider time,
    ProtectionLimits limits,
    ILogger<ProtectionDispatcher> logger)
{
    /// <summary>Name of the call to the backend in /health.</summary>
    public const string HealthName = "protection";

    private readonly IProtectionProbe[] probeList = [.. probes];
    private readonly Queue<ProtectedUse> pending = new();
    private readonly Dictionary<ProtectionSignal, bool> reported = [];
    private ProtectionBatch? inFlight;
    private DateTimeOffset lastSend = DateTimeOffset.MinValue;
    private DateTimeOffset nextAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset lastOverflowReport = DateTimeOffset.MinValue;
    private DateTimeOffset lastOverflowFailure = DateTimeOffset.MinValue;
    private readonly Dictionary<ProtectionSignal, DateTimeOffset> lastProblemReport = [];
    private int attempts;
    private int droppedUnreported;

    public int Queued => pending.Count;

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Enqueue(aggregator.Drain(now), now);
        ReportSources();
        if (inFlight is null && !Due(now)) return;
        inFlight ??= BuildBatch(now);
        if (now < nextAttempt) return;
        await SendAsync(inFlight, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The last chance at a stop: whatever is still open goes out, as many batches as the queue needs, each one
    /// in the limits of the batch (≤ 500 uses, ≤ 200 KB) and in the time the stop allows (the token). Only on
    /// shutdown the 30 s between batches does not apply: waiting would lose the uses. No retry: a batch that
    /// fails ends the flush (the uses stay unsent).
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Enqueue(aggregator.Drain(now, everything: true), now);
        while (!cancellationToken.IsCancellationRequested && (inFlight is not null || pending.Count > 0))
        {
            inFlight ??= BuildBatch(now);
            var batch = inFlight;
            await SendAsync(batch, now, cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(inFlight, batch)) return;
        }
    }

    private bool Due(DateTimeOffset now) =>
        (pending.Count > 0 && now - lastSend >= limits.BatchEvery) || now - lastSend >= limits.HealthEvery;

    private void Enqueue(IReadOnlyList<ProtectedUse> uses, DateTimeOffset now)
    {
        var dropped = 0;
        foreach (var use in uses)
        {
            if (pending.Count >= limits.MaxQueue) dropped++;
            else pending.Enqueue(use);
        }
        if (dropped == 0) return;
        droppedUnreported += dropped;
        if (now - lastOverflowReport < limits.OverflowReportEvery) return;
        lastOverflowReport = now;
        health.Failed(HealthName, $"queue full: {dropped} uses dropped (more reports than the limits allow)");
    }

    private ProtectionBatch BuildBatch(DateTimeOffset now)
    {
        var uses = new List<ProtectedUse>();
        var bytes = 0;
        while (pending.Count > 0 && uses.Count < limits.MaxUsesPerBatch)
        {
            var size = ProtectionPayload.SizeOf(pending.Peek());
            if (uses.Count > 0 && bytes + size > limits.MaxBatchBytes) break;
            bytes += size;
            uses.Add(pending.Dequeue());
        }
        var problems = probeList.Select(p => (Probe: p, Problem: p.Problem())).Where(p => p.Problem is not null).ToArray();
        var failed = problems.Where(p => ShouldReport(p.Probe, now)).Select(p => new SourceFailure(p.Probe.Signal.Wire(), p.Problem!)).ToList();
        if (droppedUnreported > 0 && now - lastOverflowFailure >= limits.OverflowReportEvery)
        {
            failed.Add(new SourceFailure("queue", $"overflow: {droppedUnreported} uses dropped"));
            droppedUnreported = 0;
            lastOverflowFailure = now;
        }
        return new ProtectionBatch(
            uses,
            [.. probeList.Where(p => problems.All(f => f.Probe.Signal != p.Signal)).Select(p => p.Signal.Wire())],
            failed);
    }

    /// <summary>A probe that asks for a report every so often is in failed at most that often (it is never in checked meanwhile).</summary>
    private bool ShouldReport(IProtectionProbe probe, DateTimeOffset now)
    {
        if (probe.ReportEvery is not { } every) return true;
        if (lastProblemReport.TryGetValue(probe.Signal, out var last) && now - last < every) return false;
        lastProblemReport[probe.Signal] = now;
        return true;
    }

    private async Task SendAsync(ProtectionBatch batch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lastSend = now;
        try
        {
            await uplink.SendAsync(batch, cancellationToken).ConfigureAwait(false);
            inFlight = null;
            attempts = 0;
            nextAttempt = DateTimeOffset.MinValue;
            health.Succeeded(HealthName);
        }
        catch (UplinkRejectedException e)
        {
            // A 4xx will be the same next time: the batch goes, the report stays.
            inFlight = null;
            attempts = 0;
            LogDropped(logger, batch.Uses.Count, e.Status, e.Message);
            health.Failed(HealthName, $"{e.Status}: {e.Message}");
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            attempts++;
            var wait = TimeSpan.FromTicks(Math.Min(limits.RetryUpTo.Ticks, limits.RetryFrom.Ticks << Math.Min(attempts - 1, 10)));
            nextAttempt = now + wait;
            health.Failed(HealthName, e.Message);
        }
    }

    /// <summary>Each source in /health as "protection_spot" and so on, when its state changes.</summary>
    private void ReportSources()
    {
        foreach (var probe in probeList)
        {
            var problem = probe.Problem();
            var name = $"{HealthName}_{probe.Signal.Wire()}";
            if (reported.TryGetValue(probe.Signal, out var wasOk) && wasOk == (problem is null)) continue;
            reported[probe.Signal] = problem is null;
            if (problem is null) health.Succeeded(name);
            else health.Failed(name, problem);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Protection: {Uses} uses dropped, the backend answered {Status}: {Reason}")]
    private static partial void LogDropped(ILogger logger, int uses, int status, string reason);
}
