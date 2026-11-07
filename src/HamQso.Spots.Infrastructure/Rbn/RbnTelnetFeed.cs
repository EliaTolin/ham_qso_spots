using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sources.Telnet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>
/// One telnet connection to the RBN for all the listeners of the server,
/// logged in with the server's callsign: opened by the first listener, kept
/// <see cref="RbnOptions.Linger"/> after the last one leaves, reopened with
/// exponential backoff when it drops or goes mute. Every spot goes to every
/// listener; a listener that does not keep up loses its oldest lines, never
/// slows the others. F71: with <see cref="RbnOptions.AlwaysOn"/> a background
/// listener never leaves, so the connection stays open. The state (down, up
/// with the first spot of a session, reconnecting after a drop, down again
/// after <see cref="DownAfter"/> of reconnecting) goes to the clients and to
/// /health as "rbn".
/// </summary>
public sealed partial class RbnTelnetFeed(IOptions<RbnOptions> options, TimeProvider time, ISourceHealth health, ILogger<RbnTelnetFeed> logger) : IRbnFeed, IAsyncDisposable
{
    /// <summary>Lines kept for a slow listener before the oldest are dropped.</summary>
    public const int ListenerBuffer = 1000;

    /// <summary>Name of the feed in /health.</summary>
    public const string HealthName = "rbn";

    /// <summary>Reconnecting for longer than this is down.</summary>
    public static readonly TimeSpan DownAfter = TimeSpan.FromMinutes(5);

    private RbnFeedStatus status = new(RbnFeedState.Down, null);
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly Encoding Latin1 = Encoding.Latin1;

    private readonly Lock gate = new();
    private readonly Dictionary<long, Channel<RbnLine>> listeners = [];
    private long nextId;
    private CancellationTokenSource? running;
    private Task? loop;
    private ITimer? stopping;

    /// <summary>Whether the connection loop runs (someone listens, or listened less than the linger ago).</summary>
    public bool IsRunning
    {
        get
        {
            lock (gate) return running is not null;
        }
    }

    public RbnFeedStatus Status
    {
        get
        {
            RbnFeedStatus s;
            lock (gate) s = status;
            return s.State == RbnFeedState.Reconnecting && s.Since is { } since && time.GetUtcNow() - since > DownAfter
                ? new RbnFeedStatus(RbnFeedState.Down, since + DownAfter)
                : s;
        }
    }

    public Task WaitForChangeAsync(CancellationToken cancellationToken)
    {
        lock (gate) return changed.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Moves to <paramref name="state"/> (nothing if already there) and wakes who waits for a change.</summary>
    private void SetState(RbnFeedState state)
    {
        TaskCompletionSource wake;
        lock (gate)
        {
            if (status.State == state) return;
            status = new RbnFeedStatus(state, time.GetUtcNow());
            wake = changed;
            changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        wake.TrySetResult();
    }

    public async IAsyncEnumerable<RbnLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<RbnLine>(new BoundedChannelOptions(ListenerBuffer) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        long id;
        lock (gate)
        {
            id = nextId++;
            listeners[id] = channel;
            stopping?.Dispose();
            stopping = null;
            if (running is null)
            {
                running = new CancellationTokenSource();
                loop = RunAsync(running.Token);
            }
        }
        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return line;
            }
        }
        finally
        {
            lock (gate)
            {
                listeners.Remove(id);
                if (listeners.Count == 0 && running is not null)
                {
                    stopping?.Dispose();
                    stopping = time.CreateTimer(_ => StopIfNobody(), null, options.Value.Linger, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? ending;
        lock (gate)
        {
            stopping?.Dispose();
            running?.Cancel();
            running = null;
            ending = loop;
        }
        if (ending is not null) await ending.ConfigureAwait(false);
        SetState(RbnFeedState.Down);
    }

    private void StopIfNobody()
    {
        lock (gate)
        {
            if (listeners.Count > 0 || running is null) return;
            running.Cancel();
            running = null;
            stopping?.Dispose();
            stopping = null;
        }
        SetState(RbnFeedState.Down);
    }

    private void Broadcast(RbnLine line)
    {
        lock (gate)
        {
            foreach (var listener in listeners.Values) listener.Writer.TryWrite(line);
        }
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var attempt = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                string reason;
                try
                {
                    await SessionAsync(() => attempt = 0, stop).ConfigureAwait(false);
                    reason = "closed";
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    reason = e.GetType().Name;
                }
                if (stop.IsCancellationRequested) break;
                LogDropped(logger, options.Value.Host, reason);
                health.Failed(HealthName, reason);
                if (Status.State == RbnFeedState.Up) SetState(RbnFeedState.Reconnecting);
                var seconds = Math.Min(options.Value.MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt++, 7)));
                await Task.Delay(TimeSpan.FromSeconds(seconds), time, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private async Task SessionAsync(Action onSpot, CancellationToken stop)
    {
        var o = options.Value;
        using var tcp = new TcpClient { NoDelay = true };
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            connect.CancelAfter(o.ConnectTimeout);
            await tcp.ConnectAsync(o.Host, o.Port, connect.Token).ConfigureAwait(false);
        }
        var stream = tcp.GetStream();
        var decoder = new TelnetDecoder();
        var lines = new LineBuffer();
        var started = time.GetUtcNow();
        var lastData = started;
        var loggedIn = false;
        var buffer = new byte[8192];

        while (true)
        {
            var deadline = loggedIn ? lastData + o.IdleTimeout : started + o.LoginFallback;
            var wait = deadline - time.GetUtcNow();
            using var read = CancellationTokenSource.CreateLinkedTokenSource(stop);
            using var timer = new CancellationTokenSource(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1), time);
            using var link = timer.Token.Register(read.Cancel);
            int count;
            try
            {
                count = await stream.ReadAsync(buffer, read.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                if (loggedIn) throw new TimeoutException($"no data from {o.Host} for {o.IdleTimeout}");
                loggedIn = true;
                await SendAsync(stream, o.Callsign, stop).ConfigureAwait(false);
                continue;
            }
            if (count == 0) return;

            lastData = time.GetUtcNow();
            var refusals = new List<byte>();
            var data = decoder.Decode(buffer.AsSpan(0, count), refusals);
            if (refusals.Count > 0) await stream.WriteAsync(refusals.ToArray(), stop).ConfigureAwait(false);
            foreach (var text in lines.Append(Latin1.GetString(data)))
            {
                if (RbnLineParser.Parse(text, lastData) is not { } spot) continue;
                onSpot();
                if (status.State != RbnFeedState.Up)
                {
                    SetState(RbnFeedState.Up);
                    health.Succeeded(HealthName);
                }
                Broadcast(spot);
            }
            if (!loggedIn && ClusterPrompts.IsLogin(lines.Pending))
            {
                loggedIn = true;
                lines.ClearPending();
                await SendAsync(stream, o.Callsign, stop).ConfigureAwait(false);
            }
        }
    }

    private static Task SendAsync(NetworkStream stream, string line, CancellationToken cancellationToken) =>
        stream.WriteAsync(Latin1.GetBytes(line + "\r\n"), cancellationToken).AsTask();

    [LoggerMessage(Level = LogLevel.Warning, Message = "RBN {Host} connection dropped: {Reason}")]
    private static partial void LogDropped(ILogger logger, string host, string reason);
}
