using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HamQso.Spots.Infrastructure.Auth;

/// <summary>
/// Verifies a Supabase access token with the project's public keys (JWKS,
/// ES256), kept for <see cref="KeysLifetime"/>. Issuer, audience
/// "authenticated" and expiry are checked; no shared secret is needed.
/// </summary>
public sealed class SupabaseJwtValidator(IOptions<SupabaseOptions> options, HttpClient http, TimeProvider time) : ITokenValidator, IDisposable
{
    public static readonly TimeSpan KeysLifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly JsonWebTokenHandler handler = new();
    private readonly SemaphoreSlim refreshing = new(1, 1);
    private IList<SecurityKey> keys = [];
    private DateTimeOffset keysUntil = DateTimeOffset.MinValue;

    private Uri AuthUrl => new(options.Value.Url, "auth/v1");

    public async Task<bool> IsValidAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || !handler.CanReadToken(token)) return false;
        var result = await ValidateAsync(token, await KeysAsync(false, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        // A new signing key (rotation): fetch the set again once.
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            result = await ValidateAsync(token, await KeysAsync(true, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        return result.IsValid;
    }

    public void Dispose() => refreshing.Dispose();

    private Task<TokenValidationResult> ValidateAsync(string token, IList<SecurityKey> signingKeys) =>
        handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKeys = signingKeys,
            ValidIssuer = AuthUrl.ToString(),
            ValidAudience = "authenticated",
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            LifetimeValidator = (notBefore, expires, _, _) => IsCurrent(notBefore, expires),
        });

    private bool IsCurrent(DateTime? notBefore, DateTime? expires)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return expires is { } e && now < e + ClockSkew && (notBefore is not { } nb || now >= nb - ClockSkew);
    }

    private async Task<IList<SecurityKey>> KeysAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && time.GetUtcNow() < keysUntil) return keys;
        await refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && time.GetUtcNow() < keysUntil) return keys;
            var json = await http.GetStringAsync(new Uri($"{AuthUrl}/.well-known/jwks.json"), cancellationToken).ConfigureAwait(false);
            keys = new JsonWebKeySet(json).GetSigningKeys();
            keysUntil = time.GetUtcNow() + KeysLifetime;
            return keys;
        }
        finally
        {
            refreshing.Release();
        }
    }
}
