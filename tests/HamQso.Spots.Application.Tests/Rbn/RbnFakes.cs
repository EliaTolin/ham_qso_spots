using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Tests.Rbn;

internal static class RbnLines
{
    public static readonly DateTimeOffset At = new(2026, 11, 6, 14, 32, 12, 418, TimeSpan.Zero);

    /// <summary>A CW spot of <paramref name="call"/> by <paramref name="skimmer"/> (with its "-#").</summary>
    public static RbnLine Line(string call, string skimmer = "OK4YL-#", double khz = 14045.1, DateTimeOffset? at = null)
    {
        var raw = $"DX de {skimmer}:   {khz.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}  {call}  CW  17 dB  23 WPM  CQ  1432Z";
        return new RbnLine(raw, RbnSpotLine.Parse(raw)!, at ?? At);
    }
}

/// <summary>An RBN feed driven by the test: <see cref="Push"/> lines, set the status with <see cref="SetStatus"/>.</summary>
internal sealed class FakeRbnFeed : IRbnFeed
{
    private readonly List<Channel<RbnLine>> readers = [];
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RbnFeedStatus status = new(RbnFeedState.Up, new DateTimeOffset(2026, 11, 6, 3, 12, 44, TimeSpan.Zero));

    public IReadOnlyList<RbnLine> Preloaded { get; init; } = [];

    public int Readers
    {
        get
        {
            lock (readers) return readers.Count;
        }
    }

    public RbnFeedStatus Status => status;

    public void SetStatus(RbnFeedStatus value)
    {
        status = value;
        Interlocked.Exchange(ref changed, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    public Task WaitForChangeAsync(CancellationToken cancellationToken) => changed.Task.WaitAsync(cancellationToken);

    public void Push(RbnLine line)
    {
        lock (readers)
        {
            foreach (var r in readers) r.Writer.TryWrite(line);
        }
    }

    public void Complete()
    {
        lock (readers)
        {
            foreach (var r in readers) r.Writer.TryComplete();
        }
    }

    public async IAsyncEnumerable<RbnLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<RbnLine>();
        foreach (var line in Preloaded) channel.Writer.TryWrite(line);
        lock (readers) readers.Add(channel);
        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken)) yield return line;
        }
        finally
        {
            lock (readers) readers.Remove(channel);
        }
    }
}

internal sealed class FakeActivityStore : IRbnActivityStore
{
    public List<ActivityMinute> Rows { get; } = [];

    public Exception? Failure { get; set; }

    public (string Band, DateTimeOffset From, DateTimeOffset To)? LastRead { get; private set; }

    public Task AddAsync(IReadOnlyCollection<ActivityMinute> minutes, CancellationToken cancellationToken)
    {
        if (Failure is not null) return Task.FromException(Failure);
        lock (Rows) Rows.AddRange(minutes);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityMinute>> ReadAsync(string band, DateTimeOffset fromMinute, DateTimeOffset until, CancellationToken cancellationToken)
    {
        LastRead = (band, fromMinute, until);
        lock (Rows) return Task.FromResult<IReadOnlyList<ActivityMinute>>([.. Rows]);
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken) => Task.FromResult(0);
}

internal sealed class FakeNodeSource : IRbnNodeSource
{
    public RbnNodeList? Next { get; set; }

    public Exception? Failure { get; set; }

    public Task<RbnNodeList> FetchAsync(CancellationToken cancellationToken) =>
        Failure is not null ? Task.FromException<RbnNodeList>(Failure) : Task.FromResult(Next!);
}

internal sealed class FakeNodeStore : IRbnNodeStore
{
    public SavedRbnNodes? Saved { get; set; }

    public int Replaced { get; private set; }

    public Task<SavedRbnNodes?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

    public Task ReplaceAsync(IReadOnlyList<RbnNode> nodes, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
    {
        Saved = new SavedRbnNodes(nodes, fetchedAt);
        Replaced++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingHealth : ISourceHealth
{
    public List<(string Source, bool Ok, string? Reason)> Calls { get; } = [];

    public void Succeeded(string source) => Calls.Add((source, true, null));

    public void Failed(string source, string reason) => Calls.Add((source, false, reason));
}
