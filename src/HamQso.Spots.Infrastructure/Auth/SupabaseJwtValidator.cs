using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HamQso.Spots.Infrastructure.Auth;

/// <summary>
/// Verifies a Supabase access token with the public keys (JWKS, ES256) of the
/// project that issued it, kept for <see cref="KeysLifetime"/>. Accepted
/// projects: HamQSO (<see cref="SupabaseOptions.Url"/>) and the readers
/// (<see cref="SupabaseOptions.ReaderUrls"/>, HamQRG). Issuer, audience
/// "authenticated" and expiry are checked; no shared secret is needed.
/// </summary>
public sealed class SupabaseJwtValidator : ITokenValidator, IDisposable
{
    public static readonly TimeSpan KeysLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A forged token with an unknown key must not make every connection hit
    /// Supabase: at most one forced refresh, or one retry after a failure, per minute
    /// and per project.
    /// </summary>
    public static readonly TimeSpan MinRefresh = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly JsonWebTokenHandler handler = new();
    private readonly HttpClient http;
    private readonly TimeProvider time;

    /// <summary>
    /// By issuer. A token of any other issuer is refused before any request:
    /// the keys are fetched only from the configured projects.
    /// </summary>
    private readonly Dictionary<string, ProjectKeys> projects;

    public SupabaseJwtValidator(IOptions<SupabaseOptions> options, HttpClient http, TimeProvider time)
    {
        this.http = http;
        this.time = time;
        projects = new[] { new ProjectKeys(new Uri(options.Value.Url, "auth/v1"), false) }
            .Concat(options.Value.ReaderUrls.Select(url => new ProjectKeys(new Uri(url, "auth/v1"), true)))
            .DistinctBy(p => p.Issuer)
            .ToDictionary(p => p.Issuer);
    }

    public async Task<TokenUser?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || !handler.CanReadToken(token)) return null;
        if (!projects.TryGetValue(IssuerOf(token) ?? "", out var project)) return null;
        var result = await ValidateAsync(token, project, await KeysAsync(project, false, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        // A new signing key (rotation): fetch the set again once.
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            result = await ValidateAsync(token, project, await KeysAsync(project, true, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        return result.IsValid ? new TokenUser(((JsonWebToken)result.SecurityToken).Subject ?? "", project.IsReader) : null;
    }

    public void Dispose()
    {
        foreach (var project in projects.Values) project.Refreshing.Dispose();
    }

    /// <summary>The unverified "iss" claim, only to pick the project whose keys check it.</summary>
    private string? IssuerOf(string token)
    {
        try
        {
            return handler.ReadJsonWebToken(token).Issuer;
        }
        catch (Exception e) when (e is ArgumentException or SecurityTokenException)
        {
            return null;
        }
    }

    private bool NeedsFetch(ProjectKeys project, bool force) =>
        force ? time.GetUtcNow() - project.LastForced >= MinRefresh : time.GetUtcNow() >= project.KeysUntil;

    private Task<TokenValidationResult> ValidateAsync(string token, ProjectKeys project, IList<SecurityKey> signingKeys) =>
        handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKeys = signingKeys,
            ValidIssuer = project.Issuer,
            ValidAudience = "authenticated",
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            LifetimeValidator = (notBefore, expires, _, _) => IsCurrent(notBefore, expires),
        });

    private bool IsCurrent(DateTime? notBefore, DateTime? expires)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return expires is { } e && now < e + ClockSkew && (notBefore is not { } nb || now >= nb - ClockSkew);
    }

    private async Task<IList<SecurityKey>> KeysAsync(ProjectKeys project, bool force, CancellationToken cancellationToken)
    {
        if (!NeedsFetch(project, force)) return project.Keys;
        await project.Refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!NeedsFetch(project, force)) return project.Keys;
            var now = time.GetUtcNow();
            if (force) project.LastForced = now;
            try
            {
                var json = await http.GetStringAsync(new Uri($"{project.Issuer}/.well-known/jwks.json"), cancellationToken).ConfigureAwait(false);
                project.Keys = new JsonWebKeySet(json).GetSigningKeys();
                project.KeysUntil = now + KeysLifetime;
            }
            catch (Exception e) when (e is HttpRequestException or ArgumentException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Supabase not answering: keep the keys we have and try again in a minute.
                project.KeysUntil = now + MinRefresh;
            }
            return project.Keys;
        }
        finally
        {
            project.Refreshing.Release();
        }
    }

    /// <summary>The cached public keys of one project, refreshed on their own.</summary>
    private sealed class ProjectKeys(Uri authUrl, bool isReader)
    {
        public string Issuer { get; } = authUrl.ToString();

        /// <summary>A reader project (HamQRG): its users only read.</summary>
        public bool IsReader { get; } = isReader;

        public SemaphoreSlim Refreshing { get; } = new(1, 1);

        public IList<SecurityKey> Keys { get; set; } = [];

        public DateTimeOffset KeysUntil { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset LastForced { get; set; } = DateTimeOffset.MinValue;
    }
}
