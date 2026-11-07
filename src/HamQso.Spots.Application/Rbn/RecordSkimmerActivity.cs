using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Rbn;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Rbn;

/// <summary>The spots of the RBN counted per skimmer, band and minute of arrival, until drained.</summary>
public sealed class SkimmerActivityBuffer
{
    private readonly Lock gate = new();
    private Dictionary<(string Skimmer, string Band, DateTimeOffset Minute), (int Spots, DateTimeOffset Last)> counts = [];

    /// <summary>Counts the spot (a frequency outside the amateur bands is not counted).</summary>
    public void Add(RbnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Bands.Of(line.Spot.Khz) is not { } band) return;
        var key = (line.Spot.Skimmer, band, SkimmerActivity.MinuteOf(line.ReceivedAt));
        lock (gate)
        {
            counts[key] = counts.TryGetValue(key, out var c)
                ? (c.Spots + 1, c.Last > line.ReceivedAt ? c.Last : line.ReceivedAt)
                : (1, line.ReceivedAt);
        }
    }

    /// <summary>Puts back counts that could not be saved.</summary>
    public void Restore(IEnumerable<ActivityMinute> minutes)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        lock (gate)
        {
            foreach (var m in minutes)
            {
                var key = (m.Skimmer, m.Band, m.Minute);
                counts[key] = counts.TryGetValue(key, out var c)
                    ? (c.Spots + m.Spots, c.Last > m.LastSpotAt ? c.Last : m.LastSpotAt)
                    : (m.Spots, m.LastSpotAt);
            }
        }
    }

    /// <summary>The counts since the last drain; the buffer is empty after.</summary>
    public IReadOnlyList<ActivityMinute> Drain()
    {
        Dictionary<(string Skimmer, string Band, DateTimeOffset Minute), (int Spots, DateTimeOffset Last)> taken;
        lock (gate)
        {
            taken = counts;
            counts = [];
        }
        return [.. taken.Select(kv => new ActivityMinute(kv.Key.Skimmer, kv.Key.Band, kv.Key.Minute, kv.Value.Spots, kv.Value.Last))];
    }
}

/// <summary>
/// The background reader that keeps the RBN connection open for good (plan F71,
/// A2): every spot of the world is counted (<see cref="SkimmerActivityBuffer"/>)
/// and the counts are added to the store at every flush. Counts that cannot be
/// saved wait for the next flush.
/// </summary>
public sealed partial class RecordSkimmerActivity(IRbnFeed feed, IRbnActivityStore store, ILogger<RecordSkimmerActivity> logger)
{
    private readonly SkimmerActivityBuffer buffer = new();

    public async Task RunAsync(TimeSpan flushEvery, CancellationToken cancellationToken)
    {
        var flushing = FlushLoopAsync(flushEvery, cancellationToken);
        try
        {
            await foreach (var line in feed.ReadAsync(cancellationToken).ConfigureAwait(false)) buffer.Add(line);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        await flushing.ConfigureAwait(false);
    }

    private async Task FlushLoopAsync(TimeSpan every, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(every, cancellationToken).ConfigureAwait(false);
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var rows = buffer.Drain();
        if (rows.Count == 0) return;
        try
        {
            await store.AddAsync(rows, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            buffer.Restore(rows);
            LogFailed(logger, rows.Count, e.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "RBN activity: {Rows} rows not saved, kept for the next try: {Reason}")]
    private static partial void LogFailed(ILogger logger, int rows, string reason);
}
