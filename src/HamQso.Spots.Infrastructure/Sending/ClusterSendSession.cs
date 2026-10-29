using System.Net.Sockets;
using System.Text;

namespace HamQso.Spots.Infrastructure.Sending;

/// <summary>
/// One telnet connection to the node logged in as a user, only to send
/// commands (the spots themselves come from the sources). Reads all the time,
/// so the node never blocks; what it writes is kept only while a command waits
/// for its answer.
/// </summary>
public sealed class ClusterSendSession : IAsyncDisposable
{
    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private readonly TcpClient tcp;
    private readonly NetworkStream stream;
    private readonly ClusterSendDialogue dialogue;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly Lock state = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task reading;
    private List<string>? collecting;
    private volatile bool closed;

    private ClusterSendSession(TcpClient tcp, string login, ClusterSendDialogue dialogue, TimeProvider time)
    {
        this.tcp = tcp;
        Login = login;
        this.dialogue = dialogue;
        this.time = time;
        stream = tcp.GetStream();
        reading = ReadAsync();
    }

    /// <summary>The login of this session (<c>IU4VRB-7</c>).</summary>
    public string Login { get; }

    /// <summary>False once the node has closed the connection (or it failed).</summary>
    public bool IsOpen => !closed;

    /// <summary>
    /// Connects and logs in. Throws <see cref="TimeoutException"/> when the node
    /// is not ready within <see cref="DxSendOptions.LoginTimeout"/>, <see cref="IOException"/>
    /// when it closes the connection first, <see cref="SocketException"/> when unreachable.
    /// </summary>
    public static async Task<ClusterSendSession> OpenAsync(DxSendOptions options, string login, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfNotOneLine(login);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.LoginTimeout);
        var tcp = new TcpClient { NoDelay = true };
        ClusterSendSession? session = null;
        try
        {
            await tcp.ConnectAsync(options.Host, options.Port, deadline.Token).ConfigureAwait(false);
            session = new ClusterSendSession(tcp, login, new ClusterSendDialogue(login, time.GetUtcNow()), time);
            while (!session.Ready(options))
            {
                if (session.closed) throw new IOException($"{options.Host} closed the connection before the login of {login} was over");
                if (session.FallbackLogin(options) is { } command) await session.WriteLineAsync(command, deadline.Token).ConfigureAwait(false);
                await Task.Delay(Tick, time, deadline.Token).ConfigureAwait(false);
            }
            return session;
        }
        catch (Exception e)
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            else tcp.Dispose();
            if (e is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{options.Host} did not take the login of {login} within {options.LoginTimeout}", e);
            }
            throw;
        }
    }

    /// <summary>Sends <paramref name="line"/> and returns what the node writes back within <paramref name="replyWindow"/>.</summary>
    public async Task<IReadOnlyList<string>> SendAsync(string line, TimeSpan replyWindow, CancellationToken cancellationToken)
    {
        var replies = new List<string>();
        lock (state) collecting = replies;
        try
        {
            await WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
            await Task.Delay(replyWindow, time, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (state) collecting = null;
        }
        return replies;
    }

    public async ValueTask DisposeAsync()
    {
        closed = true;
        await stop.CancelAsync().ConfigureAwait(false);
        tcp.Dispose();
        try
        {
            await reading.ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
        }
        stop.Dispose();
        writing.Dispose();
    }

    private bool Ready(DxSendOptions options)
    {
        lock (state) return dialogue.IsReady(time.GetUtcNow(), options.WelcomeSilence);
    }

    private string? FallbackLogin(DxSendOptions options)
    {
        lock (state) return dialogue.OnTick(time.GetUtcNow(), options.LoginFallback);
    }

    private async Task ReadAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer, stop.Token).ConfigureAwait(false);
                if (count == 0) return;
                var refusals = new List<byte>();
                string? command;
                lock (state)
                {
                    var data = dialogue.Decoder.Decode(buffer.AsSpan(0, count), refusals);
                    (var lines, command) = dialogue.OnText(Latin1.GetString(data), time.GetUtcNow());
                    collecting?.AddRange(lines);
                }
                if (refusals.Count > 0) await WriteAsync(refusals.ToArray(), stop.Token).ConfigureAwait(false);
                if (command is not null) await WriteLineAsync(command, stop.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            closed = true;
        }
    }

    private Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ThrowIfNotOneLine(line);
        return WriteAsync(Latin1.GetBytes(line + "\r\n"), cancellationToken);
    }

    /// <summary>
    /// The last guard before the socket: whatever the callers checked, a CR, LF or
    /// any other control character (a second command, telnet bytes) never goes out.
    /// </summary>
    private static void ThrowIfNotOneLine(string line)
    {
        if (line.Any(c => char.IsControl(c) || c > '\x7E')) throw new ArgumentException("Only one line of printable ASCII goes to the node", nameof(line));
    }

    private async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }
    }
}
