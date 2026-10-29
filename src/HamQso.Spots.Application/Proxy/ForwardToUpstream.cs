using System.Diagnostics;
using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Proxy;

/// <summary>The service's reply, or why the call did not go (rate limited or busy, with the wait).</summary>
public sealed record ForwardResult(ProxyReply? Reply, ProxyAdmission Admission, TimeSpan? RetryAfter);

/// <summary>
/// The CORS proxy of the web app (F58 decision 16): a call of a HamQSO user to
/// an allowlisted service, within the limits of <see cref="ProxyRateLimiter"/>.
/// The calls carry the user's credentials of those services (QRZ API key, eQSL
/// password, SOTA token): nothing of them is kept or logged. The log line has
/// only user id, service key, method, status, duration and sizes, never path,
/// query, headers, bodies or the client address.
/// </summary>
public sealed partial class ForwardToUpstream(IUpstreamProxy upstream, ProxyRateLimiter limiter, TimeProvider time, ILogger<ForwardToUpstream> logger)
{
    public bool Knows(string service) => upstream.Knows(service);

    /// <summary>
    /// Null reply: not sent (<see cref="ForwardResult.Admission"/> says why).
    /// <paramref name="clientAddress"/> only counts for its limit, it is never logged.
    /// </summary>
    public async Task<ForwardResult> ExecuteAsync(TokenUser user, string clientAddress, ProxyCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(call);
        var userId = user.UserId;
        var (admission, wait) = limiter.TryAcquire(userId, user.IsAnonymous, clientAddress);
        if (admission != ProxyAdmission.Allowed)
        {
            LogCall(logger, userId, call.Service, call.Method, admission == ProxyAdmission.Busy ? "busy" : "rate_limited", 0, call.Body?.Length ?? 0, 0);
            return new ForwardResult(null, admission, wait);
        }
        var started = time.GetTimestamp();
        var reply = await upstream.SendAsync(call, cancellationToken).ConfigureAwait(false);
        var elapsed = time.GetElapsedTime(started);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var outcome = reply.Outcome == ProxyOutcome.Answered ? reply.Status.ToString(System.Globalization.CultureInfo.InvariantCulture) : reply.Outcome.ToString();
            LogCall(logger, userId, call.Service, call.Method, outcome, (long)elapsed.TotalMilliseconds, call.Body?.Length ?? 0, reply.Body.Length);
        }
        return new ForwardResult(reply, ProxyAdmission.Allowed, null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Proxy {UserId} {Service} {Method} -> {Outcome} in {Ms} ms, {RequestBytes} B up, {ResponseBytes} B down")]
    private static partial void LogCall(ILogger logger, string userId, string service, string method, string outcome, long ms, int requestBytes, int responseBytes);
}
