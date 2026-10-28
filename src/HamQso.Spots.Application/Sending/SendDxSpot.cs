using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spotting;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Sending;

public enum SendDxSpotOutcome
{
    Sent,
    InvalidLine,
    InvalidSsid,
    RateLimited,
    NoCallsign,
    NodeUnavailable,
    Busy,
    LoginInUse,
}

public sealed record SendDxSpotResult(SendDxSpotOutcome Outcome, IReadOnlyList<string> Replies, TimeSpan? RetryAfter = null)
{
    public static SendDxSpotResult Failed(SendDxSpotOutcome outcome, TimeSpan? retryAfter = null) => new(outcome, [], retryAfter);
}

/// <summary>
/// POST /v1/dx/spots: a HamQSO user without telnet (the web app) sends a DX
/// spot through the server, as the phone app does by itself. The login is
/// always the callsign of the user's profile, never one from the request; only
/// a <see cref="DxSpotLine"/> goes out; at most <see cref="SpotRateLimiter.UserLimits"/>
/// per user and <see cref="SpotRateLimiter.ServerLimits"/> for the whole server.
/// The node's echo reaches the user with the other spots, as on the phone.
/// Every attempt is logged.
/// </summary>
public sealed partial class SendDxSpot(IProfileReader profiles, IClusterSender cluster, SpotRateLimiter limiter, ILogger<SendDxSpot> logger)
{
    public async Task<SendDxSpotResult> ExecuteAsync(string userId, string? line, int? ssid, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var result = await RunAsync(userId, line, ssid, cancellationToken).ConfigureAwait(false);
        // An invalid line is not written as is: it could forge log lines.
        LogSend(logger, userId, result.Login ?? "-", DxSpotLine.IsValid(line) ? line! : $"<invalid, {line?.Length ?? 0} chars>", result.Result.Outcome);
        return result.Result;
    }

    private async Task<(SendDxSpotResult Result, string? Login)> RunAsync(string userId, string? line, int? ssid, CancellationToken cancellationToken)
    {
        if (!DxSpotLine.IsValid(line)) return (SendDxSpotResult.Failed(SendDxSpotOutcome.InvalidLine), null);
        if (!ClusterLogin.IsValidSsid(ssid)) return (SendDxSpotResult.Failed(SendDxSpotOutcome.InvalidSsid), null);
        if (limiter.TryAcquireUser(userId) is { } wait) return (SendDxSpotResult.Failed(SendDxSpotOutcome.RateLimited, wait), null);

        // Checked again here, although the database checks it too: nothing else is ever a login.
        var callsign = ClusterLogin.NormalizeCallsign(await profiles.CallsignAsync(userId, cancellationToken).ConfigureAwait(false));
        if (callsign is null) return (SendDxSpotResult.Failed(SendDxSpotOutcome.NoCallsign), null);
        // Last: only spots that passed every other check use the server budget.
        if (limiter.TryAcquireServer() is not null)
        {
            limiter.RefundUser(userId);
            return (SendDxSpotResult.Failed(SendDxSpotOutcome.Busy), null);
        }

        var login = ClusterLogin.Create(callsign, ssid!.Value);
        var sent = await cluster.SendAsync(userId, login, line!, cancellationToken).ConfigureAwait(false);
        return (sent.Status switch
        {
            ClusterSendStatus.Sent => new SendDxSpotResult(SendDxSpotOutcome.Sent, sent.Replies),
            ClusterSendStatus.Busy => SendDxSpotResult.Failed(SendDxSpotOutcome.Busy),
            ClusterSendStatus.LoginInUse => SendDxSpotResult.Failed(SendDxSpotOutcome.LoginInUse),
            _ => SendDxSpotResult.Failed(SendDxSpotOutcome.NodeUnavailable),
        }, login);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DX spot by user {UserId} as {Login}: {Line} -> {Outcome}")]
    private static partial void LogSend(ILogger logger, string userId, string login, string line, SendDxSpotOutcome outcome);
}
