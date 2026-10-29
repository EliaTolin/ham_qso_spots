using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Proxy;

/// <summary>Configuration section "Proxy": the limits of the CORS proxy.</summary>
public sealed class ProxyOptions
{
    public const string Section = "Proxy";

    /// <summary>A service that takes longer is a 504.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Request and response bodies; an ADIF upload of a big log fits.</summary>
    [Range(1, 100 * 1024 * 1024)]
    public int MaxBodyBytes { get; set; } = 10 * 1024 * 1024;
}
