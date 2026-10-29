using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>Configuration section "Rbn": the Reverse Beacon Network feed of the coverage test (GET /v1/rbn).</summary>
public sealed class RbnOptions
{
    public const string Section = "Rbn";

    [Required]
    public string Host { get; set; } = "telnet.reversebeacon.net";

    [Range(1, 65535)]
    public int Port { get; set; } = 7000;

    /// <summary>The server's login, as the cluster sources (the users' calls are never used here).</summary>
    [Required]
    public string Callsign { get; set; } = "N0CALL-87";

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Log in after this long even without the "enter your call" prompt.</summary>
    public TimeSpan LoginFallback { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>The RBN sends several lines a second: this long without data and the connection is reopened.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The connection stays open this long after the last listener leaves.</summary>
    public TimeSpan Linger { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>A stream of the coverage test ends after this long; the client opens a new one if needed.</summary>
    public TimeSpan MaxStreamDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>An SSE comment this often keeps proxies from closing a quiet stream.</summary>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(15);
}
