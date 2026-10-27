using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Host;

/// <summary>Configuration section "Server": the two ports of the Host.</summary>
public sealed class ServerOptions
{
    public const string Section = "Server";

    /// <summary>Published by Coolify: /health only.</summary>
    [Range(1, 65535)]
    public int PublicPort { get; set; } = 8080;

    /// <summary>Docker network only: /mqtt/* for Mosquitto.</summary>
    [Range(1, 65535)]
    public int InternalPort { get; set; } = 8081;
}
