using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Rbn;

/// <summary>GET /v1/rbn/skimmers (plan F71, 3.1).</summary>
public class GetListeningSkimmersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 40, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 14, 32, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 6, 14, 33, 17, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeActivityStore store = new();
    private readonly FakeRbnFeed feed = new();
    private readonly RbnNodeCatalog catalog = new();

    private GetListeningSkimmers Use() => new(store, feed, catalog, new LocateSkimmer(catalog), new FakeTimeProvider(Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20M")]
    [InlineData("11m")]
    public async Task A_band_that_is_not_one_of_ours_is_invalid_band(string? band) =>
        Assert.Equal("invalid_band", (await Use().ExecuteAsync(band, null, null, Ct)).Error);

    [Fact]
    public async Task A_window_that_is_not_valid_is_invalid_window() =>
        Assert.Equal("invalid_window", (await Use().ExecuteAsync("20m", End, Start, Ct)).Error);

    [Fact]
    public async Task The_skimmers_heard_on_the_band_with_their_position_the_feed_and_the_node_list()
    {
        catalog.Set([new RbnNode("OK4YL", "JN79FX", ["20m"], true, Now)], new DateTimeOffset(2026, 11, 6, 14, 5, 0, TimeSpan.Zero));
        store.Rows.AddRange(
        [
            new ActivityMinute("OK4YL", "20m", new DateTimeOffset(2026, 11, 6, 14, 31, 0, TimeSpan.Zero), 42, new DateTimeOffset(2026, 11, 6, 14, 31, 40, TimeSpan.Zero)),
            new ActivityMinute("Z61DX", "20m", new DateTimeOffset(2026, 11, 6, 14, 29, 0, TimeSpan.Zero), 3, new DateTimeOffset(2026, 11, 6, 14, 29, 2, TimeSpan.Zero)),
            new ActivityMinute("Q0XYZ", "20m", new DateTimeOffset(2026, 11, 6, 14, 29, 0, TimeSpan.Zero), 1, new DateTimeOffset(2026, 11, 6, 14, 29, 2, TimeSpan.Zero)),
        ]);

        var r = (await Use().ExecuteAsync("20m", Start, End, Ct)).Value!;

        Assert.Equal(("20m", new DateTimeOffset(2026, 11, 6, 14, 22, 5, TimeSpan.Zero), End), (r.Band, r.From, r.To));
        Assert.Equal(("20m", new DateTimeOffset(2026, 11, 6, 14, 22, 0, TimeSpan.Zero), End), store.LastRead);
        Assert.Equal(feed.Status, r.Feed);
        Assert.Equal(new RbnNodesInfo(new DateTimeOffset(2026, 11, 6, 14, 5, 0, TimeSpan.Zero), 1, false), r.Nodes);
        // A skimmer with no position (no node, unknown entity) is left out: the app could not draw it.
        Assert.Equal(["OK4YL", "Z61DX"], r.Skimmers.Select(s => s.Skimmer.Call));
        Assert.Equal((42, "node"), (r.Skimmers[0].Spots, r.Skimmers[0].Skimmer.Position!.Source));
    }
}
