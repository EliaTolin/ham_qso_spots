using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Host;

/// <summary>The user of the Supabase access token in "Authorization: Bearer", for the REST endpoints.</summary>
public static class BearerAuth
{
    private const string Bearer = "Bearer ";

    /// <summary>Null without the header or with a token that is not valid.</summary>
    public static async Task<TokenUser?> UserAsync(HttpContext http, ITokenValidator tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokens);
        var authorization = http.Request.Headers.Authorization.ToString();
        return authorization.StartsWith(Bearer, StringComparison.Ordinal)
            ? await tokens.ValidateAsync(authorization[Bearer.Length..], cancellationToken).ConfigureAwait(false)
            : null;
    }
}
