using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>Configuration section "Telnet": the nodes are configuration, not code.</summary>
public sealed class TelnetOptions
{
    public const string Section = "Telnet";

    /// <summary>Login callsign of the server (decided: N0CALL-87; fallback IU4VRB-1).</summary>
    [Required]
    public string Callsign { get; set; } = "N0CALL-87";

    /// <summary>Spots asked with sh/dx after login, the same as the app.</summary>
    [Range(0, 500)]
    public int Backlog { get; set; } = 50;

    /// <summary>No data for this long: the node is considered mute and the connection reopened (B04).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Some nodes wait silently for the callsign: log in after this long anyway.</summary>
    public TimeSpan LoginFallback { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>Silence after login before sh/dx, so the welcome text is over.</summary>
    public TimeSpan BacklogDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(2);

    public IList<TelnetNodeOptions> Nodes { get; } = [];
}

public sealed class TelnetNodeOptions
{
    /// <summary>Feed name ("DXFun", "IZ8MBW-6").</summary>
    [Required]
    public string Name { get; set; } = "";

    [Required]
    public string Host { get; set; } = "";

    [Range(1, 65535)]
    public int Port { get; set; }

    /// <summary>Reserve nodes stay in the list but are not connected.</summary>
    public bool Enabled { get; set; } = true;
}
