using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HamQso.Spots.Host.Tests;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>
/// /v1/proxy as the web app calls it: a real HamQSO user (anonymous, as the
/// proxy allows), the real token check, forwarding and CORS of the server run
/// here, and a <see cref="FakeUpstream"/> on loopback instead of QRZ or SOTA.
/// </summary>
public sealed class ProxyTests : IAsyncLifetime
{
    private const string WebOrigin = "http://localhost:5173";

    private readonly LocalServer server = new();
    private FakeUpstream upstream = null!;
    private TestUser user = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        upstream = await FakeUpstream.StartAsync();
        server.Upstreams["sotawatch"] = upstream.BaseUri;
        user = await TestUser.CreateAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await user.DisposeAsync();
        await server.DisposeAsync();
        await upstream.DisposeAsync();
    }

    private HttpClient Client(string? token)
    {
        var http = server.CreateClient();
        http.DefaultRequestHeaders.Add("Origin", WebOrigin);
        if (token is not null) http.DefaultRequestHeaders.Add("X-HamQSO-Auth", $"Bearer {token}");
        return http;
    }

    [Fact]
    public async Task A_HamQSO_user_reaches_the_service_with_its_own_Authorization_and_CORS_for_the_web_app()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/proxy/sotawatch/echo/api/spots?client=hamqso")
        {
            Content = new StringContent("{\"activatorCallsign\":\"Q0E2E\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sota-id-token");

        using var response = await Client(user.AccessToken).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WebOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("{\"ok\":true}", await response.Content.ReadAsStringAsync(Ct));
        var seen = Assert.Single(upstream.Requests);
        Assert.Equal(("POST", "/echo/api/spots?client=hamqso", "Bearer sota-id-token"), (seen.Method, seen.Target, seen.Headers["Authorization"]));
        Assert.Equal("{\"activatorCallsign\":\"Q0E2E\"}", Encoding.UTF8.GetString(seen.Body));
        Assert.False(seen.Headers.ContainsKey("X-HamQSO-Auth"));
    }

    [Fact]
    public async Task The_browser_preflight_is_answered()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/proxy/sotawatch/api/spots");
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-hamqso-auth");

        using var response = await server.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(WebOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not.a.token")]
    public async Task Without_a_valid_token_it_is_a_401_and_the_service_sees_nothing(string? token)
    {
        using var response = await Client(token).GetAsync(new Uri("/v1/proxy/sotawatch/echo/api", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task A_service_outside_the_allowlist_is_a_404()
    {
        using var response = await Client(user.AccessToken).GetAsync(new Uri("/v1/proxy/example/echo/api", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(upstream.Requests);
    }
}
