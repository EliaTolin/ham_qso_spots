using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>Configuration section "Cors": the web origins allowed to call the HTTP API.</summary>
public sealed class CorsSettings
{
    public const string Section = "Cors";

    /// <summary>
    /// Exact origins (<c>https://app.hamqso.app</c>), or with any port
    /// (<c>http://localhost:*</c>, for development). Empty: no browser origin.
    /// </summary>
    public IList<string> Origins { get; } = [];
}

/// <summary>
/// CORS for the whole HTTP API (F58 decision 16): the web app calls
/// /v1/spots, /v1/dx/spots and the proxy from its own origin. No cookies are
/// used (tokens travel in headers), so credentials are not allowed.
/// </summary>
public static class ApiCors
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "DELETE", "OPTIONS"];

    public static readonly string[] Headers = ["Authorization", "Content-Type", BearerAuth.ProxyHeader];

    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddApiCors(this IServiceCollection services)
    {
        services.AddOptions<CorsSettings>().BindConfiguration(CorsSettings.Section);
        services.AddCors();
        services.AddSingleton<IConfigureOptions<CorsOptions>>(sp => new ConfigureOptions<CorsOptions>(cors =>
        {
            var origins = sp.GetRequiredService<IOptions<CorsSettings>>().Value.Origins.ToArray();
            cors.AddDefaultPolicy(policy => policy
                .SetIsOriginAllowed(origin => IsAllowed(origin, origins))
                .WithMethods(Methods)
                .WithHeaders(Headers)
                .SetPreflightMaxAge(PreflightMaxAge));
        }));
        return services;
    }

    /// <summary>Whether <paramref name="origin"/> is one of <paramref name="allowed"/> (a pattern ending in ":*" takes any port).</summary>
    public static bool IsAllowed(string origin, IEnumerable<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        return !string.IsNullOrEmpty(origin) && allowed.Any(pattern => Matches(origin, pattern));
    }

    private static bool Matches(string origin, string pattern)
    {
        if (!pattern.EndsWith(":*", StringComparison.Ordinal)) return string.Equals(origin, pattern, StringComparison.OrdinalIgnoreCase);
        var prefix = pattern[..^2];
        if (string.Equals(origin, prefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (!origin.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)) return false;
        var port = origin[(prefix.Length + 1)..];
        return port.Length is > 0 and <= 5 && port.All(char.IsAsciiDigit);
    }
}
