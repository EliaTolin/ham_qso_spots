using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace HamQso.Spots.Infrastructure.Tests.Sending;

/// <summary>
/// A DXSpider-like node on loopback, never the real cluster: welcome text, a
/// login prompt, then for every DX command the broadcast of the spot back
/// ("DX de LOGIN: ...") or "Duplicate spot" when the same command comes twice,
/// as DXSpider does. Also used by the end-to-end tests (linked file).
/// </summary>
public sealed class FakeDxNode : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<Connection> accepted = Channel.CreateUnbounded<Connection>();
    private readonly ConcurrentDictionary<string, bool> spotted = new(StringComparer.Ordinal);
    private readonly Task accepting;

    public FakeDxNode(string? prompt = "login: ", bool askPassword = false, bool closeAfterLogin = false)
    {
        Prompt = prompt;
        AskPassword = askPassword;
        CloseAfterLogin = closeAfterLogin;
        listener.Start();
        accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>Null: the node waits silently for the callsign.</summary>
    public string? Prompt { get; }

    public bool AskPassword { get; }

    /// <summary>Refuses the login: closes the connection as soon as it arrives.</summary>
    public bool CloseAfterLogin { get; }

    public ConcurrentQueue<Connection> Connections { get; } = new();

    /// <summary>The next connection the node accepts.</summary>
    public async Task<Connection> NextConnectionAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return await accepted.Reader.ReadAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Stop();
        try
        {
            await accepting;
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
        foreach (var c in Connections) c.Client.Dispose();
        listener.Dispose();
        stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stop.Token);
            var connection = new Connection(client);
            Connections.Enqueue(connection);
            await accepted.Writer.WriteAsync(connection, stop.Token);
            _ = ServeAsync(connection);
        }
    }

    private async Task ServeAsync(Connection connection)
    {
        try
        {
            var stream = connection.Client.GetStream();
            using var reader = new StreamReader(stream, Encoding.Latin1);
            await Write(stream, "Hello, this is FAKE-1 in Testland\r\nrunning DXSpider\r\n" + Prompt);
            var login = await reader.ReadLineAsync(stop.Token);
            if (login is null) return;
            connection.Lines.Enqueue(login);
            if (CloseAfterLogin) return;
            if (AskPassword)
            {
                await Write(stream, "password: ");
                connection.Lines.Enqueue(await reader.ReadLineAsync(stop.Token) ?? "");
            }
            await Write(stream, $"Hello {login}, this is FAKE-1\r\n{login} de FAKE-1 28-Oct-2026 1200Z dxspider >\r\n");
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                connection.Lines.Enqueue(line);
                connection.Received.Writer.TryWrite(line);
                if (!line.StartsWith("DX ", StringComparison.Ordinal)) continue;
                var parts = line.Split(' ', 4);
                await Write(stream, spotted.TryAdd(line, true)
                    ? $"DX de {login}:{parts[1],11}  {parts[2],-12}{(parts.Length > 3 ? parts[3] : ""),-30} 1200Z\a\a\r\n"
                    : "Duplicate spot\r\n");
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            connection.Client.Dispose();
            connection.Closed.TrySetResult();
        }
    }

    private Task Write(NetworkStream stream, string text) => stream.WriteAsync(Encoding.Latin1.GetBytes(text), stop.Token).AsTask();

    public sealed class Connection(TcpClient client)
    {
        public TcpClient Client { get; } = client;

        /// <summary>Every line the client sent: the login first.</summary>
        public ConcurrentQueue<string> Lines { get; } = new();

        /// <summary>The lines after the login, as they come.</summary>
        public Channel<string> Received { get; } = Channel.CreateUnbounded<string>();

        /// <summary>Completes when either side closes the connection.</summary>
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? Login => Lines.TryPeek(out var first) ? first : null;
    }
}
