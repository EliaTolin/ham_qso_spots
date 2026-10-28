using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HamQso.Spots.Host.Tests;

/// <summary>POST /v1/dx/spots on the real Host (see <see cref="TestHost"/>): profile and node faked.</summary>
public sealed class SendSpotApiTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    private Task<HttpResponseMessage> Post(object body, string? token = TestHost.ValidToken) =>
        host.Client(token: token).PostAsJsonAsync(new Uri("/v1/dx/spots", UriKind.Relative), body, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_HamQSO_user_spots_with_the_callsign_of_the_profile_and_gets_the_node_replies()
    {
        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ CW", ssid = 7 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.True(body.GetProperty("sent").GetBoolean());
        Assert.Equal("Spot sent", body.GetProperty("replies")[0].GetString());
        Assert.Equal(("IU4VRB-7", "DX 14025.0 K1ABC CQ CW"), Assert.Single(host.Cluster.Sent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forged.token")]
    public async Task Without_a_valid_Supabase_token_it_is_a_401(string? token)
    {
        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 }, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(host.Cluster.Sent);
    }

    [Fact]
    public async Task A_user_of_a_reader_project_cannot_spot()
    {
        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 }, TestHost.ReaderToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_allowed", (await Body(response)).GetProperty("error").GetString());
        Assert.Empty(host.Cluster.Sent);
    }

    [Fact]
    public async Task An_anonymous_user_needs_an_account_to_spot()
    {
        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 }, TestHost.AnonymousToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_required", (await Body(response)).GetProperty("error").GetString());
        Assert.Empty(host.Cluster.Sent);
    }

    [Theory]
    [InlineData("SH/DX 50")]
    [InlineData("DX 14025.0 K1ABC CQ \u00FF\u00FD\u0001")]
    [InlineData("DX 14025.0 K1ABC CQ\r\nSET/PASSWORD x")]
    [InlineData("")]
    public async Task Anything_but_one_DX_line_is_invalid_line(string line)
    {
        using var response = await Post(new { line, ssid = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_line", (await Body(response)).GetProperty("error").GetString());
        Assert.Empty(host.Cluster.Sent);
    }

    [Fact]
    public async Task A_body_that_is_not_JSON_or_too_big_is_invalid_line()
    {
        var http = host.Client();
        var ct = TestContext.Current.CancellationToken;

        using var notJson = await http.PostAsync(new Uri("/v1/dx/spots", UriKind.Relative), new StringContent("DX 14025.0 K1ABC", Encoding.UTF8, "application/json"), ct);
        using var tooBig = await Post(new { line = new string('x', 5000), ssid = 7 });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (notJson.StatusCode, tooBig.StatusCode));
        Assert.Equal("invalid_line", (await Body(notJson)).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(null)]
    public async Task An_SSID_outside_1_99_is_invalid_ssid(int? ssid)
    {
        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_ssid", (await Body(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Without_a_callsign_in_the_profile_it_is_a_422_no_callsign()
    {
        host.Profiles.Callsigns[TestHost.UserId] = null;

        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("no_callsign", (await Body(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_fourth_spot_in_a_minute_is_a_429_with_Retry_After()
    {
        for (var i = 0; i < 3; i++)
        {
            using var ok = await Post(new { line = $"DX 1402{i}.0 K1ABC CQ", ssid = 7 });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var response = await Post(new { line = "DX 14029.0 K1ABC CQ", ssid = 7 });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("rate_limited", body.GetProperty("error").GetString());
        var seconds = body.GetProperty("retry_after_s").GetInt32();
        Assert.InRange(seconds, 1, 60);
        Assert.Equal(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), response.Headers.RetryAfter!.ToString());
        Assert.Equal(3, host.Cluster.Sent.Count);
    }

    [Fact]
    public async Task When_the_node_cannot_be_reached_it_is_a_502_node_unavailable()
    {
        host.Cluster.Failure = HamQso.Spots.Application.Ports.ClusterSendResult.Unavailable;

        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("node_unavailable", (await Body(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task When_too_many_sessions_are_open_it_is_a_503_busy()
    {
        host.Cluster.Failure = HamQso.Spots.Application.Ports.ClusterSendResult.Busy;

        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("busy", (await Body(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_login_another_user_has_open_is_a_409_login_in_use()
    {
        host.Cluster.Failure = HamQso.Spots.Application.Ports.ClusterSendResult.LoginInUse;

        using var response = await Post(new { line = "DX 14025.0 K1ABC CQ", ssid = 7 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("login_in_use", (await Body(response)).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(1.0, 1)]
    [InlineData(41.3, 42)]
    public void Retry_After_is_whole_seconds_rounded_up_and_at_least_1(double wait, int seconds) =>
        Assert.Equal(seconds, SendSpotEndpoints.RetryAfterSeconds(TimeSpan.FromSeconds(wait)));
}
