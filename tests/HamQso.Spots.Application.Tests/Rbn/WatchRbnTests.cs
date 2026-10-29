using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;

namespace HamQso.Spots.Application.Tests.Rbn;

public class WatchRbnTests
{
    private static RbnLine Line(string call) => new($"DX de OK4YL-#:   7025.10  {call}  CW  7 dB  23 WPM  CQ  2055Z", call);

    [Fact]
    public async Task Only_the_lines_of_the_station_come_through_with_any_portable_prefix_or_suffix()
    {
        var feed = new FakeFeed(["IU4VRB", "IU4VRB/P", "EA8/IU4VRB", "IU4VRB/QRP", "IU4VRBX", "IU4VR", "K1ABC", "IK2ABC/IU4VRB"]);
        var watch = new WatchRbn(feed);

        var calls = new List<string>();
        await foreach (var line in watch.WatchAsync("iu4vrb/p", TestContext.Current.CancellationToken)) calls.Add(line.Call);

        // IK2ABC/IU4VRB: on a tie the later part is the base call (the app's rule).
        Assert.Equal(["IU4VRB", "IU4VRB/P", "EA8/IU4VRB", "IU4VRB/QRP", "IK2ABC/IU4VRB"], calls);
    }

    [Fact]
    public void A_user_has_at_most_two_streams_at_once()
    {
        var watch = new WatchRbn(new FakeFeed([]));

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

    private sealed class FakeFeed(string[] calls) : IRbnFeed
    {
        public async IAsyncEnumerable<RbnLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var call in calls)
            {
                await Task.Yield();
                yield return Line(call);
            }
        }
    }
}
