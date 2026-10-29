using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Proxy;
using HamQso.Spots.Infrastructure.Proxy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>
/// ANY /v1/proxy/{service}/{**path}: the web app reaches the services that send
/// no CORS headers (F58 decision 16), only those of <see cref="ProxyAllowlist"/>.
/// The caller is a HamQSO user (anonymous users too) with its Supabase token in
/// <see cref="BearerAuth.ProxyHeader"/>: "Authorization" goes to the service
/// untouched (the SOTA bearer). Nothing of the request or the answer is kept or
/// logged (see <see cref="ForwardToUpstream"/>). Contract in docs/protocol.md.
/// </summary>
public static class ProxyEndpoints
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "DELETE"];

    public static void MapProxyApi(this IEndpointRouteBuilder app) =>
        app.MapMethods("/v1/proxy/{service}/{**path}", Methods, async (HttpContext http, string service, string? path, ITokenValidator tokens, ForwardToUpstream forward, IOptions<ProxyOptions> options, CancellationToken ct) =>
        {
            var user = await BearerAuth.UserAsync(http, BearerAuth.ProxyHeader, tokens, ct);
            if (user is null || user.UserId.Length == 0) return Results.Unauthorized();
            if (user.IsReader) return ApiResults.Error(StatusCodes.Status403Forbidden, "not_allowed");
            if (!forward.Knows(service)) return ApiResults.Error(StatusCodes.Status404NotFound, "unknown_service");

            var (body, tooLarge) = await ReadBodyAsync(http.Request, options.Value.MaxBodyBytes, ct);
            if (tooLarge) return ApiResults.Error(StatusCodes.Status413PayloadTooLarge, "too_large");

            var call = new ProxyCall(service, http.Request.Method, path ?? "", http.Request.QueryString.Value ?? "", Forwarded(http.Request), body, http.Request.ContentType);
            // The address of the client: behind Traefik, from X-Forwarded-For of a trusted proxy only (ClientAddress).
            var result = await forward.ExecuteAsync(user, AddressKey.Of(http.Connection.RemoteIpAddress), call, ct);
            if (result.Reply is not { } reply)
            {
                return result.Admission == ProxyAdmission.Busy
                    ? ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "busy")
                    : ApiResults.RateLimited(http, result.RetryAfter ?? TimeSpan.Zero);
            }
            return reply.Outcome switch
            {
                ProxyOutcome.Answered => new UpstreamResult(reply),
                ProxyOutcome.UnknownService => ApiResults.Error(StatusCodes.Status404NotFound, "unknown_service"),
                ProxyOutcome.BadPath => ApiResults.Error(StatusCodes.Status400BadRequest, "bad_path"),
                ProxyOutcome.UpstreamTimeout => ApiResults.Error(StatusCodes.Status504GatewayTimeout, "upstream_timeout"),
                ProxyOutcome.UpstreamTooLarge => ApiResults.Error(StatusCodes.Status502BadGateway, "upstream_too_large"),
                ProxyOutcome.UpstreamRedirect => ApiResults.Error(StatusCodes.Status502BadGateway, "upstream_redirect"),
                _ => ApiResults.Error(StatusCodes.Status502BadGateway, "upstream_unavailable"),
            };
        });

    private static Dictionary<string, string> Forwarded(HttpRequest request) =>
        UpstreamProxy.ForwardedHeaders
            .Where(name => request.Headers.ContainsKey(name))
            .ToDictionary(name => name, name => request.Headers[name].ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The body (null when there is none), or too large past <paramref name="max"/> bytes.</summary>
    private static async Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(HttpRequest request, int max, CancellationToken ct)
    {
        if (request.ContentLength > max) return (null, true);
        if (request.ContentLength is null or 0 && request.Headers.TransferEncoding.Count == 0) return (null, false);
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = max;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > max) return (null, true);
                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException)
        {
            return (null, true);
        }
        return (buffer.ToArray(), false);
    }

    /// <summary>The service's status, Content-Type and body; no other header of the service comes back.</summary>
    private sealed class UpstreamResult(ProxyReply reply) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = reply.Status;
            if (reply.ContentType is not null) httpContext.Response.ContentType = reply.ContentType;
            // 204 and 304 have no body, not even an empty one with a length.
            if (reply.Body.Length == 0) return;
            httpContext.Response.ContentLength = reply.Body.Length;
            await httpContext.Response.Body.WriteAsync(reply.Body);
        }
    }
}
