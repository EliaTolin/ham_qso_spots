using System.Net;
using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure;

namespace HamQso.Spots.Host.Tests;

/// <summary>GET /v1/spots/on-frequency on the real Host (see <see cref="TestHost"/>).</summary>
public sealed class SpotOnFrequencyApiTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Get(string query, string? token = TestHost.ValidToken) =>
        host.Client(token: token).GetAsync(new Uri("/v1/spots/on-frequency" + query, UriKind.Relative), Ct);

    private static Spot Spot(string call, double khz, string mode, int minutesAgo) => new()
    {
        Program = SpotProgram.Pota,
        Seq = 7,
        SpotKey = $"{call}_US-2907_20m",
        Callsign = call,
        FrequencyKhz = khz,
        SpotTime = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        Feeds = ["POTA"],
        Spotters = ["GM0UDL-#"],
        Band = "20m",
        Mode = mode,
        ModeSource = ModeSource.Reported,
        ModeCategory = ModeCategory.Digital,
        Reference = "US-2907",
    };

    [Theory]
    [InlineData(null)]
    [InlineData("forged.token")]
    public async Task Without_a_valid_Supabase_token_it_is_refused(string? token)
    {
        using var response = await Get("?khz=14285.0", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_a_wrong_query_is_still_a_401()
    {
        using var response = await Get("", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(TestHost.ValidToken)]
    [InlineData(TestHost.AnonymousToken)]
    [InlineData(TestHost.ReaderToken)]
    public async Task The_same_users_of_the_lists_may_ask(string token)
    {
        using var response = await Get("?khz=14285.0", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?khz=")]
    [InlineData("?khz=abc")]
    [InlineData("?khz=0")]
    [InlineData("?khz=0.0")]
    [InlineData("?khz=-14285")]
    [InlineData("?khz=NaN")]
    [InlineData("?khz=Infinity")]
    [InlineData("?khz=14285,5")]
    [InlineData("?khz=1e5")]
    [InlineData("?khz=14285&khz=7074")]
    public async Task A_missing_or_wrong_frequency_is_a_400_that_says_why(string query)
    {
        using var response = await Get(query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("khz must be a positive number", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Nothing_on_the_frequency_is_a_null_spot()
    {
        using var response = await Get("?khz=14285.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"spot":null}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_spot_found_has_the_same_shape_as_in_the_lists()
    {
        var spot = Spot("K4RCY", 14285, "USB", 1);
        host.Reader.Nearby = [spot];

        using var response = await Get("?khz=14285.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        using var page = JsonDocument.Parse(SpotJson.Serialize(new SpotPage(7, false, [spot])));
        Assert.Equal(page.RootElement.GetProperty("spots")[0].GetRawText(), answer.RootElement.GetProperty("spot").GetRawText());
        Assert.Equal("pota", answer.RootElement.GetProperty("spot").GetProperty("program").GetString());
    }

    [Fact]
    public async Task An_FT8_spot_is_found_asking_its_calling_frequency()
    {
        host.Reader.Nearby = [Spot("JA1FT8", 14075.8, "FT8", 2)];

        using var response = await Get("?khz=14074.0");

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("JA1FT8", answer.RootElement.GetProperty("spot").GetProperty("callsign").GetString());
    }

    [Fact]
    public async Task A_spot_with_QRT_in_the_comment_is_a_null_spot()
    {
        host.Reader.Nearby = [Spot("K4RCY", 14285, "USB", 1) with { Comment = "qrt off to next peak" }];

        using var response = await Get("?khz=14285.0");

        Assert.Equal("""{"spot":null}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_spot_with_a_time_more_than_5_minutes_ahead_is_a_null_spot()
    {
        host.Reader.Nearby = [Spot("K4RCY", 14285, "USB", -6)];

        using var response = await Get("?khz=14285.0");

        Assert.Equal("""{"spot":null}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task The_frequency_asked_never_reaches_the_log(string environment)
    {
        using var logged = new TestHost { Environment = environment };
        var http = logged.Client();

        using var ok = await http.GetAsync(new Uri("/v1/spots/on-frequency?khz=14285.123", UriKind.Relative), Ct);
        using var wrong = await http.GetAsync(new Uri("/v1/spots/on-frequency?khz=-14285.123", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var log = string.Join('\n', logged.Logs.Lines);
        Assert.DoesNotContain("14285", log, StringComparison.Ordinal);
        Assert.DoesNotContain("on-frequency", log, StringComparison.Ordinal);
        Assert.DoesNotContain("khz", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_archive_down_the_error_is_logged_without_the_frequency_asked()
    {
        host.Reader.NearbyFailure = new InvalidOperationException("archive down");

        using var response = await Get("?khz=14285.123");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var log = string.Join('\n', host.Logs.Lines);
        Assert.Contains("archive down", log, StringComparison.Ordinal);
        Assert.DoesNotContain("14285", log, StringComparison.Ordinal);
        Assert.DoesNotContain("khz=", log, StringComparison.Ordinal);
        Assert.DoesNotContain("on-frequency", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_web_app_may_call_it()
    {
        host.Settings["Cors:Origins:0"] = "https://app.hamqso.app";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/spots/on-frequency?khz=14285.0");
        request.Headers.Add("Origin", "https://app.hamqso.app");

        using var response = await host.Client().SendAsync(request, Ct);

        Assert.Equal("https://app.hamqso.app", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
