using System.Globalization;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Infrastructure.Rbn;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>
/// The Reverse Beacon Network for the coverage test. Contract in docs/protocol.md.
/// <list type="bullet">
/// <item>GET /v1/rbn/{callsign} (slashes included: /v1/rbn/EA8/IU4VRB/P): a Server-Sent
/// Events stream with one event per RBN spot of the station, from the shared RBN
/// connection (<see cref="RbnTelnetFeed"/>). v1 (F58, the web app already out):
/// the raw line, signed-in users only. v2 (<c>?v=2</c>, F71, phone and web): the
/// spot read by the server with the skimmer's true position, plus the feed's
/// state; anonymous users too (RBN spots are public, the call is in the path).</item>
/// <item>GET /v1/rbn/skimmers?band=20m[&amp;from=…&amp;to=…] (F71): the skimmers listening on a band.</item>
/// </list>
/// The token in "Authorization: Bearer", as for every endpoint (the apps read the stream with fetch, not EventSource).
/// </summary>
public static partial class RbnEndpoints
{
    public static void MapRbnApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/rbn/skimmers", async (HttpContext http, ITokenValidator tokens, GetListeningSkimmers skimmers, CancellationToken ct) =>
        {
            var user = await BearerAuth.UserAsync(http, tokens, ct);
            if (user is null) return Results.Unauthorized();
            if (user.IsReader) return ApiResults.Error(StatusCodes.Status403Forbidden, "not_allowed");
            var q = http.Request.Query;
            if (!TryTime(q["from"], out var from) || !TryTime(q["to"], out var to)) return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_window");
            var result = await skimmers.ExecuteAsync(q["band"].ToString() is { Length: > 0 } b ? b : null, from, to, ct);
            return result.Value is { } value
                ? Results.Text(RbnJson.Skimmers(value), "application/json")
                : ApiResults.Error(StatusCodes.Status400BadRequest, result.Error!);
        });

        app.MapGet("/v1/rbn/{**callsign}", async (HttpContext http, string? callsign, ITokenValidator tokens, WatchRbn watch, IOptions<RbnOptions> options, TimeProvider time, ILoggerFactory loggers) =>
        {
            // Created now: a stream can outlive the logger factory when the server stops.
            var logger = loggers.CreateLogger(typeof(RbnEndpoints));
            var version = http.Request.Query["v"].ToString();
            if (version is not ("" or "1" or "2")) return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_version");
            var v2 = version == "2";
            var user = await BearerAuth.UserAsync(http, tokens, http.RequestAborted);
            if (user is null || user.UserId.Length == 0) return Results.Unauthorized();
            if (user.IsReader) return ApiResults.Error(StatusCodes.Status403Forbidden, "not_allowed");
            if (user.IsAnonymous && !v2) return ApiResults.Error(StatusCodes.Status403Forbidden, "account_required");
            // As written in the path: no spaces or line breaks to trim away. Slashes are part of it (EA8/IU4VRB/P).
            if (callsign is null || callsign.Trim() != callsign || !Callsign.IsPlausible(callsign)) return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_callsign");
            using var lease = watch.TryStart(user.UserId);
            if (lease is null) return ApiResults.Error(StatusCodes.Status429TooManyRequests, "too_many_streams");

            var station = callsign.ToUpperInvariant();
            var (lines, reason) = await StreamAsync(http, watch, station, v2, options.Value, time);
            LogStream(logger, user.UserId, station, v2 ? 2 : 1, lines, reason);
            return Results.Empty;
        });
    }

    /// <summary>One v1 SSE event: the line has no newline (the feed trims it), checked again here.</summary>
    public static string Event(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return $"data: {line.ReplaceLineEndings(" ")}\n\n";
    }

    /// <summary>One v2 SSE event of type <paramref name="type"/> with one line of JSON.</summary>
    public static string Event(string type, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return $"event: {type}\n{Event(json)}";
    }

    public const string Opened = ": connected\n\n";
    public const string Ping = ": ping\n\n";
    public const string TimeLimit = "event: end\ndata: time_limit\n\n";

    /// <summary>A time of the query: absent is null; ISO 8601, read as UTC without an offset.</summary>
    private static bool TryTime(string? text, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrEmpty(text)) return true;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) return false;
        value = t;
        return true;
    }

    private static async Task<(int Lines, string Reason)> StreamAsync(HttpContext http, WatchRbn watch, string station, bool v2, RbnOptions o, TimeProvider time)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var limit = new CancellationTokenSource(o.MaxStreamDuration, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, limit.Token);
        var count = 0;
        var said = RbnFeedState.Up;
        var spots = watch.WatchAsync(station, stop.Token).GetAsyncEnumerator(stop.Token);
        Task<bool>? next = null;
        try
        {
            await WriteAsync(http, Opened);
            next = spots.MoveNextAsync().AsTask();
            while (true)
            {
                // v2: the feed's state, said when it is not what the client was last told (up at the start).
                if (v2 && watch.Status.State != said)
                {
                    said = watch.Status.State;
                    await WriteAsync(http, Event("status", RbnJson.Status(said)));
                }
                using var beat = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var waits = new List<Task> { next, Task.Delay(o.Heartbeat, time, beat.Token) };
                if (v2) waits.Add(watch.WaitForChangeAsync(beat.Token));
                var done = await Task.WhenAny(waits);
                await beat.CancelAsync();
                if (done == next)
                {
                    if (!await next) break;
                    await WriteAsync(http, v2 ? Event("spot", RbnJson.Spot(watch.Describe(spots.Current))) : Event(spots.Current.Raw));
                    count++;
                    next = spots.MoveNextAsync().AsTask();
                }
                else if (stop.IsCancellationRequested)
                {
                    break;
                }
                else if (done == waits[1])
                {
                    await WriteAsync(http, Ping);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "RBN stream of user {UserId} for {Callsign} (v{Version}): {Lines} lines, {Reason}")]
    private static partial void LogStream(ILogger logger, string userId, string callsign, int version, int lines, string reason);
}
