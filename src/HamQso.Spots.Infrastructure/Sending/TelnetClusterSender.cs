using System.Collections.Concurrent;
using System.Net.Sockets;
using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Sending;

/// <summary>
/// Sends users' spots to the node, like the app's OnDemandTelnetSender: one
/// session per login, opened at the first spot, reused by the next ones and
/// closed <see cref="DxSendOptions.IdleTimeout"/> after the last. Spots of the
/// same login go one at a time; different logins do not wait for each other.
/// </summary>
public sealed partial class TelnetClusterSender(IOptions<DxSendOptions> options, TimeProvider time, ILogger<TelnetClusterSender> logger) : IClusterSender, IAsyncDisposable
{
    /// <summary>One per login ever seen (a few bytes each); the session inside comes and goes.</summary>
    private readonly ConcurrentDictionary<string, Slot> slots = new(StringComparer.Ordinal);

    /// <summary>Logins with a session open now.</summary>
    public IReadOnlyList<string> OpenLogins => [.. slots.Where(s => s.Value.Session is { IsOpen: true }).Select(s => s.Key)];

    public async Task<ClusterSendResult> SendAsync(string login, string line, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var slot = slots.GetOrAdd(login, _ => new Slot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Session is not { IsOpen: true })
            {
                await slot.CloseAsync().ConfigureAwait(false);
                try
                {
                    slot.Session = await ClusterSendSession.OpenAsync(o, login, time, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (IsNodeFailure(e, cancellationToken))
                {
                    LogUnavailable(logger, e, o.Host, login);
                    return ClusterSendResult.Unavailable;
                }
            }
            IReadOnlyList<string> replies;
            try
            {
                replies = await slot.Session.SendAsync(line, o.ReplyWindow, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsNodeFailure(e, cancellationToken))
            {
                LogUnavailable(logger, e, o.Host, login);
                await slot.CloseAsync().ConfigureAwait(false);
                return ClusterSendResult.Unavailable;
            }
            slot.LastUse = time.GetUtcNow();
            slot.Idle?.Dispose();
            slot.Idle = time.CreateTimer(_ => _ = CloseIfIdleAsync(slot), null, o.IdleTimeout, Timeout.InfiniteTimeSpan);
            return new ClusterSendResult(true, replies);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var slot in slots.Values)
        {
            await slot.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await slot.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                slot.Gate.Release();
            }
        }
    }

    private static bool IsNodeFailure(Exception e, CancellationToken cancellationToken) =>
        e is SocketException or IOException or TimeoutException or ObjectDisposedException
        || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private async Task CloseIfIdleAsync(Slot slot)
    {
        await slot.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A spot may have come while this timer was waiting for the gate.
            if (time.GetUtcNow() - slot.LastUse >= options.Value.IdleTimeout) await slot.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DX node {Host} unavailable for {Login}")]
    private static partial void LogUnavailable(ILogger logger, Exception e, string host, string login);

    private sealed class Slot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public ClusterSendSession? Session { get; set; }

        public ITimer? Idle { get; set; }

        public DateTimeOffset LastUse { get; set; }

        /// <summary>Called with <see cref="Gate"/> held.</summary>
        public async Task CloseAsync()
        {
            Idle?.Dispose();
            Idle = null;
            if (Session is not null) await Session.DisposeAsync().ConfigureAwait(false);
            Session = null;
        }
    }
}
