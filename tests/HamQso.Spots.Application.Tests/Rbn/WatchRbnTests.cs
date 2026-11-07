using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Tests.Rbn;

public class WatchRbnTests
{
    private static WatchRbn Watch(IRbnFeed feed, RbnNodeCatalog? catalog = null) => new(feed, new LocateSkimmer(catalog ?? new RbnNodeCatalog()));

    [Fact]
    public async Task Only_the_lines_of_the_station_come_through_with_any_portable_prefix_or_suffix()
    {
        var feed = new FakeFeed(["IU4VRB", "IU4VRB/P", "EA8/IU4VRB", "IU4VRB/QRP", "IU4VRBX", "IU4VR", "K1ABC", "IK2ABC/IU4VRB"]);
        var watch = Watch(feed);

        var calls = new List<string>();
        await foreach (var line in watch.WatchAsync("iu4vrb/p", TestContext.Current.CancellationToken)) calls.Add(line.Call);

        // IK2ABC/IU4VRB: on a tie the later part is the base call (the app's rule).
        Assert.Equal(["IU4VRB", "IU4VRB/P", "EA8/IU4VRB", "IU4VRB/QRP", "IK2ABC/IU4VRB"], calls);
    }

    [Fact]
    public void A_user_has_at_most_two_streams_at_once()
    {
        var watch = Watch(new FakeFeed([]));

        var first = watch.TryStart("u");
        using var second = watch.TryStart("u");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(watch.TryStart("u"));
        using var other = watch.TryStart("v");
        Assert.NotNull(other);

        first!.Dispose();
        first.Dispose();
        using var third = watch.TryStart("u");
        Assert.NotNull(third);
        Assert.Null(watch.TryStart("u"));
    }

    [Fact]
    public void A_spot_event_has_the_fields_read_by_the_server_and_the_true_position_of_the_skimmer()
    {
        var catalog = new RbnNodeCatalog();
        catalog.Set([new RbnNode("OK4YL", "JN79FX", [], true, null)], RbnLines.At);

        var e = Watch(new FakeFeed([]), catalog).Describe(RbnLines.Line("IU2DEMO"));

        Assert.Equal(
            new RbnSpotEvent("OK4YL", "IU2DEMO", 14045.1, 17, 23, "CQ", "20m", "1432Z", RbnLines.At, new SkimmerPosition("JN79FX", 49.98, 14.46, "node"), 503, "EU"),
            e);
    }

    private sealed class FakeFeed(string[] calls) : IRbnFeed
    {
        public RbnFeedStatus Status => new(RbnFeedState.Up, null);

        public Task WaitForChangeAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);

        public async IAsyncEnumerable<RbnLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var call in calls)
            {
                await Task.Yield();
                yield return RbnLines.Line(call);
            }
        }
    }
}
