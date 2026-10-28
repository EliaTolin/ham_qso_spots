using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Sending;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Sending;

public class SendDxSpotTests
{
    private const string User = "4b1e2a9c-0d3f-4e5a-8b6c-7d8e9f0a1b2c";
    private const string Line = "DX 14025.0 K1ABC CQ CW";

    private const string OtherUser = "9c8d7e6f-5a4b-4c3d-8e2f-1a0b9c8d7e6f";

    private readonly FakeProfiles profiles = new();
    private readonly FakeCluster cluster = new();
    private readonly RecordingLogger logger = new();
    private readonly SendDxSpot send;

    public SendDxSpotTests() => send = new SendDxSpot(profiles, cluster, new SpotRateLimiter(new FakeTimeProvider()), logger);

    private Task<SendDxSpotResult> Send(string? line = Line, int? ssid = 7, string user = User) => send.ExecuteAsync(user, line, ssid, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_spot_goes_out_logged_in_with_the_callsign_of_the_profile_and_the_SSID()
    {
        var result = await Send();

        Assert.Equal(SendDxSpotOutcome.Sent, result.Outcome);
        Assert.Equal(["Spot sent"], result.Replies);
        Assert.Equal(("IU4VRB-7", Line), Assert.Single(cluster.Sent));
    }

    [Fact]
    public async Task Every_send_is_logged_with_user_login_line_and_outcome()
    {
        await Send();
        await Send("SH/DX\r\nBYE");

        Assert.Equal($"DX spot by user {User} as IU4VRB-7: {Line} -> Sent", logger.Messages[0]);
        // An invalid line never reaches the log as it is (it could forge log lines).
        Assert.Equal($"DX spot by user {User} as -: <invalid, 10 chars> -> InvalidLine", logger.Messages[1]);
    }

    [Fact]
    public async Task An_invalid_line_or_SSID_never_reaches_the_profile_or_the_node()
    {
        Assert.Equal(SendDxSpotOutcome.InvalidLine, (await Send("SH/DX 50")).Outcome);
        Assert.Equal(SendDxSpotOutcome.InvalidSsid, (await Send(ssid: 0)).Outcome);

        Assert.Equal(0, profiles.Reads);
        Assert.Empty(cluster.Sent);
    }

    [Fact]
    public async Task Without_a_callsign_in_the_profile_nothing_is_sent()
    {
        profiles.Callsign = null;

        Assert.Equal(SendDxSpotOutcome.NoCallsign, (await Send()).Outcome);
        Assert.Empty(cluster.Sent);
    }

    [Theory]
    [InlineData("IU4VRB\r\nSET/PASSWORD x")]
    [InlineData("IU4VRB-7")]
    [InlineData("TOOLONGCALL")]
    [InlineData("IU4VRB\u00FF")]
    public async Task A_profile_callsign_outside_the_profiles_pattern_is_no_callsign(string callsign)
    {
        profiles.Callsign = callsign;

        Assert.Equal(SendDxSpotOutcome.NoCallsign, (await Send()).Outcome);
        Assert.Empty(cluster.Sent);
    }

    [Fact]
    public async Task Two_accounts_with_the_same_callsign_share_its_limit()
    {
        await Send();
        await Send();
        await Send(user: OtherUser);

        var result = await Send(user: OtherUser);

        Assert.Equal(SendDxSpotOutcome.RateLimited, result.Outcome);
        Assert.Equal(3, cluster.Sent.Count);
    }

    [Fact]
    public async Task Too_many_sessions_open_is_busy()
    {
        cluster.Failure = ClusterSendResult.Busy;

        Assert.Equal(SendDxSpotOutcome.Busy, (await Send()).Outcome);
    }

    [Fact]
    public async Task The_fourth_spot_in_a_minute_is_refused_before_the_profile_is_read()
    {
        for (var i = 0; i < 3; i++) await Send();

        var result = await Send();

        Assert.Equal(SendDxSpotOutcome.RateLimited, result.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(1), result.RetryAfter);
        Assert.Equal(3, profiles.Reads);
        Assert.Equal(3, cluster.Sent.Count);
    }

    [Fact]
    public async Task A_node_that_cannot_be_reached_is_node_unavailable()
    {
        cluster.Failure = ClusterSendResult.Unavailable;

        Assert.Equal(SendDxSpotOutcome.NodeUnavailable, (await Send()).Outcome);
    }

    private sealed class FakeProfiles : IProfileReader
    {
        public string? Callsign { get; set; } = "IU4VRB";

        public int Reads { get; private set; }

        public Task<string?> CallsignAsync(string userId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(userId is User or OtherUser ? Callsign : null);
        }
    }

    private sealed class FakeCluster : IClusterSender
    {
        public List<(string Login, string Line)> Sent { get; } = [];

        public ClusterSendResult? Failure { get; set; }

        public Task<ClusterSendResult> SendAsync(string login, string line, CancellationToken cancellationToken)
        {
            if (Failure is not null) return Task.FromResult(Failure);
            Sent.Add((login, line));
            return Task.FromResult(ClusterSendResult.Sent(["Spot sent"]));
        }
    }

    private sealed class RecordingLogger : ILogger<SendDxSpot>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
