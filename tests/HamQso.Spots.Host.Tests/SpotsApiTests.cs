using System.Net;
using System.Net.Http.Headers;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HamQso.Spots.Host.Tests;

/// <summary>GET /v1/spots on the real Host, with the archive and the token check faked; no source runs.</summary>
public sealed class SpotsApiTests : IDisposable
{
    private const string ValidToken = "valid.supabase.token";

    private readonly FakeReader reader = new();
    private readonly WebApplicationFactory<Program> factory;

    public SpotsApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Spots", "Host=localhost;Database=none");
            b.UseSetting("Mqtt:Password", "test");
            b.UseSetting("Supabase:SecretKey", "test");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IHostedService>();
                s.AddSingleton<ISpotReader>(reader);
                s.AddSingleton<ITokenValidator>(new FakeTokens());
            });
        });
    }

    public void Dispose() => factory.Dispose();

    private async Task<HttpResponseMessage> Get(string query, string? token = ValidToken)
    {
        var http = factory.CreateClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.GetAsync(new Uri("/v1/spots" + query, UriKind.Relative), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_HamQSO_user_gets_the_starting_list_with_its_seq()
    {
        using var response = await Get("?program=dx&band=20m");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = SpotJson.Deserialize(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((99L, true, "JA1ABC"), (page.Seq, page.Full, page.Spots[0].Callsign));
        Assert.Equal(["20m"], reader.LastFilter!.Bands);
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
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ValidToken);
        http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br");

        using var response = await http.GetAsync(new Uri("/v1/spots?program=dx", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    private sealed class FakeTokens : ITokenValidator
    {
        public Task<bool> IsValidAsync(string token, CancellationToken cancellationToken) => Task.FromResult(token == ValidToken);
    }

    private sealed class FakeReader : ISpotReader
    {
        public SpotFilter? LastFilter { get; private set; }

        private static readonly Spot Spot = new()
        {
            Program = SpotProgram.Dx,
            SpotKey = "k",
            Callsign = "JA1ABC",
            FrequencyKhz = 14025,
            SpotTime = DateTimeOffset.UnixEpoch,
            Feeds = ["DXFun"],
            Seq = 99,
        };

        public Task<SpotPage> LatestAsync(SpotFilter filter, int limit, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult(new SpotPage(99, false, [Spot]));
        }

        public Task<SpotPage> ChangedSinceAsync(SpotFilter filter, long sinceSeq, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new SpotPage(99, false, []));
    }
}
