using System.Net;
using System.Net.Sockets;
using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sources.Telnet;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Telnet;

/// <summary>Against a fake DXSpider node on loopback, with real sockets and short timeouts.</summary>
public sealed class TelnetNodeSourceTests : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    public TelnetNodeSourceTests()
    {
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        listener.Start();
    }

    public void Dispose()
    {
        cts.Dispose();
        listener.Dispose();
    }

    private TelnetNodeSource Source(TimeSpan? idle = null) => new(
        new TelnetNodeOptions { Name = "DXFun", Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port },
        new TelnetOptions
        {
            IdleTimeout = idle ?? TimeSpan.FromSeconds(10),
            LoginFallback = TimeSpan.FromMilliseconds(300),
            BacklogDelay = TimeSpan.FromMilliseconds(200),
            MaxBackoff = TimeSpan.FromSeconds(1),
        },
        TimeProvider.System);

    [Fact]
    public async Task Logs_in_with_N0CALL_87_asks_the_backlog_and_streams_the_spots()
    {
        var node = RunNode(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cts.Token);
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.Latin1);
            await Write(stream, "Welcome to EA4RCH-5\r\nlogin: ");
            var login = await reader.ReadLineAsync(cts.Token);
            var backlog = await reader.ReadLineAsync(cts.Token);
            await Write(stream, "DX de EA4RCH-#:   14025.0  JA1ABC       CW 12 dB 25 WPM CQ             2009Z IN80\a\a\r\n");
            await Task.Delay(500, cts.Token);
            return (login, backlog);
        });

        var batch = await FirstBatch(Source(), new FakeHealth());
        var (login, backlog) = await node;

        Assert.Equal("N0CALL-87", login);
        Assert.Equal("sh/dx 50", backlog);
        Assert.Equal("JA1ABC", Assert.Single(batch.Spots).Callsign);
        Assert.Equal("DXFun", batch.Feed);
    }

    [Fact]
    public async Task A_node_that_never_asks_gets_the_login_anyway()
    {
        var node = RunNode(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cts.Token);
            using var reader = new StreamReader(client.GetStream(), Encoding.Latin1);
            return await reader.ReadLineAsync(cts.Token);
        });

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var reading = Drain(Source(), new FakeHealth(), stop.Token);
        var login = await node;
        await stop.CancelAsync();
        await reading;

        Assert.Equal("N0CALL-87", login);
    }

    [Fact]
    public async Task A_mute_node_is_reported_and_the_connection_reopened()
    {
        var connections = 0;
        var node = RunNode(async () =>
        {
            while (connections < 2)
            {
                var client = await listener.AcceptTcpClientAsync(cts.Token);
                connections++;
                await Write(client.GetStream(), "login: ");
                // Then silence: no spot, no close.
            }
        });
        var health = new FakeHealth();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var reading = Drain(Source(idle: TimeSpan.FromMilliseconds(500)), health, stop.Token);
        await node;
        await stop.CancelAsync();
        await reading;

        Assert.Equal(2, connections);
        Assert.Contains(health.Failures, f => f.Contains("no data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Telnet_negotiation_is_refused_and_not_taken_as_text()
    {
        var node = RunNode(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cts.Token);
            var stream = client.GetStream();
            // IAC DO ECHO, then the prompt.
            await stream.WriteAsync(new byte[] { 255, 253, 1 }, cts.Token);
            await Write(stream, "login: ");
            var reply = new byte[3];
            await stream.ReadExactlyAsync(reply, cts.Token);
            return reply;
        });

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var reading = Drain(Source(), new FakeHealth(), stop.Token);
        var reply = await node;
        await stop.CancelAsync();
        await reading;

        Assert.Equal(new byte[] { 255, 252, 1 }, reply);
    }

    private async Task<SpotBatch> FirstBatch(TelnetNodeSource source, FakeHealth health)
    {
        await foreach (var batch in source.ReadAsync(health, cts.Token)) return batch;
        throw new InvalidOperationException("no batch");
    }

    private static async Task Drain(TelnetNodeSource source, FakeHealth health, CancellationToken token)
    {
        try
        {
            await foreach (var _ in source.ReadAsync(health, token))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task<T> RunNode<T>(Func<Task<T>> node) => Task.Run(node, cts.Token);

    private Task RunNode(Func<Task> node) => Task.Run(node, cts.Token);

    private Task Write(NetworkStream stream, string text) => stream.WriteAsync(Encoding.Latin1.GetBytes(text), cts.Token).AsTask();

    private sealed class FakeHealth : ISourceHealth
    {
        private readonly List<string> failures = [];

        public IReadOnlyList<string> Failures
        {
            get
            {
                lock (failures) return [.. failures];
            }
        }

        public void Succeeded(string source)
        {
        }

        public void Failed(string source, string reason)
        {
            lock (failures) failures.Add(reason);
        }
    }
}
