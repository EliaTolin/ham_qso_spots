using System.Net;
using System.Text.Json;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Catalog;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Catalog;

/// <summary>F53.3: every active reference, read from PostgREST page after page.</summary>
public sealed class SupabaseReferenceCatalogSourceTests : IDisposable
{
    private readonly FakePostgrest postgrest = new();
    private readonly HttpClient http;
    private readonly SupabaseReferenceCatalogSource source;

    public SupabaseReferenceCatalogSourceTests()
    {
        http = new HttpClient(postgrest);
        source = new SupabaseReferenceCatalogSource(
            Options.Create(new SupabaseOptions { Url = new Uri("https://example.supabase.co"), SecretKey = "sb_secret_test" }),
            http) { PageSize = 2 };
    }

    public void Dispose() => http.Dispose();

    [Fact]
    public async Task Reads_every_page_after_the_last_code_with_the_secret_key()
    {
        postgrest.Rows = ["I/ER-001", "IT-0001", "IT-1271", "K-0001", "VK-0001"];

        var all = await source.ReadActiveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["I/ER-001", "IT-0001", "IT-1271", "K-0001", "VK-0001"], all.Select(r => r.Ref));
        Assert.Equal(3, postgrest.Urls.Count);
        Assert.Contains("active=is.true", postgrest.Urls[0], StringComparison.Ordinal);
        Assert.Contains("order=ref.asc", postgrest.Urls[0], StringComparison.Ordinal);
        Assert.Contains("ref=gt.IT-0001", postgrest.Urls[1], StringComparison.Ordinal);
        Assert.All(postgrest.ApiKeys, k => Assert.Equal("sb_secret_test", k));
    }

    [Fact]
    public async Task Fields_come_with_their_types_and_empty_ones_are_null()
    {
        postgrest.Rows = ["I/ER-001"];

        var r = (await source.ReadActiveAsync(TestContext.Current.CancellationToken)).Single();

        Assert.Equal(("SOTA", "Name I/ER-001", 248, 44.19333, 10.70111, 2165, 10, (string?)null),
            (r.Program, r.Name, r.Dxcc, r.Latitude, r.Longitude, r.AltitudeM, r.Points, r.Location));
    }

    [Fact]
    public async Task A_Supabase_error_fails_the_read()
    {
        postgrest.Status = HttpStatusCode.ServiceUnavailable;

        await Assert.ThrowsAsync<HttpRequestException>(() => source.ReadActiveAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FakePostgrest : HttpMessageHandler
    {
        public IReadOnlyList<string> Rows { get; set; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<string> Urls { get; } = [];

        public List<string> ApiKeys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            Urls.Add(url);
            ApiKeys.Add(request.Headers.GetValues("apikey").Single());
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
            var after = url.Split('&').FirstOrDefault(p => p.StartsWith("ref=gt.", StringComparison.Ordinal))?["ref=gt.".Length..];
            var limit = int.Parse(url.Split('&').First(p => p.StartsWith("limit=", StringComparison.Ordinal))["limit=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            var page = Rows.Where(r => after is null || string.CompareOrdinal(r, after) > 0).Take(limit).Select(r => new
            {
                @ref = r,
                program = "SOTA",
                name = "Name " + r,
                location = (string?)null,
                dxcc = 248,
                grid = "JN54MF",
                lat = 44.19333,
                lon = 10.70111,
                altitude_m = 2165,
                points = 10,
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(page)) });
        }
    }
}
