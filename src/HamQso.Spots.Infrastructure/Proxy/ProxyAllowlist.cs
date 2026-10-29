namespace HamQso.Spots.Infrastructure.Proxy;

/// <summary>
/// The only places the proxy goes: a fixed key per service, in code, not in
/// configuration (F58 decision 16). Tests build their own list pointing to a
/// fake service on loopback.
/// </summary>
public sealed class ProxyAllowlist(IReadOnlyDictionary<string, Uri> services)
{
    /// <summary>The services of the HamQSO app that send no CORS headers.</summary>
    public static ProxyAllowlist Production { get; } = new(new Dictionary<string, Uri>(StringComparer.Ordinal)
    {
        ["qrz-logbook"] = new("https://logbook.qrz.com"),
        ["hrdlog"] = new("https://robot.hrdlog.net"),
        ["eqsl"] = new("https://www.eqsl.cc"),
        ["sotawatch"] = new("https://sotawatch.sota.org.uk"),
        ["sota-sso"] = new("https://sso.sota.org.uk"),
        ["sotadata"] = new("https://www.sotadata.org.uk"),
        ["hamqsl"] = new("https://www.hamqsl.com"),
        ["country-files"] = new("https://www.country-files.com"),
    });

    public IReadOnlyCollection<string> Keys => [.. services.Keys];

    public Uri? BaseOf(string service) => services.GetValueOrDefault(service);
}
