using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>Configuration section "Rbn": the Reverse Beacon Network feed of the coverage test (GET /v1/rbn), the node list and the skimmer activity (F71).</summary>
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

    /// <summary>
    /// F71: the connection stays open for good, every spot counted as skimmer
    /// activity, and the node list is read. Off: open only while someone listens
    /// (a local run that should not reach the real RBN).
    /// </summary>
    public bool AlwaysOn { get; set; } = true;

    /// <summary>The RBN node list page (the HTML behind reversebeacon.net/nodes).</summary>
    [Required]
    public Uri NodesUrl { get; set; } = new("https://www.reversebeacon.net/cont_includes/status.php?t=skt");

    /// <summary>How often the node list is read.</summary>
    public TimeSpan NodesInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often the counted activity is written to Postgres.</summary>
    public TimeSpan ActivityFlush { get; set; } = TimeSpan.FromSeconds(30);
}
