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

    private static IDisposable? Start(WatchRbn watch, string user, bool anonymous = false, string address = "10.0.0.1") =>
        watch.TryStart(user, anonymous, address).Lease;

    [Fact]
    public void A_user_has_at_most_two_streams_at_once()
    {
        var watch = Watch(new FakeFeed([]));

        var first = Start(watch, "u");
        using var second = Start(watch, "u");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(new StreamAdmission(null, "too_many_streams", WatchRbn.RetryAfter), watch.TryStart("u", false, "10.0.0.1"));
        using var other = Start(watch, "v");
        Assert.NotNull(other);

        first!.Dispose();
        first.Dispose();
        using var third = Start(watch, "u");
        Assert.NotNull(third);
        Assert.Null(Start(watch, "u"));
    }

    // Security review F71: anonymous Supabase users are free to create, so a new one per stream must not get around the limits.

    [Fact]
    public void Anonymous_users_have_at_most_four_streams_per_client_address()
    {
        var watch = Watch(new FakeFeed([]));
        var leases = Enumerable.Range(0, WatchRbn.MaxAnonymousStreamsPerAddress).Select(i => Start(watch, $"anon{i}", true)).ToList();

        Assert.All(leases, Assert.NotNull);
        Assert.Equal(new StreamAdmission(null, "too_many_streams", WatchRbn.RetryAfter), watch.TryStart("anon-new", true, "10.0.0.1"));
        using (var elsewhere = Start(watch, "anon-new", true, "10.0.0.2")) Assert.NotNull(elsewhere);
        // Users with an account are not counted per address (a club behind one address).
        using (var account = Start(watch, "account", false, "10.0.0.1")) Assert.NotNull(account);

        leases[0]!.Dispose();
        using var again = Start(watch, "anon-new", true, "10.0.0.1");
        Assert.NotNull(again);
        foreach (var l in leases) l?.Dispose();
    }

    [Fact]
    public void All_anonymous_streams_together_have_a_ceiling_accounts_do_not_count_in_it()
    {
        var watch = Watch(new FakeFeed([]));
        var leases = Enumerable.Range(0, WatchRbn.MaxAnonymousStreams).Select(i => Start(watch, $"anon{i}", true, $"10.0.{i / 200}.{i % 200}")).ToList();

        Assert.All(leases, Assert.NotNull);
        Assert.Equal(new StreamAdmission(null, "busy", WatchRbn.RetryAfter), watch.TryStart("one-more", true, "192.168.1.1"));
        using (var account = Start(watch, "account")) Assert.NotNull(account);

        leases[^1]!.Dispose();
        using var again = Start(watch, "one-more", true, "192.168.1.1");
        Assert.NotNull(again);
        foreach (var l in leases) l?.Dispose();
    }

    [Fact]
    public void A_refused_stream_takes_no_place()
    {
        var watch = Watch(new FakeFeed([]));
        using var a = Start(watch, "u");
        using var b = Start(watch, "u");
        for (var i = 0; i < 10; i++) Assert.Null(Start(watch, "u"));

        a!.Dispose();

        using var c = Start(watch, "u");
        Assert.NotNull(c);
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
