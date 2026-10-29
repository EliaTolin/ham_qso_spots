using System.Collections.Concurrent;
using System.Net.Sockets;
using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Sending;

/// <summary>
/// Sends users' spots to the node, like the app's OnDemandTelnetSender: one
/// session per user, opened at the first spot, reused by the next ones and
/// closed <see cref="DxSendOptions.IdleTimeout"/> after the last; a spot with
/// another login (another SSID) replaces the user's session. A login belongs to
/// one user at a time: while another user's session has it (until that session
/// closes when idle), the spot is <see cref="ClusterSendResult.LoginInUse"/>. Spots of the same user go one at
/// a time; different users do not wait for each other.
/// At most <see cref="DxSendOptions.MaxSessions"/> sessions at once: a new one
/// beyond them closes the least recently used idle session, and is
/// <see cref="ClusterSendResult.Busy"/> only when every session is sending.
/// Once a spot starts it ends on its own timeouts (login, reply window), not
/// with the HTTP request, and the session always gets its idle timer.
/// </summary>
public sealed partial class TelnetClusterSender(IOptions<DxSendOptions> options, TimeProvider time, ILogger<TelnetClusterSender> logger) : IClusterSender, IAsyncDisposable
{
    /// <summary>One per user ever seen (a few bytes each); the session inside comes and goes.</summary>
    private readonly ConcurrentDictionary<string, Slot> slots = new(StringComparer.Ordinal);

    /// <summary>Login → the user whose session has it (open or opening). Taken atomically, released at close.</summary>
    private readonly ConcurrentDictionary<string, string> owners = new(StringComparer.Ordinal);

    /// <summary>One decision at a time about who may open a session.</summary>
    private readonly SemaphoreSlim capacity = new(1, 1);

    /// <summary>Sessions open or opening, counted before connecting so concurrent users cannot pass the cap.</summary>
    private int sessions;

    /// <summary>Logins with a session open now.</summary>
    public IReadOnlyList<string> OpenLogins => [.. slots.Values.Select(s => s.Session).OfType<ClusterSendSession>().Where(s => s.IsOpen).Select(s => s.Login)];

