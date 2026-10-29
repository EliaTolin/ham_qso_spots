using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace HamQso.Spots.Host;

/// <summary>Configuration section "ForwardedHeaders": who may tell the client address.</summary>
public sealed class ForwardedHeadersSettings
{
    public const string Section = "ForwardedHeaders";

    /// <summary>
    /// Networks whose X-Forwarded-For is believed: the reverse proxy (Traefik on
    /// Coolify, in the Docker network). The server's ports are only exposed in
    /// that network, so by default the private ranges.
    /// </summary>
    public IList<string> TrustedNetworks { get; } = [];

    public static readonly string[] DefaultTrustedNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
}

/// <summary>
/// The client address of a request (for the proxy's per-address limit):
/// the connection's, or the last X-Forwarded-For entry when the connection comes
/// from a trusted proxy. Only that last hop is taken, the one the proxy wrote:
/// the entries before it are the client's word. Scheme and host are not changed.
/// </summary>
public static class ClientAddress
{
    public static IServiceCollection AddClientAddress(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration.GetSection($"{ForwardedHeadersSettings.Section}:TrustedNetworks").Get<string[]>();
        var networks = configured is { Length: > 0 } ? configured : ForwardedHeadersSettings.DefaultTrustedNetworks;
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in networks) options.KnownIPNetworks.Add(IPNetwork.Parse(network));
        });
        return services;
    }
}
