using System.Globalization;
using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Sending;
using Microsoft.AspNetCore.Http.Features;

namespace HamQso.Spots.Host;

/// <summary>
/// POST /v1/dx/spots: the web app (no telnet in a browser) sends a DX spot
/// through the server, logged in with the callsign of the user's profile.
/// Only HamQSO users: the Supabase access token as Bearer; users of the
/// reader projects (HamQRG) may only read. Contract in docs/protocol.md.
/// </summary>
public static class SendSpotEndpoints
{
    /// <summary>A spot request is under 200 bytes.</summary>
    public const long MaxBodyBytes = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapSendSpotApi(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/dx/spots", async (HttpContext http, ITokenValidator tokens, SendDxSpot send, CancellationToken ct) =>
        {
            var user = await BearerAuth.UserAsync(http, tokens, ct);
            if (user is null || user.UserId.Length == 0) return Results.Unauthorized();
            if (user.IsReader) return Error(StatusCodes.Status403Forbidden, "not_allowed");

            var body = await ReadBodyAsync(http, ct);
            if (body is null) return Error(StatusCodes.Status400BadRequest, "invalid_line");

            var result = await send.ExecuteAsync(user.UserId, body.Line, body.Ssid, ct);
            return result.Outcome switch
            {
                SendDxSpotOutcome.Sent => Results.Ok(new { sent = true, replies = result.Replies }),
                SendDxSpotOutcome.InvalidLine => Error(StatusCodes.Status400BadRequest, "invalid_line"),
                SendDxSpotOutcome.InvalidSsid => Error(StatusCodes.Status400BadRequest, "invalid_ssid"),
                SendDxSpotOutcome.NoCallsign => Error(StatusCodes.Status422UnprocessableEntity, "no_callsign"),
                SendDxSpotOutcome.RateLimited => RateLimited(http, result.RetryAfter ?? TimeSpan.Zero),
                _ => Error(StatusCodes.Status502BadGateway, "node_unavailable"),
            };
        });

    /// <summary>Whole seconds, at least 1: a client that waits that long is let through.</summary>
    public static int RetryAfterSeconds(TimeSpan wait) => Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));

    private static IResult RateLimited(HttpContext http, TimeSpan wait)
    {
        var seconds = RetryAfterSeconds(wait);
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return Results.Json(new { error = "rate_limited", retry_after_s = seconds }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static IResult Error(int status, string error) => Results.Json(new { error }, statusCode: status);

    /// <summary>The JSON body, or null when it is not one (or too big).</summary>
    private static async Task<SendSpotBody?> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxBodyBytes;
        if (http.Request.ContentLength > MaxBodyBytes) return null;
        try
        {
            return await JsonSerializer.DeserializeAsync<SendSpotBody>(http.Request.Body, Json, ct);
        }
        catch (Exception e) when (e is JsonException or BadHttpRequestException)
        {
            return null;
        }
    }

    public sealed record SendSpotBody(string? Line, int? Ssid);
}
