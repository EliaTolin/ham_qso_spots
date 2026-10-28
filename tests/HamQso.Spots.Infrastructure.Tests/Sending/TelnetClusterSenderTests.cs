using System.Net;
using System.Net.Sockets;
using HamQso.Spots.Infrastructure.Sending;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Sending;

/// <summary>Against <see cref="FakeDxNode"/> on loopback, with real sockets and short timings.</summary>
public sealed class TelnetClusterSenderTests
{
    private const string Line = "DX 14025.0 K1ABC CQ CW";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TelnetClusterSender Sender(int port, TimeSpan? idle = null, TimeSpan? loginTimeout = null) => new(
        Options.Create(new DxSendOptions
        {
            Host = "127.0.0.1",
            Port = port,
            LoginTimeout = loginTimeout ?? TimeSpan.FromSeconds(5),
            LoginFallback = TimeSpan.FromMilliseconds(300),
            WelcomeSilence = TimeSpan.FromMilliseconds(200),
            ReplyWindow = TimeSpan.FromMilliseconds(300),
            IdleTimeout = idle ?? TimeSpan.FromSeconds(30),
        }),
        TimeProvider.System,
        NullLogger<TelnetClusterSender>.Instance);

    [Fact]
    public async Task Logs_in_as_the_user_sends_the_line_and_returns_the_replies_but_not_the_spots()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);

        var first = await sender.SendAsync("IU4VRB-7", Line, Ct);
        var again = await sender.SendAsync("IU4VRB-7", Line, Ct);

        var connection = Assert.Single(node.Connections);
        Assert.Equal(["IU4VRB-7", Line, Line], connection.Lines);
        // The echo "DX de IU4VRB-7: ..." is a spot: the app sees it in the spot list. The welcome is not a reply.
        Assert.True(first.Sent);
        Assert.Empty(first.Replies);
        Assert.Equal(["Duplicate spot"], again.Replies);
    }

    [Fact]
    public async Task The_session_is_reused_by_the_same_login_and_another_login_has_its_own()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);

        await sender.SendAsync("IU4VRB-7", Line, Ct);
        await sender.SendAsync("IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);
        await sender.SendAsync("IU4VRB-12", "DX 7074.0 K1ABC FT8", Ct);

        Assert.Equal(["IU4VRB-7", "IU4VRB-12"], node.Connections.Select(c => c.Login));
        Assert.Equal(["IU4VRB-12", "IU4VRB-7"], sender.OpenLogins.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_session_closes_after_the_idle_time_and_the_next_spot_opens_a_new_one()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromMilliseconds(500));

        await sender.SendAsync("IU4VRB-7", Line, Ct);
        var first = Assert.Single(node.Connections);
        await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Empty(sender.OpenLogins);

        var result = await sender.SendAsync("IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);

        Assert.True(result.Sent);
        Assert.Equal(2, node.Connections.Count);
    }

    [Fact]
    public async Task A_spot_before_the_idle_time_keeps_the_session_open()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromMilliseconds(1500));

        await sender.SendAsync("IU4VRB-7", Line, Ct);
        await Task.Delay(800, Ct);
        await sender.SendAsync("IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);
        await Task.Delay(1000, Ct);

        Assert.False(Assert.Single(node.Connections).Closed.Task.IsCompleted);
    }

    [Fact]
    public async Task A_node_that_never_asks_gets_the_login_anyway()
    {
        await using var node = new FakeDxNode(prompt: null);
        await using var sender = Sender(node.Port);

        var result = await sender.SendAsync("IU4VRB-7", Line, Ct);

        Assert.True(result.Sent);
        Assert.Equal(["IU4VRB-7", Line], Assert.Single(node.Connections).Lines);
    }

    [Fact]
    public async Task A_password_prompt_gets_an_empty_password_as_in_the_app()
    {
        await using var node = new FakeDxNode(askPassword: true);
        await using var sender = Sender(node.Port);

        var result = await sender.SendAsync("IU4VRB-7", Line, Ct);

        Assert.True(result.Sent);
        Assert.Equal(["IU4VRB-7", "", Line], Assert.Single(node.Connections).Lines);
    }

    [Fact]
    public async Task A_node_that_does_not_answer_is_unavailable()
    {
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await using var sender = Sender(port);

        var result = await sender.SendAsync("IU4VRB-7", Line, Ct);

        Assert.False(result.Sent);
    }

    [Fact]
    public async Task A_node_that_refuses_the_login_is_unavailable()
    {
        await using var node = new FakeDxNode(closeAfterLogin: true);
        await using var sender = Sender(node.Port);

        var result = await sender.SendAsync("IU4VRB-7", Line, Ct);

        Assert.False(result.Sent);
        Assert.Empty(sender.OpenLogins);
    }

    [Fact]
    public async Task A_node_still_talking_when_the_login_time_is_over_is_unavailable()
    {
        // Silent node, login fallback (300 ms) after the login timeout: never ready.
        await using var node = new FakeDxNode(prompt: null);
        await using var sender = Sender(node.Port, loginTimeout: TimeSpan.FromMilliseconds(200));

        var result = await sender.SendAsync("IU4VRB-7", Line, Ct);

        Assert.False(result.Sent);
    }

    [Fact]
    public async Task A_session_dropped_by_the_node_is_opened_again_at_the_next_spot()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);
        await sender.SendAsync("IU4VRB-7", Line, Ct);
        var first = Assert.Single(node.Connections);

        first.Client.Client.Shutdown(SocketShutdown.Both);
        await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await WaitUntil(() => sender.OpenLogins.Count == 0);
        var result = await sender.SendAsync("IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);

        Assert.True(result.Sent);
        Assert.Equal(2, node.Connections.Count);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50, Ct);
        Assert.True(condition());
    }
}
