using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Sending;
using Microsoft.AspNetCore.Http.Features;

namespace HamQso.Spots.Host;

/// <summary>
/// POST /v1/dx/spots: the web app (no telnet in a browser) sends a DX spot
/// through the server, logged in with the callsign of the user's profile.
/// Only signed-in HamQSO users: the Supabase access token as Bearer; users of
/// the reader projects (HamQRG) may only read, anonymous users need an account.
/// Contract in docs/protocol.md.
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
            if (user.IsReader) return ApiResults.Error(StatusCodes.Status403Forbidden, "not_allowed");
            if (user.IsAnonymous) return ApiResults.Error(StatusCodes.Status403Forbidden, "account_required");

            var body = await ReadBodyAsync(http, ct);
            if (body is null) return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_line");

            var result = await send.ExecuteAsync(user.UserId, body.Line, body.Ssid, ct);
            return result.Outcome switch
            {
                SendDxSpotOutcome.Sent => Results.Ok(new { sent = true, replies = result.Replies }),
                SendDxSpotOutcome.InvalidLine => ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_line"),
                SendDxSpotOutcome.InvalidSsid => ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_ssid"),
                SendDxSpotOutcome.NoCallsign => ApiResults.Error(StatusCodes.Status422UnprocessableEntity, "no_callsign"),
                SendDxSpotOutcome.RateLimited => ApiResults.RateLimited(http, result.RetryAfter ?? TimeSpan.Zero),
                SendDxSpotOutcome.Busy => ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "busy"),
                SendDxSpotOutcome.Disabled => ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "sending_disabled"),
                _ => ApiResults.Error(StatusCodes.Status502BadGateway, "node_unavailable"),
            };
        });

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
