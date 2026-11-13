using HamQso.Spots.Application.Health;
using System.Net;
using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Domain.Protection;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Protection;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Tests.Protection;

public class SupabaseProtectionTests
{
    private static readonly SupabaseOptions Supabase = new() { Url = new Uri("https://example.supabase.co"), SecretKey = "sb_secret_test" };

    private static ProtectionBatch Batch() => new(
        [new ProtectedUse("IN3FOB/P", ProtectionSignal.Skimmer, new DateTimeOffset(2026, 11, 13, 3, 14, 0, TimeSpan.Zero), "40m", 7020, "CW", "DF7GB-#", "JO40",
            new Dictionary<string, object?> { ["reports"] = 3 }, "skimmer:IN3FOB:40m:1")],
        ["spot", "skimmer"],
        [new SourceFailure("pskreporter", "down")]);

    private static SupabaseProtectionUplink Uplink(Func<HttpRequestMessage, HttpResponseMessage> answer, List<(string Url, string? Key, string Body)>? seen = null) =>
        new(Options.Create(Supabase), Options.Create(new ProtectionOptions()), new HttpClient(new StubHandler(answer, seen)));

    [Fact]
    public async Task The_batch_goes_to_callsign_protection_with_the_secret_key_and_the_contract_body()
    {
        var seen = new List<(string, string?, string)>();
        await Uplink(_ => new HttpResponseMessage(HttpStatusCode.OK), seen).SendAsync(Batch(), TestContext.Current.CancellationToken);

        var (url, key, body) = Assert.Single(seen);
        Assert.Equal("https://example.supabase.co/functions/v1/callsign_protection", url);
        Assert.Equal("sb_secret_test", key);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("skimmer:IN3FOB:40m:1", json.RootElement.GetProperty("uses")[0].GetProperty("source_id").GetString());
        Assert.Equal("pskreporter", json.RootElement.GetProperty("failed")[0].GetProperty("source").GetString());
        Assert.Equal(["spot", "skimmer"], json.RootElement.GetProperty("checked").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    public async Task A_4xx_is_final(HttpStatusCode status)
    {
        var e = await Assert.ThrowsAsync<UplinkRejectedException>(() => Uplink(_ => new HttpResponseMessage(status)).SendAsync(Batch(), TestContext.Current.CancellationToken));
        Assert.Equal((int)status, e.Status);
    }

    [Fact]
    public async Task A_5xx_and_a_network_error_are_worth_a_retry()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Uplink(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)).SendAsync(Batch(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => Uplink(_ => throw new HttpRequestException("no network")).SendAsync(Batch(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task F107_review_5_the_watchlist_is_the_backend_rpc_with_the_secret_key_not_every_profile()
    {
        var seen = new List<(string Url, string? Key, string Body)>();
        var source = new SupabaseProtectedCallsigns(Options.Create(Supabase), new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"callsign":"IN3FOB/P","base":"IN3FOB"},{"callsign":"DL1ABC","base":"DL1ABC"},{"callsign":null,"base":null}]""") }, seen)));

        var all = await source.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["IN3FOB/P", "DL1ABC"], all);
        var (url, key, body) = Assert.Single(seen);
        Assert.Equal("https://example.supabase.co/rest/v1/rpc/callsign_protection_watchlist?order=callsign&limit=1000&offset=0", url);
        Assert.Equal("sb_secret_test", key);
        Assert.Equal("{}", body);
    }

    [Fact]
    public async Task F107_review_5_the_watchlist_is_read_in_pages_of_1000_because_postgrest_cuts_at_max_rows()
    {
        var seen = new List<(string Url, string? Key, string Body)>();
        var calls = 0;
        var source = new SupabaseProtectedCallsigns(Options.Create(Supabase), new HttpClient(new StubHandler(_ =>
        {
            var rows = calls++ == 0
                ? Enumerable.Range(0, SupabaseProtectedCallsigns.PageSize).Select(i => $"{{\"callsign\":\"IN{i}FOB\",\"base\":\"IN{i}FOB\"}}")
                : ["{\"callsign\":\"DL1ABC\",\"base\":\"DL1ABC\"}"];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"[{string.Join(',', rows)}]") };
        }, seen)));

        var all = await source.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SupabaseProtectedCallsigns.PageSize + 1, all.Count);
        Assert.Equal(2, seen.Count);
        Assert.Contains("offset=1000", seen[1].Url, StringComparison.Ordinal);
        Assert.All(seen, s => Assert.Equal("sb_secret_test", s.Key));
    }

    [Fact]
    public async Task F107_review_5_over_the_cap_the_feed_subscribes_up_to_it_and_says_so_and_says_so()
    {
        var registry = new ProtectedCallsignRegistry(new ListSource("IN3FOB", "IN4FOB", "IN5FOB"), NullSourceHealth.Instance);
        await registry.RefreshAsync(TestContext.Current.CancellationToken);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 3, 14, 0, TimeSpan.Zero));
        var feed = new PskReporterFeed(Options.Create(new ProtectionOptions { PskReporterMaxCallsigns = 2 }), registry, new ProtectionCollector(registry, new UseAggregator()), time, Microsoft.Extensions.Logging.Abstractions.NullLogger<PskReporterFeed>.Instance);

        Assert.Equal(2, feed.WantedCallsigns().Count);
        Assert.Equal("cap exceeded: 3 > 2", feed.CapProblem());
    }

    [Fact]
    public async Task The_PSKReporter_feed_drops_a_message_that_is_not_about_a_protected_callsign()
    {
        var agg = new UseAggregator();
        var registry = new ProtectedCallsignRegistry(new ListSource("IN3FOB"), NullSourceHealth.Instance);
        await registry.RefreshAsync(TestContext.Current.CancellationToken);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 3, 14, 0, TimeSpan.Zero));
        var feed = new PskReporterFeed(Options.Create(new ProtectionOptions()), registry, new ProtectionCollector(registry, agg), time, Microsoft.Extensions.Logging.Abstractions.NullLogger<PskReporterFeed>.Instance);

        feed.Handle("""{"f":14074500,"md":"FT8","rp":-3,"sc":"DL1XYZ","rc":"I1ABC","rl":"JN45","b":"20m"}"""u8);
        feed.Handle("""{"f":14074500,"md":"FT8","rp":-3,"sc":"IN3FOB/P","rc":"I1ABC","rl":"JN45","b":"20m"}"""u8);
        feed.Handle("garbage"u8);

        var use = Assert.Single(agg.Drain(time.GetUtcNow().AddMinutes(1)));
        Assert.Equal(ProtectionSignal.PskReporter, use.Signal);
        Assert.NotNull(feed.Probe.Problem());
    }

    private sealed class ListSource(params string[] calls) : IProtectedCallsignSource
    {
        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(calls);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer, List<(string, string?, string)>? seen) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            seen?.Add((request.RequestUri!.ToString(), request.Headers.TryGetValues("apikey", out var k) ? k.Single() : null, body));
            return answer(request);
        }
    }
}
