using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HamQso.Spots.Infrastructure.Tests.Auth;

/// <summary>Tokens signed in the test with an ES256 key, published by a fake JWKS endpoint.</summary>
public sealed class SupabaseJwtValidatorTests : IDisposable
{
    private const string Issuer = "https://example.supabase.co/auth/v1";

    /// <summary>A reader project (HamQRG): its users may read the spots too.</summary>
    private const string ReaderIssuer = "https://reader.supabase.co/auth/v1";

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly ECDsa projectKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa readerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeTimeProvider time = new(Now);
    private readonly JwksHandler jwks;
    private readonly HttpClient http;
    private readonly SupabaseJwtValidator validator;

    public SupabaseJwtValidatorTests()
    {
        jwks = new JwksHandler(Jwks(projectKey, "key-1"), Jwks(readerKey, "reader-1"));
        http = new HttpClient(jwks);
        validator = new SupabaseJwtValidator(
            Options.Create(new SupabaseOptions
            {
                Url = new Uri("https://example.supabase.co"),
                ReaderUrls = [new Uri("https://reader.supabase.co")],
            }),
            http,
            time);
    }

    public void Dispose()
    {
        validator.Dispose();
        http.Dispose();
        projectKey.Dispose();
        otherKey.Dispose();
        readerKey.Dispose();
    }

    private Task<bool> Check(string token) => validator.IsValidAsync(token, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_token_of_a_logged_in_HamQSO_user_is_accepted() =>
        Assert.True(await Check(Token(projectKey, "key-1")));

    [Fact]
    public async Task An_expired_token_is_refused() =>
        Assert.False(await Check(Token(projectKey, "key-1", expires: Now.AddMinutes(-5))));

    [Fact]
    public async Task A_token_signed_by_someone_else_is_refused() =>
        Assert.False(await Check(Token(otherKey, "key-1")));

    [Fact]
    public async Task A_token_of_another_project_is_refused() =>
        Assert.False(await Check(Token(projectKey, "key-1", issuer: "https://other.supabase.co/auth/v1")));

    [Fact]
    public async Task A_token_of_a_reader_project_is_accepted_with_its_own_keys() =>
        Assert.True(await Check(Token(readerKey, "reader-1", issuer: ReaderIssuer)));

    [Fact]
    public async Task A_reader_token_signed_with_the_HamQSO_key_is_refused() =>
        Assert.False(await Check(Token(projectKey, "key-1", issuer: ReaderIssuer)));

    [Fact]
    public async Task A_HamQSO_token_signed_with_the_reader_key_is_refused() =>
        Assert.False(await Check(Token(readerKey, "reader-1")));

    [Fact]
    public async Task A_token_of_an_unknown_project_fetches_no_keys()
    {
        Assert.False(await Check(Token(otherKey, "key-1", issuer: "https://attacker.example/auth/v1")));

        Assert.Equal(0, jwks.Requests);
    }

    [Fact]
    public async Task Forged_tokens_on_one_project_do_not_hold_back_the_rotation_of_the_other()
    {
        Assert.True(await Check(Token(projectKey, "key-1")));
        for (var i = 0; i < 10; i++) Assert.False(await Check(Token(otherKey, $"forged-{i}")));
        jwks.ReaderBody = Jwks(otherKey, "reader-2");

        Assert.True(await Check(Token(otherKey, "reader-2", issuer: ReaderIssuer)));
    }

    [Fact]
    public async Task An_anonymous_key_token_is_refused() =>
        Assert.False(await Check(Token(projectKey, "key-1", audience: "anon")));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("sb_publishable_abc")]
    public async Task Garbage_is_refused(string token) => Assert.False(await Check(token));

    [Fact]
    public async Task The_public_keys_are_fetched_once_for_many_connections()
    {
        for (var i = 0; i < 20; i++) Assert.True(await Check(Token(projectKey, "key-1")));

        Assert.Equal(1, jwks.Requests);
    }

    [Fact]
    public async Task After_a_key_rotation_the_new_key_is_fetched_at_once()
    {
        Assert.True(await Check(Token(projectKey, "key-1")));
        jwks.Body = Jwks(otherKey, "key-2");

        Assert.True(await Check(Token(otherKey, "key-2")));
        Assert.Equal(2, jwks.Requests);
    }

    [Fact]
    public async Task Forged_tokens_with_unknown_keys_fetch_the_keys_at_most_once_a_minute()
    {
        Assert.True(await Check(Token(projectKey, "key-1")));

        for (var i = 0; i < 50; i++) Assert.False(await Check(Token(otherKey, $"forged-{i}")));

        Assert.Equal(2, jwks.Requests);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await Check(Token(otherKey, "forged-late")));
        Assert.Equal(3, jwks.Requests);
    }

    [Fact]
    public async Task When_Supabase_does_not_answer_the_token_is_refused_without_an_error()
    {
        jwks.Fail = true;

        Assert.False(await Check(Token(projectKey, "key-1")));
    }

    private static string Token(ECDsa key, string kid, DateTimeOffset? expires = null, string issuer = Issuer, string audience = "authenticated") =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = Now.AddMinutes(-1).UtcDateTime,
            NotBefore = Now.AddMinutes(-1).UtcDateTime,
            Expires = (expires ?? Now.AddHours(1)).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "8f2c-user-id", ["role"] = "authenticated" },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256),
        });

    private static string Jwks(ECDsa key, string kid)
    {
        var p = key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "EC", crv = "P-256", alg = "ES256", use = "sig", kid, x = Base64UrlEncoder.Encode(p.Q.X), y = Base64UrlEncoder.Encode(p.Q.Y) },
            },
        });
    }

    private sealed class JwksHandler(string body, string readerBody) : HttpMessageHandler
    {
        public string Body { get; set; } = body;

        public string ReaderBody { get; set; } = readerBody;

        public int Requests { get; private set; }

        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Assert.Contains(url, new[] { $"{Issuer}/.well-known/jwks.json", $"{ReaderIssuer}/.well-known/jwks.json" });
            Requests++;
            return Task.FromResult(Fail
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(url.StartsWith(Issuer, StringComparison.Ordinal) ? Body : ReaderBody) });
        }
    }
}
