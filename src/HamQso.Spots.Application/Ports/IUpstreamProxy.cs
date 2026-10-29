namespace HamQso.Spots.Application.Ports;

/// <summary>
/// Passes a request of the web app to one of the services that send no CORS
/// headers (QRZ Logbook, eQSL, SOTA, ...), only those of a fixed allowlist.
/// </summary>
public interface IUpstreamProxy
{
    /// <summary>Whether <paramref name="service"/> is an allowlist key.</summary>
    bool Knows(string service);

    Task<ProxyReply> SendAsync(ProxyCall request, CancellationToken cancellationToken);
}

/// <summary>
/// What goes to the service: <paramref name="Path"/> after the service key,
/// <paramref name="Query"/> as received ("?a=b" or empty), the forwarded headers
/// (Accept, Authorization, User-Agent), the body and its Content-Type.
/// Credentials travel in here: never log or keep any part of it but the sizes.
/// </summary>
public sealed record ProxyCall(
    string Service,
    string Method,
    string Path,
    string Query,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? Body,
    string? ContentType);

public enum ProxyOutcome
{
    /// <summary>The service answered: its status, Content-Type and body go back as they are.</summary>
    Answered,
    UnknownService,
    BadPath,
    UpstreamUnavailable,
    UpstreamTimeout,
    UpstreamTooLarge,

    /// <summary>The service redirected to another host: never followed.</summary>
    UpstreamRedirect,
}

public sealed record ProxyReply(ProxyOutcome Outcome, int Status, string? ContentType, byte[] Body)
{
    public static ProxyReply Failed(ProxyOutcome outcome) => new(outcome, 0, null, []);
}
