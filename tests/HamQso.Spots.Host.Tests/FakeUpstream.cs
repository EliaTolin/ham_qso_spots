using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Host.Tests;

/// <summary>
/// A service without CORS on loopback, for the proxy tests (never a real
/// service): /echo records what arrived and sets a cookie, /status/{code},
/// /redirect-same and /redirect-other, /big?n=, /slow, /broken. Also used by the
/// end-to-end tests (linked file).
/// </summary>
public sealed class FakeUpstream : IAsyncDisposable
{
    private readonly WebApplication app;

    private FakeUpstream(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    public ConcurrentQueue<Received> Requests { get; } = new();

    public static async Task<FakeUpstream> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        FakeUpstream? self = null;
        app.Map("/echo/{**rest}", async (HttpContext http) =>
        {
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            self!.Requests.Enqueue(new Received(
                http.Request.Method,
                http.Features.Get<IHttpRequestFeature>()!.RawTarget,
                http.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                body.ToArray()));
            http.Response.Headers.SetCookie = "upstream-session=abc; Path=/";
            http.Response.Headers["X-Upstream-Secret"] = "internal";
            return Results.Text("{\"ok\":true}", "application/json", statusCode: StatusCodes.Status200OK);
        });
        app.Map("/status/{code:int}", (int code) => code is 204 or 304 ? Results.StatusCode(code) : Results.Text($"upstream {code}", "text/plain", statusCode: code));
        app.Map("/broken", async (HttpContext http) =>
        {
            // Says 1000 bytes, sends 10 and drops the connection.
            http.Response.ContentLength = 1000;
            await http.Response.Body.WriteAsync(new byte[10]);
            http.Abort();
        });
        app.Map("/redirect-same", () => Results.Redirect("/echo/after"));
        app.Map("/redirect-other", () => Results.Redirect("http://127.0.0.2:9/stolen"));
        app.Map("/big", (int n) => Results.Bytes(new byte[n], "application/octet-stream"));
        app.Map("/slow", async (CancellationToken ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Results.Ok();
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        self = new FakeUpstream(app, new Uri(address));
        return self;
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    /// <summary>A request as the service saw it: the raw target keeps the path and query as sent.</summary>
    public sealed record Received(string Method, string Target, Dictionary<string, string> Headers, byte[] Body);
}
