using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HamQso.Spots.Host.Tests;

/// <summary>/v1/proxy on the real Host, the real forwarding client and a <see cref="FakeUpstream"/> on loopback.</summary>
public sealed class ProxyApiTests : IAsyncLifetime
{
    private readonly TestHost host = new();
    private FakeUpstream upstream = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        upstream = await FakeUpstream.StartAsync();
        host.Upstreams["svc"] = upstream.BaseUri;
        host.Settings["Proxy:MaxBodyBytes"] = "1000";
        host.Settings["Proxy:Timeout"] = "00:00:01";
    }

    public async ValueTask DisposeAsync()
    {
        await host.DisposeAsync();
        await upstream.DisposeAsync();
    }

    private HttpClient Client(string? token = TestHost.ValidToken)
    {
        var http = host.Client(token: null);
        if (token is not null) http.DefaultRequestHeaders.Add("X-HamQSO-Auth", $"Bearer {token}");
        return http;
    }

    private static async Task<string?> Error(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync(Ct)).GetProperty("error").GetString();

    [Fact]
    public async Task The_request_goes_through_as_it_is_and_only_status_type_and_body_come_back()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/proxy/svc/echo/api?KEY=QRZ-KEY-1&ACTION=INSERT")
        {
            Content = new StringContent("ADIF=<call:5>K1ABC", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "SOTA-TOKEN");
        request.Headers.Add("Cookie", "hamqso-session=1");
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await Client().SendAsync(request, Ct);

        var seen = Assert.Single(upstream.Requests);
        Assert.Equal(("POST", "/echo/api?KEY=QRZ-KEY-1&ACTION=INSERT"), (seen.Method, seen.Target));
        Assert.Equal("Bearer SOTA-TOKEN", seen.Headers["Authorization"]);
        Assert.Equal("application/json", seen.Headers["Accept"]);
        Assert.StartsWith("application/x-www-form-urlencoded", seen.Headers["Content-Type"], StringComparison.Ordinal);
        Assert.Equal("ADIF=<call:5>K1ABC", Encoding.UTF8.GetString(seen.Body));
        Assert.False(seen.Headers.ContainsKey("Cookie"));
        Assert.False(seen.Headers.ContainsKey("X-HamQSO-Auth"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("{\"ok\":true}", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.False(response.Headers.Contains("X-Upstream-Secret"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Get_put_and_delete_go_through_too(string method)
    {
        using var response = await Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), "/v1/proxy/svc/echo/x"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(method, Assert.Single(upstream.Requests).Method);
    }

    [Fact]
    public async Task Without_the_HamQSO_token_in_its_own_header_it_is_a_401_and_nothing_goes_out()
    {
        using var none = await Client(token: null).GetAsync(new Uri("/v1/proxy/svc/echo/x", UriKind.Relative), Ct);
        using var forged = await Client("forged").GetAsync(new Uri("/v1/proxy/svc/echo/x", UriKind.Relative), Ct);
        // The HamQSO token in Authorization does not count: that header belongs to the service.
        using var wrongHeader = await host.Client(token: TestHost.ValidToken).GetAsync(new Uri("/v1/proxy/svc/echo/x", UriKind.Relative), Ct);

        Assert.Equal(
            (HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
            (none.StatusCode, forged.StatusCode, wrongHeader.StatusCode));
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task Anonymous_HamQSO_users_may_use_it_reader_projects_may_not()
    {
        using var anonymous = await Client(TestHost.AnonymousToken).GetAsync(new Uri("/v1/proxy/svc/echo/x", UriKind.Relative), Ct);
        using var reader = await Client(TestHost.ReaderToken).GetAsync(new Uri("/v1/proxy/svc/echo/x", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reader.StatusCode);
        Assert.Equal("not_allowed", await Error(reader));
    }

    [Fact]
    public async Task A_service_outside_the_allowlist_is_a_404()
    {
        using var response = await Client().GetAsync(new Uri("/v1/proxy/evil/echo/x", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_service", await Error(response));
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task The_status_of_the_service_comes_back_as_it_is()
    {
        using var response = await Client().GetAsync(new Uri("/v1/proxy/svc/status/418", UriKind.Relative), Ct);

        Assert.Equal((HttpStatusCode)418, response.StatusCode);
        Assert.Equal("upstream 418", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_redirect_is_followed_on_the_same_host_only()
    {
        using var same = await Client().GetAsync(new Uri("/v1/proxy/svc/redirect-same", UriKind.Relative), Ct);
        using var other = await Client().GetAsync(new Uri("/v1/proxy/svc/redirect-other", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal("/echo/after", Assert.Single(upstream.Requests).Target);
        Assert.Equal(HttpStatusCode.BadGateway, other.StatusCode);
        Assert.Equal("upstream_redirect", await Error(other));
    }

    [Fact]
    public async Task Bodies_over_the_maximum_are_refused_both_ways()
    {
        using var tooBigUp = await Client().PostAsync(new Uri("/v1/proxy/svc/echo/x", UriKind.Relative), new ByteArrayContent(new byte[1001]), Ct);
        using var tooBigDown = await Client().GetAsync(new Uri("/v1/proxy/svc/big?n=1001", UriKind.Relative), Ct);
        using var fits = await Client().GetAsync(new Uri("/v1/proxy/svc/big?n=1000", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBigUp.StatusCode);
        Assert.Equal("too_large", await Error(tooBigUp));
        Assert.Equal(HttpStatusCode.BadGateway, tooBigDown.StatusCode);
        Assert.Equal("upstream_too_large", await Error(tooBigDown));
        Assert.Equal(1000, (await fits.Content.ReadAsByteArrayAsync(Ct)).Length);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task A_slow_service_is_a_504()
    {
        using var response = await Client().GetAsync(new Uri("/v1/proxy/svc/slow", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("upstream_timeout", await Error(response));
    }

    [Fact]
    public async Task An_answer_that_breaks_off_is_a_502()
    {
        using var response = await Client().GetAsync(new Uri("/v1/proxy/svc/broken", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("upstream_unavailable", await Error(response));
    }

    [Fact]
    public async Task An_unreachable_service_is_a_502()
    {
        host.Upstreams["down"] = new Uri("http://127.0.0.1:1");

        using var response = await Client().GetAsync(new Uri("/v1/proxy/down/x", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("upstream_unavailable", await Error(response));
    }

    [Fact]
    public async Task After_120_calls_in_a_minute_it_is_a_429()
    {
        var http = Client();
        for (var i = 0; i < 120; i++)
        {
            using var ok = await http.GetAsync(new Uri("/v1/proxy/svc/status/204", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        }

        using var response = await http.GetAsync(new Uri("/v1/proxy/svc/status/204", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("rate_limited", await Error(response));
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Credentials_queries_headers_and_bodies_never_reach_the_log()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/proxy/svc/echo/secret-path?KEY=QRZ-KEY-SECRET")
        {
            Content = new StringContent("password=EQSL-PASSWORD-SECRET", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "SOTA-TOKEN-SECRET");
        using var ok = await Client().SendAsync(request, Ct);
        using var down = await Client().GetAsync(new Uri("/v1/proxy/svc/slow?KEY=QRZ-KEY-SECRET", UriKind.Relative), Ct);

        var log = string.Join('\n', host.Logs.Lines);
        Assert.Contains($"Proxy {TestHost.UserId} svc POST -> 200 in ", log, StringComparison.Ordinal);
        Assert.Contains($"Proxy {TestHost.UserId} svc GET -> UpstreamTimeout in ", log, StringComparison.Ordinal);
        foreach (var secret in new[] { "SECRET", "secret-path", "KEY=", TestHost.ValidToken, "Bearer" })
        {
            Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        }
    }
}
