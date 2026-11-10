using System.Net;
using System.Text.Json;
using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Host.Tests;

/// <summary>GET /v1/rbn/{callsign} on the real Host, the RBN feed faked.</summary>
public sealed class RbnApiTests : IDisposable
{
    private const string Mine = "DX de OK4YL-#:   7025.10  IU4VRB/P       CW     7 dB  23 WPM  CQ      2055Z";
    private const string Other = "DX de OK4YL-#:   7025.10  K1ABC          CW     9 dB  23 WPM  CQ      2055Z";

    private readonly TestHost host = new();

    public RbnApiTests()
    {
        host.Settings["Rbn:Heartbeat"] = "00:00:00.300";
        host.Settings["Rbn:MaxStreamDuration"] = "00:00:03";
    }

    public void Dispose() => host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An open stream and its lines as they come.</summary>
    private sealed class Stream(HttpResponseMessage response, StreamReader reader) : IDisposable
    {
        public HttpResponseMessage Response { get; } = response;

        public async Task<string> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await reader.ReadLineAsync(timeout.Token) ?? "<end>";
        }

        /// <summary>The next "data:" or "event:" line, skipping comments and blank lines.</summary>
        public async Task<string> NextEventAsync()
        {
            while (true)
            {
                var line = await NextAsync();
                if (line.Length > 0 && !line.StartsWith(':')) return line;
            }
        }

