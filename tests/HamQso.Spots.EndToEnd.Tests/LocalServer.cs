using HamQso.Spots.Infrastructure.Proxy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>
/// The server run in the test process with the production Supabase project
/// (real tokens, real profiles) and everything outward on loopback: the DX
/// node (<see cref="DxNodePort"/>), the RBN (<see cref="RbnPort"/>) and the
/// proxied services (<see cref="Upstreams"/>).
/// No sources, archive or broker. The deployed server would reach the real
/// cluster and the real services; these tests never do.
/// </summary>
public sealed class LocalServer : WebApplicationFactory<Program>
{
    public int DxNodePort { get; init; } = 1;

    public int RbnPort { get; init; } = 1;

    public Dictionary<string, Uri> Upstreams { get; } = new(StringComparer.Ordinal);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Spots", "Host=localhost;Database=none");
        builder.UseSetting("Supabase:Url", E2eSettings.Supabase.ToString());
        builder.UseSetting("Supabase:SecretKey", E2eSettings.SecretKey);
        builder.UseSetting("DxSend:Host", "127.0.0.1");
        builder.UseSetting("DxSend:Port", DxNodePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Rbn:Host", "127.0.0.1");
        builder.UseSetting("Rbn:Port", RbnPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IHostedService>();
            s.AddSingleton(new ProxyAllowlist(Upstreams));
        });
    }
}