    public async Task<ClusterSendResult> SendAsync(string userId, string login, string line, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var slot = slots.GetOrAdd(userId, id => new Slot(id));
        // The request may give up while waiting for the user's previous spot; after this, nothing follows its token.
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Session is not { IsOpen: true } || slot.Session.Login != login)
            {
                await CloseAsync(slot).ConfigureAwait(false);
                if (!owners.TryAdd(login, userId))
                {
                    LogInUse(logger, login, userId);
                    return ClusterSendResult.LoginInUse;
                }
                slot.Login = login;
                if (!await ReserveAsync(slot).ConfigureAwait(false))
                {
                    ReleaseLogin(slot);
                    LogBusy(logger, o.MaxSessions, login);
                    return ClusterSendResult.Busy;
                }
                try
                {
                    slot.Session = await ClusterSendSession.OpenAsync(o, login, time, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Interlocked.Decrement(ref sessions);
                    ReleaseLogin(slot);
                    if (!IsNodeFailure(e)) throw;
                    LogUnavailable(logger, e, o.Host, login);
                    return ClusterSendResult.Unavailable;
                }
            }
            try
            {
                return ClusterSendResult.Sent(await slot.Session.SendAsync(line, o.ReplyWindow, CancellationToken.None).ConfigureAwait(false));
            }
            catch (Exception e) when (IsNodeFailure(e))
            {
                LogUnavailable(logger, e, o.Host, login);
                await CloseAsync(slot).ConfigureAwait(false);
                return ClusterSendResult.Unavailable;
            }
        }
        finally
        {
            // Whatever happened, an open session closes when idle.
            if (slot.Session is not null) ArmIdle(slot, o.IdleTimeout);
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
                await CloseAsync(slot).ConfigureAwait(false);
            }
            finally
            {
                slot.Gate.Release();
            }
        }
        capacity.Dispose();
    }

    private static bool IsNodeFailure(Exception e) =>
        e is SocketException or IOException or TimeoutException or ObjectDisposedException or OperationCanceledException;

    /// <summary>
    /// A place for a new session of <paramref name="mine"/> (whose gate is held). At
    /// the cap, closes the least recently used session that is not sending (its
    /// gate is free: taken without waiting, so two users never wait for each other).
    /// </summary>
    private async Task<bool> ReserveAsync(Slot mine)
    {
        await capacity.WaitAsync().ConfigureAwait(false);
        try
        {
            if (sessions >= options.Value.MaxSessions && !await CloseLeastRecentlyUsedAsync(mine).ConfigureAwait(false)) return false;
            Interlocked.Increment(ref sessions);
            return true;
        }
        finally
        {
            capacity.Release();
        }
    }

    private async Task<bool> CloseLeastRecentlyUsedAsync(Slot mine)
    {
        foreach (var other in slots.Values.Where(s => s != mine && s.Session is not null).OrderBy(s => s.LastUse).ToArray())
        {
            if (!await other.Gate.WaitAsync(0).ConfigureAwait(false)) continue;
            try
            {
                if (other.Session is null) continue;
                LogEvicted(logger, other.Session.Login);
                await CloseAsync(other).ConfigureAwait(false);
                return true;
            }
            finally
            {
                other.Gate.Release();
            }
        }
        return false;
    }

    /// <summary>Called with the slot's gate held.</summary>
    private void ArmIdle(Slot slot, TimeSpan idle)
    {
        slot.LastUse = time.GetUtcNow();
        Schedule(slot, idle);
    }

    /// <summary>Called with the slot's gate held.</summary>
    private void Schedule(Slot slot, TimeSpan due)
    {
        slot.Idle?.Dispose();
        slot.Idle = time.CreateTimer(_ => _ = CloseIfIdleAsync(slot), null, due, Timeout.InfiniteTimeSpan);
    }

    private async Task CloseIfIdleAsync(Slot slot)
    {
        await slot.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (slot.Session is null) return;
            var left = options.Value.IdleTimeout - (time.GetUtcNow() - slot.LastUse);
            if (left <= TimeSpan.Zero) await CloseAsync(slot).ConfigureAwait(false);
            // A spot came while this timer waited for the gate, or the timer fired a little
            // before the clock says the idle time is over: wait the rest, never give up.
            else Schedule(slot, left);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    /// <summary>Called with the slot's gate held.</summary>
    private async Task CloseAsync(Slot slot)
    {
        slot.Idle?.Dispose();
        slot.Idle = null;
        if (slot.Session is not null)
        {
            await slot.Session.DisposeAsync().ConfigureAwait(false);
            slot.Session = null;
            Interlocked.Decrement(ref sessions);
        }
        ReleaseLogin(slot);
    }

    /// <summary>Frees the slot's login for any user (only if this user still holds it). Gate held.</summary>
    private void ReleaseLogin(Slot slot)
    {
        if (slot.Login is { } login) owners.TryRemove(new KeyValuePair<string, string>(login, slot.UserId));
        slot.Login = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DX sending busy: {Max} sessions open and all sending, {Login} refused")]
    private static partial void LogBusy(ILogger logger, int max, string login);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DX login {Login} in use by another user, refused for user {UserId}")]
    private static partial void LogInUse(ILogger logger, string login, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "DX sending full: session of {Login} closed for a new one")]
    private static partial void LogEvicted(ILogger logger, string login);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DX node {Host} unavailable for {Login}")]
    private static partial void LogUnavailable(ILogger logger, Exception e, string host, string login);

    private sealed class Slot(string userId)
    {
        public string UserId { get; } = userId;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>The login this user holds in <see cref="owners"/> (from reservation to close).</summary>
        public string? Login { get; set; }

        public ClusterSendSession? Session { get; set; }

        public ITimer? Idle { get; set; }

        public DateTimeOffset LastUse { get; set; }
    }
}
