using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Host.Tests;

/// <summary>GET /v1/rbn/skimmers (plan F71, 3.1) on the real Host, activity and node list faked.</summary>
public sealed class RbnSkimmersApiTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(int h, int m, int s = 0) => new(2026, 11, 6, h, m, s, TimeSpan.Zero);

    private Task<HttpResponseMessage> Get(string query, string? token = TestHost.ValidToken) =>
        host.Client(token: token).GetAsync(new Uri($"/v1/rbn/skimmers{query}", UriKind.Relative), Ct);

    private static async Task<string?> Error(HttpResponseMessage r) =>
        JsonElement.Parse(await r.Content.ReadAsStringAsync(Ct)).GetProperty("error").GetString();

    [Fact]
    public async Task The_answer_of_a_test_window_is_the_shared_fixture()
    {
        host.RbnNodes.Set([new RbnNode("OK4YL", "JN79FX", ["20m"], true, null)], At(14, 5));
        host.RbnActivity.Rows.AddRange(
        [
            new ActivityMinute("OK4YL", "20m", At(14, 30), 12, At(14, 30, 50)),
            new ActivityMinute("OK4YL", "20m", At(14, 31), 30, At(14, 31, 40)),
            new ActivityMinute("Z61DX", "20m", At(14, 29), 3, At(14, 29, 2)),
            new ActivityMinute("OK4YL", "40m", At(14, 31), 7, At(14, 31, 41)),
        ]);
        // The node list read had 350 nodes in the fixture.
        host.RbnNodes.Set([new RbnNode("OK4YL", "JN79FX", ["20m"], true, null), .. Enumerable.Range(0, 349).Select(i => new RbnNode($"X{i}X", null, [], false, null))], At(14, 5));

        using var response = await Get("?band=20m&from=2026-11-06T14:32:05Z&to=2026-11-06T14:33:17Z", TestHost.AnonymousToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rbn_skimmers_v1.json")));
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(JsonNode.DeepEquals(fixture, answer), answer!.ToJsonString());
    }

    [Fact]
    public async Task Without_a_window_the_last_10_minutes()
    {
        host.RbnActivity.Rows.Add(new ActivityMinute("OK4YL", "20m", At(14, 35), 1, At(14, 35, 1)));
        host.RbnActivity.Rows.Add(new ActivityMinute("Z61DX", "20m", At(14, 20), 1, At(14, 20, 1)));

        using var response = await Get("?band=20m");
        var answer = JsonElement.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(("2026-11-06T14:30:00Z", "2026-11-06T14:40:00Z"), (answer.GetProperty("from").GetString(), answer.GetProperty("to").GetString()));
        Assert.Equal(["OK4YL"], answer.GetProperty("skimmers").EnumerateArray().Select(s => s.GetProperty("call").GetString()));
    }

    [Fact]
    public async Task Before_any_node_list_and_with_the_feed_down_the_answer_says_so()
    {
        host.Rbn.SetStatus(new RbnFeedStatus(RbnFeedState.Down, null));

        using var response = await Get("?band=40m");
        var answer = JsonElement.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal("""{"state":"down","since":null}""", answer.GetProperty("feed").GetRawText());
        Assert.Equal("""{"fetched_at":null,"count":0,"stale":true}""", answer.GetProperty("nodes").GetRawText());
        Assert.Empty(answer.GetProperty("skimmers").EnumerateArray());
    }

    [Theory]
    [InlineData("", "invalid_band")]
    [InlineData("?band=11m", "invalid_band")]
    [InlineData("?band=20m&from=2026-11-06T14:32:05Z", "invalid_window")]
    [InlineData("?band=20m&from=2026-11-06T14:33:17Z&to=2026-11-06T14:32:05Z", "invalid_window")]
    [InlineData("?band=20m&from=2026-11-04T10:00:00Z&to=2026-11-04T10:05:00Z", "invalid_window")]
    [InlineData("?band=20m&from=yesterday&to=2026-11-06T14:32:05Z", "invalid_window")]
    public async Task A_wrong_band_or_window_is_a_400(string query, string error)
    {
        using var response = await Get(query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await Error(response));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("forged", HttpStatusCode.Unauthorized)]
    [InlineData(TestHost.ReaderToken, HttpStatusCode.Forbidden)]
    [InlineData(TestHost.AnonymousToken, HttpStatusCode.OK)]
    [InlineData(TestHost.ValidToken, HttpStatusCode.OK)]
    public async Task Every_HamQSO_user_anonymous_included_may_ask(string? token, HttpStatusCode status)
    {
        using var response = await Get("?band=20m", token);

        Assert.Equal(status, response.StatusCode);
    }
}
