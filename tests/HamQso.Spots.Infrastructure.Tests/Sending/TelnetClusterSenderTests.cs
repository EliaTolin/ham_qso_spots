using System.Net;
using System.Net.Sockets;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sending;
using Microsoft.Extensions.Logging.Abstractions;

namespace HamQso.Spots.Infrastructure.Tests.Sending;

/// <summary>Against <see cref="FakeDxNode"/> on loopback, with real sockets and short timings.</summary>
public sealed class TelnetClusterSenderTests
{
    private const string Line = "DX 14025.0 K1ABC CQ CW";
    private const string UserA = "user-a";
    private const string UserB = "user-b";
    private const string UserC = "user-c";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DxSendOptions Settings(int port, TimeSpan? idle = null, TimeSpan? loginTimeout = null, int maxSessions = 50) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        LoginTimeout = loginTimeout ?? TimeSpan.FromSeconds(5),
        LoginFallback = TimeSpan.FromMilliseconds(300),
        WelcomeSilence = TimeSpan.FromMilliseconds(200),
        ReplyWindow = TimeSpan.FromMilliseconds(300),
        IdleTimeout = idle ?? TimeSpan.FromSeconds(30),
        MaxSessions = maxSessions,
    };

    private static TelnetClusterSender Sender(int port, TimeSpan? idle = null, TimeSpan? loginTimeout = null, int maxSessions = 50, TimeProvider? time = null) => new(
        Microsoft.Extensions.Options.Options.Create(Settings(port, idle, loginTimeout, maxSessions)),
        time ?? TimeProvider.System,
        NullLogger<TelnetClusterSender>.Instance);

    /// <summary>The real clock, whose timers fire 200 ms early, as a timer may against the wall clock.</summary>
    private sealed class EarlyTimers : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, dueTime > TimeSpan.FromMilliseconds(200) ? dueTime - TimeSpan.FromMilliseconds(200) : dueTime, period);
    }

    [Fact]
    public async Task Logs_in_as_the_user_sends_the_line_and_returns_the_replies_but_not_the_spots()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);

        var first = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        var again = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        var connection = Assert.Single(node.Connections);
        Assert.Equal(["IU4VRB-7", Line, Line], connection.Lines);
        // The echo "DX de IU4VRB-7: ..." is a spot: the app sees it in the spot list. The welcome is not a reply.
        Assert.Equal(ClusterSendStatus.Sent, first.Status);
        Assert.Empty(first.Replies);
        Assert.Equal(["Duplicate spot"], again.Replies);
    }

    [Fact]
    public async Task The_session_is_reused_by_the_same_user_and_another_user_has_their_own()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        await sender.SendAsync(UserA, "IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);
        await sender.SendAsync(UserB, "K1ABC-1", "DX 7074.0 K1ABC FT8", Ct);

        Assert.Equal(["IU4VRB-7", "K1ABC-1"], node.Connections.Select(c => c.Login));
        Assert.Equal(["IU4VRB-7", "K1ABC-1"], sender.OpenLogins.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_user_has_one_session_another_SSID_replaces_it()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        await sender.SendAsync(UserA, "IU4VRB-12", "DX 7074.0 K1ABC FT8", Ct);

        var first = node.Connections.First();
        await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(["IU4VRB-7", "IU4VRB-12"], node.Connections.Select(c => c.Login));
        Assert.Equal(["IU4VRB-12"], sender.OpenLogins);
    }

    [Fact]
    public async Task A_login_open_by_another_user_is_in_use_until_that_session_closes()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromMilliseconds(800));

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        var other = await sender.SendAsync(UserB, "IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);

        Assert.Equal(ClusterSendStatus.LoginInUse, other.Status);
        Assert.Equal(["IU4VRB-7", Line], Assert.Single(node.Connections).Lines);

        await WaitUntil(() => sender.OpenLogins.Count == 0);
        Assert.Equal(ClusterSendStatus.Sent, (await sender.SendAsync(UserB, "IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct)).Status);
    }

    [Fact]
    public async Task A_user_moving_to_another_SSID_frees_the_old_login()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        await sender.SendAsync(UserA, "IU4VRB-12", Line, Ct);

        Assert.Equal(ClusterSendStatus.Sent, (await sender.SendAsync(UserB, "IU4VRB-7", Line, Ct)).Status);
    }

    [Fact]
    public async Task A_failed_login_frees_the_login_for_others()
    {
        await using var node = new FakeDxNode(closeAfterLogin: true);
        await using var sender = Sender(node.Port);

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Unavailable, (await sender.SendAsync(UserB, "IU4VRB-7", Line, Ct)).Status);
    }

    [Fact]
    public async Task The_session_closes_after_the_idle_time_and_the_next_spot_opens_a_new_one()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromMilliseconds(500));

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        var first = Assert.Single(node.Connections);
        await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Empty(sender.OpenLogins);

        var result = await sender.SendAsync(UserA, "IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);

        Assert.Equal(ClusterSendStatus.Sent, result.Status);
        Assert.Equal(2, node.Connections.Count);
    }

    [Fact]
    public async Task The_session_closes_even_when_the_idle_timer_fires_before_the_clock_says_so()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromMilliseconds(600), time: new EarlyTimers());

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        await Assert.Single(node.Connections).Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Empty(sender.OpenLogins);
    }

    [Fact]
    public async Task A_spot_before_the_idle_time_keeps_the_session_open()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromMilliseconds(1500));

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        await Task.Delay(800, Ct);
        await sender.SendAsync(UserA, "IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);
        await Task.Delay(1000, Ct);

        Assert.False(Assert.Single(node.Connections).Closed.Task.IsCompleted);
    }

    [Fact]
    public async Task A_node_that_never_asks_gets_the_login_anyway()
    {
        await using var node = new FakeDxNode(prompt: null);
        await using var sender = Sender(node.Port);

        var result = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Sent, result.Status);
        Assert.Equal(["IU4VRB-7", Line], Assert.Single(node.Connections).Lines);
    }

    [Fact]
    public async Task A_password_prompt_gets_an_empty_password_as_in_the_app()
    {
        await using var node = new FakeDxNode(askPassword: true);
        await using var sender = Sender(node.Port);

        var result = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Sent, result.Status);
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

        var result = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task A_node_that_refuses_the_login_is_unavailable()
    {
        await using var node = new FakeDxNode(closeAfterLogin: true);
        await using var sender = Sender(node.Port);

        var result = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Unavailable, result.Status);
        Assert.Empty(sender.OpenLogins);
    }

    [Fact]
    public async Task A_node_still_talking_when_the_login_time_is_over_is_unavailable()
    {
        // Silent node, login fallback (300 ms) after the login timeout: never ready.
        await using var node = new FakeDxNode(prompt: null);
        await using var sender = Sender(node.Port, loginTimeout: TimeSpan.FromMilliseconds(200));

        var result = await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task A_session_dropped_by_the_node_is_opened_again_at_the_next_spot()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port);
        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        var first = Assert.Single(node.Connections);

        first.Client.Client.Shutdown(SocketShutdown.Both);
        await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await WaitUntil(() => sender.OpenLogins.Count == 0);
        var result = await sender.SendAsync(UserA, "IU4VRB-7", "DX 7074.0 K1ABC FT8", Ct);

        Assert.Equal(ClusterSendStatus.Sent, result.Status);
        Assert.Equal(2, node.Connections.Count);
    }

    [Fact]
    public async Task At_the_maximum_a_new_user_closes_the_least_recently_used_idle_session()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, maxSessions: 2);

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        await sender.SendAsync(UserB, "K1ABC-1", Line, Ct);
        var third = await sender.SendAsync(UserC, "DL1XYZ-2", Line, Ct);

        Assert.Equal(ClusterSendStatus.Sent, third.Status);
        await node.Connections.First().Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(["DL1XYZ-2", "K1ABC-1"], sender.OpenLogins.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task At_the_maximum_with_every_session_sending_a_new_user_is_busy()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, maxSessions: 1);

        // A is opening or sending (its gate is held) for at least the welcome silence and the reply window.
        var sending = sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);
        await node.NextConnectionAsync(Ct);
        var other = await sender.SendAsync(UserB, "K1ABC-1", Line, Ct);

        Assert.Equal(ClusterSendStatus.Busy, other.Status);
        Assert.Equal(ClusterSendStatus.Sent, (await sending).Status);
    }

    [Fact]
    public async Task A_request_given_up_after_the_spot_started_still_sends_and_the_session_still_closes_when_idle()
    {
        await using var node = new FakeDxNode();
        await using var sender = Sender(node.Port, idle: TimeSpan.FromSeconds(1));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var sending = sender.SendAsync(UserA, "IU4VRB-7", Line, request.Token);
        var connection = await node.NextConnectionAsync(Ct);
        await request.CancelAsync();

        Assert.Equal(ClusterSendStatus.Sent, (await sending).Status);
        Assert.Equal(["IU4VRB-7", Line], connection.Lines);
        await connection.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Empty(sender.OpenLogins);
    }

    [Fact]
    public async Task A_failed_login_does_not_hold_a_place_among_the_sessions()
    {
        await using var node = new FakeDxNode(closeAfterLogin: true);
        await using var sender = Sender(node.Port, maxSessions: 1);

        await sender.SendAsync(UserA, "IU4VRB-7", Line, Ct);

        Assert.Equal(ClusterSendStatus.Unavailable, (await sender.SendAsync(UserB, "K1ABC-1", Line, Ct)).Status);
    }

    [Theory]
    [InlineData("DX 14025.0 K1ABC CQ\r\nSET/PASSWORD x")]
    [InlineData("DX 14025.0 K1ABC CQ\n")]
    [InlineData("DX 14025.0 K1ABC \u00FF\u00F4")]
    [InlineData("DX 14025.0 K1ABC \u0007")]
    public async Task The_session_never_writes_a_second_line_or_telnet_bytes(string line)
    {
        await using var node = new FakeDxNode();
        await using var session = await ClusterSendSession.OpenAsync(Settings(node.Port), "IU4VRB-7", TimeProvider.System, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync(line, TimeSpan.FromMilliseconds(100), Ct));
        await session.SendAsync(Line, TimeSpan.FromMilliseconds(300), Ct);

        Assert.Equal(["IU4VRB-7", Line], Assert.Single(node.Connections).Lines);
    }

    [Fact]
    public async Task A_login_with_a_newline_never_connects()
    {
        await using var node = new FakeDxNode();

        await Assert.ThrowsAsync<ArgumentException>(() => ClusterSendSession.OpenAsync(Settings(node.Port), "IU4VRB-7\r\nBYE", TimeProvider.System, Ct));

        Assert.Empty(node.Connections);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50, Ct);
        Assert.True(condition());
    }
}
