using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Host;

/// <summary>The user of the Supabase access token sent as "Bearer &lt;token&gt;", for the REST endpoints.</summary>
public static class BearerAuth
{
    private const string Bearer = "Bearer ";

    /// <summary>The proxy's own header: there "Authorization" belongs to the service behind it.</summary>
    public const string ProxyHeader = "X-HamQSO-Auth";

    /// <summary>Null without the header or with a token that is not valid.</summary>
    public static Task<TokenUser?> UserAsync(HttpContext http, ITokenValidator tokens, CancellationToken cancellationToken) =>
        UserAsync(http, "Authorization", tokens, cancellationToken);

    /// <summary>
    /// The token in "Authorization: Bearer", or else in the query parameter
    /// <see cref="QueryParameter"/> (EventSource cannot set headers). The query
    /// is never logged (Microsoft.AspNetCore stays at Warning, see operations.md).
    /// </summary>
    public static async Task<TokenUser?> UserFromHeaderOrQueryAsync(HttpContext http, ITokenValidator tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokens);
        if (http.Request.Headers.Authorization.Count > 0) return await UserAsync(http, tokens, cancellationToken).ConfigureAwait(false);
        var token = http.Request.Query[QueryParameter].ToString();
        return token.Length > 0 ? await tokens.ValidateAsync(token, cancellationToken).ConfigureAwait(false) : null;
    }

    public const string QueryParameter = "access_token";

    /// <summary>The same, with "Bearer &lt;token&gt;" in <paramref name="header"/>.</summary>
    public static async Task<TokenUser?> UserAsync(HttpContext http, string header, ITokenValidator tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokens);
        var authorization = http.Request.Headers[header].ToString();
        return authorization.StartsWith(Bearer, StringComparison.Ordinal)
            ? await tokens.ValidateAsync(authorization[Bearer.Length..], cancellationToken).ConfigureAwait(false)
            : null;
    }
}
