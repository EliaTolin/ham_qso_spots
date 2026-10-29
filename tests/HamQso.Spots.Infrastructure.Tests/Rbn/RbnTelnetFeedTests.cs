using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Rbn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Rbn;

/// <summary>Against <see cref="FakeRbnNode"/> on loopback, with real sockets and short timings.</summary>
public sealed class RbnTelnetFeedTests
{
    private const string Spot = "DX de OK4YL-#:   7025.10  G0BQV          CW     7 dB  23 WPM  CQ      2055Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RbnTelnetFeed Feed(int port, TimeSpan? linger = null, TimeSpan? idle = null) => new(
        Options.Create(new RbnOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Callsign = "N0CALL-87",
            LoginFallback = TimeSpan.FromMilliseconds(300),
            IdleTimeout = idle ?? TimeSpan.FromSeconds(30),
            Linger = linger ?? TimeSpan.FromSeconds(30),
            MaxBackoff = TimeSpan.FromSeconds(1),
        }),
        TimeProvider.System,
        NullLogger<RbnTelnetFeed>.Instance);

    /// <summary>Reads the feed in the background until cancelled.</summary>
    private sealed class Listener : IAsyncDisposable
    {
        private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        private readonly Task reading;

        public Listener(IRbnFeed feed) => reading = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in feed.ReadAsync(stop.Token)) Lines.Enqueue(line);
            }
            catch (OperationCanceledException)
            {
            }
        });

        public System.Collections.Concurrent.ConcurrentQueue<RbnLine> Lines { get; } = new();

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            await reading;
            stop.Dispose();
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(50, Ct);
        Assert.True(condition());
    }

    [Fact]
    public async Task Nothing_connects_until_someone_listens()
    {
        await using var node = new FakeRbnNode();
        await using var feed = Feed(node.Port);

        await Task.Delay(300, Ct);

        Assert.Empty(node.Connections);
        Assert.False(feed.IsRunning);
    }

    [Fact]
    public async Task Two_listeners_share_one_connection_logged_in_with_the_server_callsign_and_both_get_every_spot()
    {
        await using var node = new FakeRbnNode();
        await using var feed = Feed(node.Port);
        await using var a = new Listener(feed);
        await using var b = new Listener(feed);
        await node.WaitForLoginAsync();

        await node.SendAsync(Spot);
        await node.SendAsync("some banner text");
        await Until(() => a.Lines.Count == 1 && b.Lines.Count == 1);

        Assert.Equal("N0CALL-87", Assert.Single(node.Connections).Login);
        Assert.Equal(new RbnLine(Spot, "G0BQV"), a.Lines.Single());
        Assert.Equal(new RbnLine(Spot, "G0BQV"), b.Lines.Single());
    }

    [Fact]
    public async Task A_node_without_prompt_gets_the_login_anyway()
    {
        await using var node = new FakeRbnNode(prompt: false);
        await using var feed = Feed(node.Port);
        await using var listener = new Listener(feed);

        await node.WaitForLoginAsync();

        Assert.Equal("N0CALL-87", Assert.Single(node.Connections).Login);
    }

    [Fact]
    public async Task The_connection_closes_a_while_after_the_last_listener_leaves()
    {
        await using var node = new FakeRbnNode();
        await using var feed = Feed(node.Port, linger: TimeSpan.FromMilliseconds(500));
        var listener = new Listener(feed);
        await node.WaitForLoginAsync();

        await listener.DisposeAsync();
        Assert.True(feed.IsRunning);
        await Assert.Single(node.Connections).Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(feed.IsRunning);
    }

    [Fact]
    public async Task A_listener_coming_back_within_the_linger_keeps_the_same_connection()
    {
        await using var node = new FakeRbnNode();
        await using var feed = Feed(node.Port, linger: TimeSpan.FromSeconds(2));
        var first = new Listener(feed);
        await node.WaitForLoginAsync();
        await first.DisposeAsync();

        await using var second = new Listener(feed);
        await Task.Delay(2500, Ct);
        await node.SendAsync(Spot);
        await Until(() => second.Lines.Count == 1);

        Assert.Single(node.Connections);
    }

    [Fact]
    public async Task A_dropped_or_mute_connection_is_opened_again()
    {
        await using var node = new FakeRbnNode();
        await using var feed = Feed(node.Port, idle: TimeSpan.FromMilliseconds(800));
        await using var listener = new Listener(feed);
        await node.WaitForLoginAsync();

        // Dropped by the node.
        node.Connections.Single().Client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Both);
        await node.WaitForLoginAsync(2);
        // Mute: no data for longer than the idle timeout.
        await node.WaitForLoginAsync(3);

        await node.SendAsync(Spot);
        await Until(() => listener.Lines.Count == 1);
    }
}
