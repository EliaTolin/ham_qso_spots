using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Infrastructure.Rbn;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>
/// GET /v1/rbn/{callsign} (slashes included: /v1/rbn/EA8/IU4VRB/P): the coverage test of the web app (F58 decision 9).
/// A Server-Sent Events stream with one event per RBN spot line of the
/// station, from the shared RBN connection (<see cref="RbnTelnetFeed"/>).
/// Signed-in HamQSO users only; the token in "Authorization: Bearer" or in
/// ?access_token= (EventSource cannot set headers), never logged.
/// Contract in docs/protocol.md.
/// </summary>
public static partial class RbnEndpoints
{
    public static void MapRbnApi(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/rbn/{**callsign}", async (HttpContext http, string? callsign, ITokenValidator tokens, WatchRbn watch, IOptions<RbnOptions> options, TimeProvider time, ILoggerFactory loggers) =>
        {
            // Created now: a stream can outlive the logger factory when the server stops.
            var logger = loggers.CreateLogger(typeof(RbnEndpoints));
            var user = await BearerAuth.UserFromHeaderOrQueryAsync(http, tokens, http.RequestAborted);
            if (user is null || user.UserId.Length == 0) return Results.Unauthorized();
            if (user.IsReader) return ApiResults.Error(StatusCodes.Status403Forbidden, "not_allowed");
            if (user.IsAnonymous) return ApiResults.Error(StatusCodes.Status403Forbidden, "account_required");
            // As written in the path: no spaces or line breaks to trim away. Slashes are part of it (EA8/IU4VRB/P).
            if (callsign is null || callsign.Trim() != callsign || !Callsign.IsPlausible(callsign)) return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_callsign");
            using var lease = watch.TryStart(user.UserId);
            if (lease is null) return ApiResults.Error(StatusCodes.Status429TooManyRequests, "too_many_streams");

            var station = callsign.ToUpperInvariant();
            var (lines, reason) = await StreamAsync(http, watch, station, options.Value, time);
            LogStream(logger, user.UserId, station, lines, reason);
            return Results.Empty;
        });

    /// <summary>One SSE event: the line has no newline (the feed trims it), checked again here.</summary>
    public static string Event(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return $"data: {line.ReplaceLineEndings(" ")}\n\n";
    }

    public const string Opened = ": connected\n\n";
    public const string Ping = ": ping\n\n";
    public const string TimeLimit = "event: end\ndata: time_limit\n\n";

    private static async Task<(int Lines, string Reason)> StreamAsync(HttpContext http, WatchRbn watch, string station, RbnOptions o, TimeProvider time)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var limit = new CancellationTokenSource(o.MaxStreamDuration, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, limit.Token);
        var count = 0;
        var spots = watch.WatchAsync(station, stop.Token).GetAsyncEnumerator(stop.Token);
        Task<bool>? next = null;
        try
        {
            await WriteAsync(http, Opened);
            next = spots.MoveNextAsync().AsTask();
            while (true)
            {
                using var beat = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var done = await Task.WhenAny(next, Task.Delay(o.Heartbeat, time, beat.Token));
                if (done == next)
                {
                    await beat.CancelAsync();
                    if (!await next) break;
                    await WriteAsync(http, Event(spots.Current.Raw));
                    count++;
                    next = spots.MoveNextAsync().AsTask();
                }
                else if (!stop.IsCancellationRequested)
                {
                    await WriteAsync(http, Ping);
                }
                else
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The client went away while being written to.
        }
        finally
        {
            await stop.CancelAsync();
            if (next is not null)
            {
                try
                {
                    await next;
                }
                catch (OperationCanceledException)
                {
                }
            }
            await spots.DisposeAsync();
        }

        if (!limit.IsCancellationRequested || http.RequestAborted.IsCancellationRequested) return (count, "closed");
        try
        {
            await WriteAsync(http, TimeLimit);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        return (count, "time_limit");
    }

    private static async Task WriteAsync(HttpContext http, string text)
    {
        await http.Response.WriteAsync(text, http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RBN stream of user {UserId} for {Callsign}: {Lines} lines, {Reason}")]
    private static partial void LogStream(ILogger logger, string userId, string callsign, int lines, string reason);
}
