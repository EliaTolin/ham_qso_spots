using System.Net;
using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Proxy;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Proxy;

/// <summary>The forwarding rules, against a fake service behind an HttpMessageHandler.</summary>
public sealed class UpstreamProxyTests : IDisposable
{
    private static readonly Uri Service = new("https://service.example");

    private readonly FakeService service = new();
    private readonly HttpClient http;
    private readonly ProxyOptions settings = new() { Timeout = TimeSpan.FromSeconds(5), MaxBodyBytes = 1000 };
    private readonly UpstreamProxy proxy;

    public UpstreamProxyTests()
    {
        http = new HttpClient(service);
        proxy = new UpstreamProxy(http, new ProxyAllowlist(new Dictionary<string, Uri> { ["svc"] = Service }), Options.Create(settings));
    }

    public void Dispose() => http.Dispose();

    private static ProxyCall Call(string method = "GET", string path = "api", string query = "", Dictionary<string, string>? headers = null, byte[]? body = null, string? type = null) =>
        new("svc", method, path, query, headers ?? [], body, type);

    private Task<ProxyReply> Send(ProxyCall call) => proxy.SendAsync(call, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Method_path_query_body_and_the_allowed_headers_go_through_unchanged()
    {
        var body = Encoding.UTF8.GetBytes("KEY=secret&ACTION=INSERT");
        service.Answer = _ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("RESULT=OK", Encoding.UTF8, "text/plain") };

        var reply = await Send(Call("POST", "api", "?x=1", new() { ["Authorization"] = "Bearer sota-token", ["Accept"] = "*/*", ["User-Agent"] = "HamQSO/1.0" }, body, "application/x-www-form-urlencoded"));

        var request = Assert.Single(service.Requests);
        Assert.Equal(("POST", "https://service.example/api?x=1"), (request.Method, request.Url));
        Assert.Equal("Bearer sota-token", request.Headers["Authorization"]);
        Assert.Equal("*/*", request.Headers["Accept"]);
        Assert.Equal("HamQSO/1.0", request.Headers["User-Agent"]);
        Assert.Equal("application/x-www-form-urlencoded", request.Headers["Content-Type"]);
        Assert.Equal(body, request.Body);
        Assert.Equal((ProxyOutcome.Answered, 201, "text/plain; charset=utf-8", "RESULT=OK"), (reply.Outcome, reply.Status, reply.ContentType, Encoding.UTF8.GetString(reply.Body)));
    }

    [Fact]
    public async Task Cookies_and_other_headers_never_go_out()
    {
        await Send(Call(headers: new() { ["Cookie"] = "session=1", ["X-Forwarded-For"] = "1.2.3.4", ["Connection"] = "keep-alive", ["Accept"] = "text/html" }));

        Assert.Equal(["Accept"], Assert.Single(service.Requests).Headers.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_error_of_the_service_comes_back_as_it_is()
    {
        service.Answer = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"bad key\"}", Encoding.UTF8, "application/json") };

        var reply = await Send(Call());

        Assert.Equal((ProxyOutcome.Answered, 401, "{\"error\":\"bad key\"}"), (reply.Outcome, reply.Status, Encoding.UTF8.GetString(reply.Body)));
    }

    [Fact]
    public async Task A_redirect_on_the_same_host_is_followed_as_a_browser_does()
    {
        service.Answer = r => r.RequestUri!.AbsolutePath == "/api"
            ? Redirect(HttpStatusCode.Found, "/after")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("after") };

        var reply = await Send(Call("POST", body: [1, 2, 3], type: "text/plain"));

        Assert.Equal("after", Encoding.UTF8.GetString(reply.Body));
        Assert.Equal(("GET", "https://service.example/after", (byte[]?)null), (service.Requests[1].Method, service.Requests[1].Url, service.Requests[1].Body));
    }

    [Fact]
    public async Task A_307_repeats_the_method_and_the_body()
    {
        service.Answer = r => r.RequestUri!.AbsolutePath == "/api" ? Redirect(HttpStatusCode.TemporaryRedirect, "https://service.example/v2") : new HttpResponseMessage(HttpStatusCode.OK);

        await Send(Call("POST", body: [1, 2, 3], type: "text/plain"));

        Assert.Equal("POST", service.Requests[1].Method);
        Assert.Equal([1, 2, 3], service.Requests[1].Body!);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("http://service.example/plain")]
    [InlineData("//evil.example/x")]
    public async Task A_redirect_to_another_host_is_never_followed(string location)
    {
        service.Answer = _ => Redirect(HttpStatusCode.Found, location);

        var reply = await Send(Call());

        Assert.Equal(ProxyOutcome.UpstreamRedirect, reply.Outcome);
        Assert.Single(service.Requests);
    }

    [Fact]
    public async Task Endless_redirects_stop()
    {
        service.Answer = _ => Redirect(HttpStatusCode.Found, "/again");

        Assert.Equal(ProxyOutcome.UpstreamRedirect, (await Send(Call())).Outcome);
        Assert.Equal(UpstreamProxy.MaxRedirects + 1, service.Requests.Count);
    }

    [Fact]
    public async Task An_answer_over_the_maximum_is_refused_with_or_without_its_length()
    {
        service.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1001]) };
        Assert.Equal(ProxyOutcome.UpstreamTooLarge, (await Send(Call())).Outcome);

        service.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[1001])) };
        Assert.Equal(ProxyOutcome.UpstreamTooLarge, (await Send(Call())).Outcome);

        service.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1000]) };
        Assert.Equal(1000, (await Send(Call())).Body.Length);
    }

    [Fact]
    public async Task A_slow_service_is_a_timeout_and_an_unreachable_one_unavailable()
    {
        settings.Timeout = TimeSpan.FromMilliseconds(100);
        service.Delay = TimeSpan.FromSeconds(10);
        Assert.Equal(ProxyOutcome.UpstreamTimeout, (await Send(Call())).Outcome);

        service.Delay = TimeSpan.Zero;
        service.Answer = _ => throw new HttpRequestException("Connection refused (service.example:443)");
        Assert.Equal(ProxyOutcome.UpstreamUnavailable, (await Send(Call())).Outcome);
    }

    [Fact]
    public async Task An_answer_that_breaks_off_while_read_is_unavailable()
    {
        service.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) };

        Assert.Equal(ProxyOutcome.UpstreamUnavailable, (await Send(Call())).Outcome);
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("connection reset"));
    }

    [Fact]
    public async Task Unknown_services_and_bad_paths_never_leave()
    {
        Assert.Equal(ProxyOutcome.UnknownService, (await Send(Call() with { Service = "other" })).Outcome);
        Assert.Equal(ProxyOutcome.BadPath, (await Send(Call(path: "../x"))).Outcome);
        Assert.Empty(service.Requests);
        Assert.False(proxy.Knows("other"));
        Assert.True(proxy.Knows("svc"));
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed record Seen(string Method, string Url, Dictionary<string, string> Headers, byte[]? Body);

    private sealed class FakeService : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        public TimeSpan Delay { get; set; }

        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            byte[]? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                if (request.Content.Headers.ContentType is { } type) headers["Content-Type"] = type.ToString();
            }
            Requests.Add(new Seen(request.Method.Method, request.RequestUri!.AbsoluteUri, headers, body));
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return Answer(request);
        }
    }
}