        public void Dispose()
        {
            reader.Dispose();
            Response.Dispose();
        }
    }

    private async Task<Stream> Open(string path, string? token = TestHost.ValidToken, string? origin = null)
    {
        var http = host.Client(token: token);
        if (origin is not null) http.DefaultRequestHeaders.Add("Origin", origin);
        var response = await http.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, Ct);
        return new Stream(response, new StreamReader(await response.Content.ReadAsStreamAsync(Ct)));
    }

    private async Task WaitForReaders(int count)
    {
        for (var i = 0; i < 200 && host.Rbn.Readers != count; i++) await Task.Delay(20, Ct);
        Assert.Equal(count, host.Rbn.Readers);
    }

    [Fact]
    public async Task A_signed_in_user_gets_the_RBN_lines_of_their_station_as_server_sent_events()
    {
        using var stream = await Open("/v1/rbn/IU4VRB", origin: "http://localhost:5173");
        Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
        Assert.Equal("text/event-stream", stream.Response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("http://localhost:5173", stream.Response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(": connected", await stream.NextAsync());
        await WaitForReaders(1);

        host.Rbn.Push(Other);
        host.Rbn.Push(Mine);

        Assert.Equal($"data: {Mine}", await stream.NextEventAsync());
    }

    [Fact]
    public async Task The_token_goes_in_the_Authorization_header_only_never_in_the_query_and_never_reaches_the_log()
    {
        using (var query = await Open($"/v1/rbn/IU4VRB?access_token={TestHost.ValidToken}", token: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, query.Response.StatusCode);
        }
        using (var stream = await Open("/v1/rbn/IU4VRB"))
        {
            Assert.Equal(": connected", await stream.NextAsync());
        }
        await WaitForReaders(0);
        await Task.Delay(200, Ct);

        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains(TestHost.ValidToken, StringComparison.Ordinal));
        Assert.Contains(host.Logs.Lines, l => l.Contains($"RBN stream of user {TestHost.UserId} for IU4VRB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, null)]
    [InlineData("forged", HttpStatusCode.Unauthorized, null)]
    [InlineData(TestHost.AnonymousToken, HttpStatusCode.Forbidden, "account_required")]
    [InlineData(TestHost.ReaderToken, HttpStatusCode.Forbidden, "not_allowed")]
    public async Task Only_signed_in_HamQSO_users_may_listen(string? token, HttpStatusCode status, string? error)
    {
        using var stream = await Open("/v1/rbn/IU4VRB", token);

        Assert.Equal(status, stream.Response.StatusCode);
        if (error is not null) Assert.Equal(error, JsonElement.Parse(await stream.Response.Content.ReadAsStringAsync(Ct)).GetProperty("error").GetString());
        Assert.Equal(0, host.Rbn.Readers);
    }

    [Theory]
    [InlineData("IU4VRB-7")]
    [InlineData("ABCDEF")]
    [InlineData("IU4VRB%0A")]
    [InlineData("%20IU4VRB")]
    [InlineData("IU4VRB//P")]
    public async Task Something_that_is_not_a_callsign_is_a_400(string callsign)
    {
        using var stream = await Open($"/v1/rbn/{callsign}");

        Assert.Equal(HttpStatusCode.BadRequest, stream.Response.StatusCode);
    }

    [Fact]
    public async Task A_user_has_two_streams_at_most()
    {
        var first = await Open("/v1/rbn/IU4VRB");
        using var second = await Open("/v1/rbn/IU4VRB/P");
        using var third = await Open("/v1/rbn/IU4VRB");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests), (first.Response.StatusCode, second.Response.StatusCode, third.Response.StatusCode));
        Assert.Equal("too_many_streams", JsonElement.Parse(await third.Response.Content.ReadAsStringAsync(Ct)).GetProperty("error").GetString());

        // Once the first stream is over (its slot is freed right after its reader), a new one may open.
        await first.NextAsync();
        first.Dispose();
        await WaitForReaders(1);
        var again = await Open("/v1/rbn/IU4VRB");
        for (var i = 0; i < 50 && again.Response.StatusCode == HttpStatusCode.TooManyRequests; i++)
        {
            again.Dispose();
            await Task.Delay(50, Ct);
            again = await Open("/v1/rbn/IU4VRB");
        }
        using (again) Assert.Equal(HttpStatusCode.OK, again.Response.StatusCode);
    }

    [Fact]
    public async Task A_quiet_stream_gets_a_ping_and_ends_at_its_time_limit()
    {
        using var stream = await Open("/v1/rbn/IU4VRB");

        var lines = new List<string>();
        for (var line = await stream.NextAsync(); line != "<end>"; line = await stream.NextAsync()) lines.Add(line);

        Assert.Contains(": ping", lines);
        Assert.Equal(["event: end", "data: time_limit", ""], lines[^3..]);
        await WaitForReaders(0);
    }

    // F71: the stream v2, every event already read by the server (plan, 3.2).

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)).ReplaceLineEndings("\n");

    [Fact]
    public async Task A_v2_spot_is_the_event_of_the_shared_fixture_with_the_true_position_of_the_skimmer()
    {
        host.RbnNodes.Set([new Domain.Rbn.RbnNode("OK4YL", "JN79FX", ["20m"], true, null)], DateTimeOffset.UtcNow);
        var expected = Fixture("rbn_sse_v2.txt").Split("\n\n");
        using var stream = await Open("/v1/rbn/IU2DEMO?v=2");
        Assert.Equal(": connected", await stream.NextAsync());
        await WaitForReaders(1);

        host.Rbn.Push("DX de OK4YL-#:   14045.1  IU2DEMO        CW    17 dB  23 WPM  CQ      1432Z", new DateTimeOffset(2026, 11, 6, 14, 32, 12, 418, TimeSpan.Zero));
        var spot = new[] { await stream.NextEventAsync(), await stream.NextEventAsync() };

        var fixtureSpot = expected[1].Split('\n');
        Assert.Equal(fixtureSpot[0], spot[0]);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(fixtureSpot[1]["data: ".Length..]),
            System.Text.Json.Nodes.JsonNode.Parse(spot[1]["data: ".Length..])), spot[1]);
    }

    [Fact]
    public async Task A_v2_stream_says_when_the_feed_changes_state()
    {
        var expected = Fixture("rbn_sse_v2.txt").Split("\n\n")[2].Split('\n');
        using var stream = await Open("/v1/rbn/IU2DEMO?v=2");
        Assert.Equal(": connected", await stream.NextAsync());
        await WaitForReaders(1);

        host.Rbn.SetStatus(new RbnFeedStatus(RbnFeedState.Reconnecting, DateTimeOffset.UtcNow));

        Assert.Equal(expected[0], await stream.NextEventAsync());
        Assert.Equal(expected[1], await stream.NextEventAsync());
    }

    [Fact]
    public async Task A_v2_stream_opened_while_the_feed_is_not_up_says_so_at_once()
    {
        host.Rbn.SetStatus(new RbnFeedStatus(RbnFeedState.Down, DateTimeOffset.UtcNow));
        using var stream = await Open("/v1/rbn/IU2DEMO?v=2");

        Assert.Equal(": connected", await stream.NextAsync());
        Assert.Equal("event: status", await stream.NextEventAsync());
        Assert.Equal("data: {\"feed\":\"down\"}", await stream.NextEventAsync());
    }

    [Fact]
    public async Task A_v2_stream_is_open_to_anonymous_users_too_but_not_to_readers()
    {
        using (var anonymous = await Open("/v1/rbn/IU2DEMO?v=2", TestHost.AnonymousToken))
        {
            Assert.Equal(HttpStatusCode.OK, anonymous.Response.StatusCode);
            Assert.Equal(": connected", await anonymous.NextAsync());
        }
        using var reader = await Open("/v1/rbn/IU2DEMO?v=2", TestHost.ReaderToken);
        Assert.Equal(HttpStatusCode.Forbidden, reader.Response.StatusCode);
        using var none = await Open("/v1/rbn/IU2DEMO?v=2", null);
        Assert.Equal(HttpStatusCode.Unauthorized, none.Response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_users_of_one_address_have_four_v2_streams_then_429_with_Retry_After()
    {
        var open = new List<Stream>();
        for (var i = 0; i < 4; i++) open.Add(await Open("/v1/rbn/IU2DEMO?v=2", $"{TestHost.UserTokenPrefix}anon{i}"));
        Assert.All(open, s => Assert.Equal(HttpStatusCode.OK, s.Response.StatusCode));

        using var fifth = await Open("/v1/rbn/IU2DEMO?v=2", $"{TestHost.UserTokenPrefix}anon5");

        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.Response.StatusCode);
        Assert.Equal("too_many_streams", JsonElement.Parse(await fifth.Response.Content.ReadAsStringAsync(Ct)).GetProperty("error").GetString());
        Assert.Equal("30", fifth.Response.Headers.GetValues("Retry-After").Single());
        foreach (var s in open) s.Dispose();
    }

    // F71 decision 25: the app offers 160 m at 1822 kHz.
    [Fact]
    public async Task A_v2_spot_on_160_m_has_its_band()
    {
        using var stream = await Open("/v1/rbn/IU2DEMO?v=2");
        Assert.Equal(": connected", await stream.NextAsync());
        await WaitForReaders(1);

        host.Rbn.Push("DX de OK4YL-#:    1822.0  IU2DEMO        CW    12 dB  22 WPM  CQ      2215Z");

        Assert.Equal("event: spot", await stream.NextEventAsync());
        var data = JsonElement.Parse((await stream.NextEventAsync())["data: ".Length..]);
        Assert.Equal(("160m", 1822.0), (data.GetProperty("band").GetString(), data.GetProperty("khz").GetDouble()));
    }

    [Fact]
    public async Task The_v1_stream_is_unchanged_raw_lines_and_no_status_event()
    {
        using var stream = await Open("/v1/rbn/IU4VRB?v=1");
        Assert.Equal(": connected", await stream.NextAsync());
        await WaitForReaders(1);

        host.Rbn.SetStatus(new RbnFeedStatus(RbnFeedState.Reconnecting, DateTimeOffset.UtcNow));
        host.Rbn.Push(Mine);

        Assert.Equal($"data: {Mine}", await stream.NextEventAsync());
    }

    [Fact]
    public async Task An_unknown_version_is_a_400()
    {
        using var stream = await Open("/v1/rbn/IU4VRB?v=3");

        Assert.Equal(HttpStatusCode.BadRequest, stream.Response.StatusCode);
        Assert.Equal("invalid_version", JsonElement.Parse(await stream.Response.Content.ReadAsStringAsync(Ct)).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("DX de X: line", "data: DX de X: line\n\n")]
    [InlineData("two\nlines\r\n", "data: two lines \n\n")]
    public void An_event_is_one_data_line(string line, string expected) => Assert.Equal(expected, RbnEndpoints.Event(line));
}
