using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HamQso.Spots.Infrastructure.Tests.Rbn;

/// <summary>
/// An RBN-like telnet server on loopback, never the real one: banner, "Please
/// enter your call: ", then whatever <see cref="SendAsync"/> broadcasts to the
/// logged-in connections. Also used by the end-to-end tests (linked file).
/// </summary>
public sealed class FakeRbnNode : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task accepting;

    public FakeRbnNode(bool prompt = true)
    {
        Prompt = prompt;
        listener.Start();
        accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public bool Prompt { get; }

    public ConcurrentQueue<Connection> Connections { get; } = new();

    public IEnumerable<Connection> Open => Connections.Where(c => !c.Closed.Task.IsCompleted && c.Login is not null);

    /// <summary>Writes <paramref name="line"/> (plus CRLF) to every logged-in connection.</summary>
    public async Task SendAsync(string line)
    {
        foreach (var c in Open)
        {
            try
            {
                await c.Client.GetStream().WriteAsync(Encoding.Latin1.GetBytes(line + "\r\n"), stop.Token);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
        }
    }

    public async Task WaitForLoginAsync(int connections = 1)
    {
        for (var i = 0; i < 200 && Connections.Count(c => c.Login is not null) < connections; i++) await Task.Delay(50);
        if (Connections.Count(c => c.Login is not null) < connections) throw new TimeoutException("no login on the fake RBN");
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
            var connection = new Connection(await listener.AcceptTcpClientAsync(stop.Token));
            Connections.Enqueue(connection);
            _ = ServeAsync(connection);
        }
    }

    private async Task ServeAsync(Connection connection)
    {
        try
        {
            var stream = connection.Client.GetStream();
            using var reader = new StreamReader(stream, Encoding.Latin1);
            await stream.WriteAsync(Encoding.Latin1.GetBytes("Welcome to the Reverse Beacon Network (fake)\r\n" + (Prompt ? "Please enter your call: " : "")), stop.Token);
            connection.Login = await reader.ReadLineAsync(stop.Token);
            while (await reader.ReadLineAsync(stop.Token) is not null)
            {
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            connection.Closed.TrySetResult();
        }
    }

    public sealed class Connection(TcpClient client)
    {
        public TcpClient Client { get; } = client;

        public string? Login { get; set; }

        /// <summary>Completes when the client closes the connection.</summary>
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
