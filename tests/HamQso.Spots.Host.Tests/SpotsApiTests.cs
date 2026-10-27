using System.Net;
using HamQso.Spots.Infrastructure;

namespace HamQso.Spots.Host.Tests;

/// <summary>GET /v1/spots on the real Host (see <see cref="TestHost"/>).</summary>
public sealed class SpotsApiTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    private Task<HttpResponseMessage> Get(string query, string? token = TestHost.ValidToken) =>
        host.Client(token: token).GetAsync(new Uri("/v1/spots" + query, UriKind.Relative), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_HamQSO_user_gets_the_starting_list_with_its_seq()
    {
        using var response = await Get("?program=dx&band=20m");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = SpotJson.Deserialize(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((99L, true, "JA1ABC"), (page.Seq, page.Full, page.Spots[0].Callsign));
        Assert.Equal(["20m"], host.Reader.LastFilter!.Bands);
    }

    [Fact]
    public async Task With_since_seq_only_the_changes_come_back()
    {
        using var response = await Get("?program=pota&since_seq=50");

        var page = SpotJson.Deserialize(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((99L, false, 0), (page.Seq, page.Full, page.Spots.Count));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forged.token")]
    public async Task Without_a_valid_Supabase_token_the_list_is_refused(string? token)
    {
        using var response = await Get("?program=dx", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_query_is_a_400_that_says_why()
    {
        using var response = await Get("?program=dx&band=11m");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("band", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_answer_is_not_compressed()
    {
        var http = host.Client();
        http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br");

        using var response = await http.GetAsync(new Uri("/v1/spots?program=dx", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
