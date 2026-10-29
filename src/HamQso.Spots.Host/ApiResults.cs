using System.Globalization;

namespace HamQso.Spots.Host;

/// <summary>The error answers of the REST API: <c>{"error": "..."}</c>, and 429 with Retry-After.</summary>
public static class ApiResults
{
    public static IResult Error(int status, string error) => Results.Json(new { error }, statusCode: status);

    public static IResult RateLimited(HttpContext http, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(http);
        var seconds = RetryAfterSeconds(wait);
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return Results.Json(new { error = "rate_limited", retry_after_s = seconds }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Whole seconds, at least 1: a client that waits that long is let through.</summary>
    public static int RetryAfterSeconds(TimeSpan wait) => Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
}
