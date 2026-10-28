using System.Net;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Profiles;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Profiles;

/// <summary>PostgREST answers as Supabase gives them, from a fake handler.</summary>
public sealed class SupabaseProfileReaderTests : IDisposable
{
    private const string User = "4b1e2a9c-0d3f-4e5a-8b6c-7d8e9f0a1b2c";

    private readonly FakePostgrest postgrest = new();
    private readonly HttpClient http;
    private readonly SupabaseProfileReader reader;

    public SupabaseProfileReaderTests()
    {
        http = new HttpClient(postgrest);
        reader = new SupabaseProfileReader(
            Options.Create(new SupabaseOptions { Url = new Uri("https://example.supabase.co"), SecretKey = "sb_secret_test" }),
            http);
    }

    public void Dispose() => http.Dispose();

    private Task<string?> Read(string user = User) => reader.CallsignAsync(user, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Reads_the_callsign_of_the_user_profile_with_the_secret_key()
    {
        postgrest.Body = """[{"callsign":"IU4VRB"}]""";

        Assert.Equal("IU4VRB", await Read());
        Assert.Equal($"https://example.supabase.co/rest/v1/profiles?id=eq.{User}&select=callsign", postgrest.Url);
        Assert.Equal("sb_secret_test", postgrest.ApiKey);
    }

    [Theory]
    [InlineData("""[{"callsign":null}]""")]
    [InlineData("""[]""")]
    public async Task A_profile_without_callsign_or_no_profile_is_null(string body)
    {
        postgrest.Body = body;

        Assert.Null(await Read());
    }

    [Fact]
    public async Task A_user_id_that_is_not_a_UUID_never_reaches_the_query()
    {
        Assert.Null(await Read("x&or=(id.neq.0)"));
        Assert.Null(postgrest.Url);
    }

    [Fact]
    public async Task A_Supabase_error_is_an_error_not_a_missing_callsign()
    {
        postgrest.Status = HttpStatusCode.Unauthorized;

        await Assert.ThrowsAsync<HttpRequestException>(() => Read());
    }

    private sealed class FakePostgrest : HttpMessageHandler
    {
        public string Body { get; set; } = "[]";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? Url { get; private set; }

        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            ApiKey = request.Headers.GetValues("apikey").Single();
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
}
