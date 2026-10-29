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

        host.Rbn.Push(new RbnLine(Other, "K1ABC"));
        host.Rbn.Push(new RbnLine(Mine, "IU4VRB/P"));

        Assert.Equal($"data: {Mine}", await stream.NextEventAsync());
    }

    [Fact]
    public async Task The_token_may_come_in_the_query_for_EventSource_and_never_reaches_the_log()
    {
        using (var stream = await Open($"/v1/rbn/IU4VRB?access_token={TestHost.ValidToken}", token: null))
        {
            Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
            Assert.Equal(": connected", await stream.NextAsync());
        }
        await WaitForReaders(0);
        await Task.Delay(200, Ct);

        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains(TestHost.ValidToken, StringComparison.Ordinal) || l.Contains("access_token", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("DX de X: line", "data: DX de X: line\n\n")]
    [InlineData("two\nlines\r\n", "data: two lines \n\n")]
    public void An_event_is_one_data_line(string line, string expected) => Assert.Equal(expected, RbnEndpoints.Event(line));
}
