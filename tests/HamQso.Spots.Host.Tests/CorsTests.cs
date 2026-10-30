using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace HamQso.Spots.Host.Tests;

/// <summary>CORS of the HTTP API for the web app (origins from configuration).</summary>
public sealed class CorsTests : IDisposable
{
    private const string WebApp = "https://app.hamqso.example";

    private readonly TestHost host = new();

    public CorsTests() => host.Settings["Cors:Origins:2"] = WebApp;

    public void Dispose() => host.Dispose();

    private async Task<HttpResponseMessage> Preflight(string path, string origin, string method, string headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return await host.Client(token: null).SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    [Theory]
    [InlineData("/v1/dx/spots", "POST", "authorization,content-type")]
    [InlineData("/v1/spots", "GET", "authorization")]
    [InlineData("/v1/references/catalog", "GET", "authorization,if-none-match")]
    [InlineData("/v1/proxy/qrz-logbook/api", "PUT", "authorization,content-type,x-hamqso-auth")]
    [InlineData("/v1/proxy/sotawatch/api/spots", "DELETE", "x-hamqso-auth")]
    public async Task The_web_app_origin_passes_the_preflight_of_every_endpoint(string path, string method, string headers)
    {
        using var response = await Preflight(path, WebApp, method, headers);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(WebApp, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains(method, Header(response, "Access-Control-Allow-Methods"), StringComparison.Ordinal);
        Assert.Equal("600", Header(response, "Access-Control-Max-Age"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Another_origin_gets_no_CORS_answer_and_only_the_API_headers_and_methods_are_allowed()
    {
        using var evil = await Preflight("/v1/dx/spots", "https://evil.example", "POST", "authorization");
        using var ok = await Preflight("/v1/dx/spots", WebApp, "POST", "authorization");

        Assert.Equal("", Header(evil, "Access-Control-Allow-Origin"));
        // The browser refuses what is not listed (another header, PATCH).
        Assert.Equal(["Authorization", "Content-Type", "X-HamQSO-Auth", "If-None-Match"], Header(ok, "Access-Control-Allow-Headers").Split(','));
        Assert.Equal(["GET", "POST", "PUT", "DELETE", "OPTIONS"], Header(ok, "Access-Control-Allow-Methods").Split(','));
    }

    [Fact]
    public async Task A_real_request_from_the_web_app_gets_its_origin_back()
    {
        var http = host.Client();
        http.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");

        using var response = await http.GetAsync(new Uri("/v1/spots?program=dx", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", Header(response, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("https://app.hamqso.app", HttpStatusCode.NoContent, "https://app.hamqso.app")]
    [InlineData("http://localhost:5173", HttpStatusCode.NoContent, "")]
    [InlineData("https://evil.example", HttpStatusCode.NoContent, "")]
    public async Task In_production_only_the_web_app_at_app_hamqso_app_is_allowed(string origin, HttpStatusCode status, string allowed)
    {
        using var production = new TestHost { Environment = "Production" };
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/dx/spots");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        using var response = await production.Client(token: null).SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(allowed, Header(response, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("https://app.hamqso.example", true)]
    [InlineData("HTTPS://APP.HAMQSO.EXAMPLE", true)]
    [InlineData("http://localhost", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("https://localhost:5173", false)]
    [InlineData("http://localhost:5173.evil.example", false)]
    [InlineData("http://localhost.evil.example", false)]
    [InlineData("http://localhost:", false)]
    [InlineData("https://app.hamqso.example.evil", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    public void Origins_match_exactly_or_with_any_port(string origin, bool allowed) =>
        Assert.Equal(allowed, ApiCors.IsAllowed(origin, ["https://app.hamqso.example", "http://localhost:*", "http://127.0.0.1:*"]));

    [Fact]
    public async Task The_web_app_reads_the_ETag_and_the_count_of_the_catalog()
    {
        host.Services.GetRequiredService<Application.Catalog.CurrentReferenceCatalog>().File = Domain.References.ReferenceCatalogFormat.Write(
            [new Domain.References.CatalogReference("IT-1271", "POTA", "Park", null, null, null, null, null, null, null)],
            new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero));
        var http = host.Client();
        http.DefaultRequestHeaders.Add("Origin", WebApp);

        using var response = await http.GetAsync(new Uri("/v1/references/catalog", UriKind.Relative), TestContext.Current.CancellationToken);

        var exposed = Header(response, "Access-Control-Expose-Headers").Split(',');
        Assert.Contains("ETag", exposed);
        Assert.Contains("X-Reference-Count", exposed);
    }
}
