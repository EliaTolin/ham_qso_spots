using System.Net;
using System.Net.Http.Headers;
using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Proxy;

/// <summary>
/// Forwards a call to an allowlisted service and reads its answer, bounded by
/// <see cref="ProxyOptions"/>. Only Accept, Authorization, User-Agent and the
/// body's Content-Type go out (no cookies, no hop-by-hop headers); only the
/// status, Content-Type and body come back. Redirects are followed only on the
/// service's own host, at most <see cref="MaxRedirects"/>. Nothing of the call
/// is logged here (the HttpClient has no loggers, see the Host).
/// </summary>
public sealed class UpstreamProxy(HttpClient http, ProxyAllowlist allowlist, IOptions<ProxyOptions> options) : IUpstreamProxy
{
    /// <summary>The name of the HttpClient: no cookies, no automatic redirects, no logging.</summary>
    public const string ClientName = "proxy";

    public const int MaxRedirects = 5;

    /// <summary>The request headers that go to the service, besides the body's Content-Type.</summary>
    public static readonly IReadOnlyList<string> ForwardedHeaders = ["Accept", "Authorization", "User-Agent"];

    public bool Knows(string service) => allowlist.BaseOf(service) is not null;

    public async Task<ProxyReply> SendAsync(ProxyCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (allowlist.BaseOf(request.Service) is not { } service) return ProxyReply.Failed(ProxyOutcome.UnknownService);
        if (UpstreamUri.Build(service, request.Path, request.Query) is not { } uri) return ProxyReply.Failed(ProxyOutcome.BadPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Timeout);
        var method = new HttpMethod(request.Method);
        var body = request.Body;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var message = Message(method, uri, request, body);
                using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                {
                    var target = new Uri(uri, location);
                    if (!UpstreamUri.IsSameOrigin(target, service) || redirects >= MaxRedirects) return ProxyReply.Failed(ProxyOutcome.UpstreamRedirect);
                    // 301/302/303 turn into a GET without body, as browsers do; 307/308 repeat the request.
                    if (response.StatusCode is not (HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) && method != HttpMethod.Head)
                    {
                        method = HttpMethod.Get;
                        body = null;
                    }
                    uri = target;
                    continue;
                }
                var content = await ReadBoundedAsync(response.Content, options.Value.MaxBodyBytes, timeout.Token).ConfigureAwait(false);
                return content is null
                    ? ProxyReply.Failed(ProxyOutcome.UpstreamTooLarge)
                    : new ProxyReply(ProxyOutcome.Answered, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), content);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProxyReply.Failed(ProxyOutcome.UpstreamTimeout);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            // Unreachable, or the answer broke off while read. The message may hold the address: it is not logged.
            return ProxyReply.Failed(ProxyOutcome.UpstreamUnavailable);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => (int)status is >= 300 and < 400 and not 304;

    private static HttpRequestMessage Message(HttpMethod method, Uri uri, ProxyCall request, byte[]? body)
    {
        var message = new HttpRequestMessage(method, uri);
        foreach (var name in ForwardedHeaders)
        {
            if (request.Headers.TryGetValue(name, out var value)) message.Headers.TryAddWithoutValidation(name, value);
        }
        if (body is not null)
        {
            message.Content = new ByteArrayContent(body);
            if (request.ContentType is { } type && MediaTypeHeaderValue.TryParse(type, out var parsed)) message.Content.Headers.ContentType = parsed;
        }
        return message;
    }

    /// <summary>The body, or null when it is over <paramref name="max"/> bytes.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int max, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > max) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
