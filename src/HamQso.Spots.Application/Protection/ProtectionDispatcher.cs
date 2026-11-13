using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Protection;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Protection;

/// <summary>
/// Takes the finished uses of <see cref="UseAggregator"/> to the backend, in the limits of
/// <see cref="ProtectionLimits"/>: one batch every 30 s of at most 500 uses (and under 256 KB); a batch that
/// cannot be sent is kept whole and retried with backoff, with the same source_id of every use (5xx or network
/// only: a 4xx is final and the batch is dropped); the queue overflow is dropped and reported once an hour.
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
    private int attempts;

    public int Queued => pending.Count;

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Enqueue(aggregator.Drain(now), now);
        ReportSources();
        if (inFlight is null && !Due(now)) return;
        inFlight ??= BuildBatch();
        if (now < nextAttempt) return;
        await SendAsync(inFlight, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The last chance at a stop: whatever is still open goes out in one try (no retry, no limit of time).</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Enqueue(aggregator.Drain(now, everything: true), now);
        if (inFlight is null && pending.Count > 0) inFlight = BuildBatch();
        if (inFlight is not null) await SendAsync(inFlight, now, cancellationToken).ConfigureAwait(false);
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
        if (dropped == 0 || now - lastOverflowReport < limits.OverflowReportEvery) return;
        lastOverflowReport = now;
        health.Failed(HealthName, $"queue full: {dropped} uses dropped (more reports than the limits allow)");
    }

    private ProtectionBatch BuildBatch()
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
        var failed = probeList.Select(p => (p.Signal, Problem: p.Problem())).Where(p => p.Problem is not null).ToArray();
        return new ProtectionBatch(
            uses,
            [.. probeList.Where(p => failed.All(f => f.Signal != p.Signal)).Select(p => p.Signal.Wire())],
            [.. failed.Select(f => new SourceFailure(f.Signal.Wire(), f.Problem!))]);
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
