using System.Net;
using HamQso.Spots.Application.Catalog;
using HamQso.Spots.Domain.References;
using Microsoft.Extensions.DependencyInjection;

namespace HamQso.Spots.Host.Tests;

/// <summary>
/// F53.3: GET /v1/references/catalog on the real Host. The apps download the
/// world catalog once every three months and ask again with its ETag: an
/// unchanged catalog is a 304 with no body.
/// </summary>
public sealed class ReferenceCatalogApiTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    private ReferenceCatalogFile Serve()
    {
        var file = ReferenceCatalogFormat.Write(
            [new CatalogReference("IT-1271", "POTA", "Torbiere del Lavaze'", null, 248, "JN56NH", 46.35, 11.49, null, null)],
            new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero));
        host.Services.GetRequiredService<CurrentReferenceCatalog>().File = file;
        return file;
    }

    private Task<HttpResponseMessage> Get(string? token = TestHost.ValidToken, string? etag = null)
    {
        var http = host.Client(token: token);
        if (etag is not null) http.DefaultRequestHeaders.IfNoneMatch.ParseAdd(etag);
        return http.GetAsync(new Uri("/v1/references/catalog", UriKind.Relative), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_HamQSO_user_gets_the_gzip_file_with_its_ETag_size_and_count()
    {
        var file = Serve();

        using var response = await Get();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/gzip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(file.Gzip.Length, response.Content.Headers.ContentLength);
        Assert.Equal(file.ETag, response.Headers.ETag?.Tag);
        Assert.Equal("1", response.Headers.GetValues("X-Reference-Count").Single());
        Assert.Equal(file.Gzip, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Null(response.Content.Headers.ContentEncoding.FirstOrDefault());
    }

    [Fact]
    public async Task The_same_catalog_asked_again_with_its_ETag_is_a_304_without_body()
    {
        var file = Serve();

        using var response = await Get(etag: file.ETag);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_old_ETag_gets_the_new_file()
    {
        Serve();

        using var response = await Get(etag: "\"old\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forged.token")]
    public async Task Without_a_valid_Supabase_token_it_is_refused(string? token)
    {
        Serve();

        using var response = await Get(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_users_get_it_too_they_log_offline_as_well()
    {
        Serve();

        using var response = await Get(TestHost.AnonymousToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Before_the_first_file_exists_it_is_a_503_to_try_later()
    {
        using var response = await Get();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }
}
