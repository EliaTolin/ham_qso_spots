namespace HamQso.Spots.Infrastructure.Proxy;

/// <summary>
/// The address of a proxied request, built so it can only ever be on the
/// service's own host: the path is cut into segments, each escaped, never
/// resolved against the base ("//evil.example/x" stays a path), "." and ".."
/// refused. The query goes as the client encoded it.
/// </summary>
public static class UpstreamUri
{
    /// <summary>Null when the path or query cannot be one on the service's host.</summary>
    public static Uri? Build(Uri service, string? path, string? query)
    {
        ArgumentNullException.ThrowIfNull(service);
        var segments = (path ?? "").Split('/');
        if (segments.Any(s => s is "." or "..")) return null;
        var q = query ?? "";
        if (q.Length > 0 && (q[0] != '?' || q.Contains('#', StringComparison.Ordinal) || q.Any(char.IsControl))) return null;
        var uri = new Uri($"{service.GetLeftPart(UriPartial.Authority)}/{string.Join('/', segments.Select(Uri.EscapeDataString))}{q}");
        return IsSameOrigin(uri, service) ? uri : null;
    }

    /// <summary>Same scheme, host and port: the only redirects the proxy follows.</summary>
    public static bool IsSameOrigin(Uri target, Uri service)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(service);
        return target.IsAbsoluteUri
            && string.Equals(target.Scheme, service.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Host, service.Host, StringComparison.OrdinalIgnoreCase)
            && target.Port == service.Port;
    }
}
