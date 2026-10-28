using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Sending;

/// <summary>
/// Configuration section "DxSend": the node users spot on through the server
/// (POST /v1/dx/spots), the same node and timings as the app's telnet sender.
/// </summary>
public sealed class DxSendOptions
{
    public const string Section = "DxSend";

    /// <summary>DXFun (EA4RCH-5), as the app.</summary>
    [Required]
    public string Host { get; set; } = "dxfun.com";

    [Range(1, 65535)]
    public int Port { get; set; } = 8000;

    /// <summary>Connection, login and welcome must be over within this, or the node is unavailable.</summary>
    public TimeSpan LoginTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Some nodes wait silently for the callsign: log in after this long anyway.</summary>
    public TimeSpan LoginFallback { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>Silence after the login before the first command, so the welcome text is over.</summary>
    public TimeSpan WelcomeSilence { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The node's answer to a spot (an error, "Duplicate spot") is collected for this long.</summary>
    public TimeSpan ReplyWindow { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>A session closes this long after its last spot, as in the app.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
