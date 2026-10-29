using System.Net;
using HamQso.Spots.Application.Sending;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Sending;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>
/// What runs without the Supabase secret key or outside production, decided
/// once at startup and written in the log: alerts need the key; spot sending
/// needs the key (to read the profile), must be enabled, and in Development may
/// only go to a node on this machine (a local run never spots on a real cluster).
/// Everything else (sources, archive, MQTT, REST, proxy, RBN, token checks with
/// the public keys) works without the key.
/// </summary>
public static partial class FeatureSwitches
{
    public static SpotSending Sending(bool hasSecretKey, DxSendOptions dxSend, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(dxSend);
        if (!hasSecretKey) return new SpotSending(false, "Supabase:SecretKey is not set (the profile callsign cannot be read)");
        if (!dxSend.Enabled) return new SpotSending(false, "DxSend:Enabled is false");
        if (isDevelopment && !IsLoopback(dxSend.Host)) return new SpotSending(false, $"Development may only send to a node on this machine, not {dxSend.Host}");
        return SpotSending.On;
    }

    public static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

    /// <summary>One warning per feature that is off.</summary>
    public static void LogAtStart(IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.GetRequiredService<IOptions<SupabaseOptions>>().Value.HasSecretKey) LogAlertsOff(logger);
        if (services.GetRequiredService<SpotSending>() is { Enabled: false } sending) LogSendingOff(logger, sending.OffReason ?? "");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerts OFF: Supabase:SecretKey is not set, no spot goes to alert_spots")]
    private static partial void LogAlertsOff(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Spot sending OFF (POST /v1/dx/spots answers 503 sending_disabled): {Reason}")]
    private static partial void LogSendingOff(ILogger logger, string reason);
}
