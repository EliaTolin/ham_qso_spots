using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Rbn;

/// <summary>The RBN node list: read every 30 minutes, the last good read kept (plan F71, A2).</summary>
public class RbnNodesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 5, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider time = new(Now);
    private readonly FakeNodeSource source = new();
    private readonly FakeNodeStore store = new();
    private readonly RecordingHealth health = new();
    private readonly RbnNodeCatalog catalog = new();

    private RefreshRbnNodes Refresh() => new(source, store, catalog, health, time);

    private static RbnNode Node(string call, string? locator = "JN79FX") => new(call, locator, ["20m", "40m"], true, Now);

    [Fact]
    public async Task A_good_read_is_saved_and_used_and_health_is_ok()
    {
        source.Next = new RbnNodeList([Node("OK4YL"), Node("PA5KT-4", "JO11WL")], FormatChanged: false);

        await Refresh().RefreshAsync(Ct);

        Assert.Equal(1, store.Replaced);
        Assert.Equal("JO11WL", catalog.Find("PA5KT-4")!.Locator);
        Assert.Equal(new RbnNodesInfo(Now, 2, false), catalog.Info(Now));
        Assert.Equal(("rbn_nodes", true, (string?)null), health.Calls.Single());
    }

    [Fact]
    public async Task A_page_whose_format_changed_keeps_the_last_good_read_marks_it_stale_and_fails_health()
    {
        source.Next = new RbnNodeList([Node("OK4YL")], false);
        await Refresh().RefreshAsync(Ct);
        source.Next = new RbnNodeList([], FormatChanged: true);
        time.Advance(TimeSpan.FromMinutes(30));

        await Refresh().RefreshAsync(Ct);

        Assert.Equal(1, store.Replaced);
        Assert.NotNull(catalog.Find("OK4YL"));
        Assert.Equal(new RbnNodesInfo(Now, 1, true), catalog.Info(time.GetUtcNow()));
        Assert.Equal(("rbn_nodes", false, "format changed"), health.Calls[^1]);
    }

    [Fact]
    public async Task A_page_with_no_node_is_a_changed_format()
    {
        source.Next = new RbnNodeList([], FormatChanged: false);

        await Refresh().RefreshAsync(Ct);

        Assert.Equal(0, store.Replaced);
        Assert.True(catalog.Info(Now).Stale);
        Assert.False(health.Calls.Single().Ok);
    }

    [Fact]
    public async Task A_site_that_does_not_answer_keeps_the_last_good_read_which_goes_stale_after_2_hours()
    {
        source.Next = new RbnNodeList([Node("OK4YL")], false);
        await Refresh().RefreshAsync(Ct);
        source.Failure = new HttpRequestException("503");

        time.Advance(TimeSpan.FromHours(2));
        await Refresh().RefreshAsync(Ct);
        Assert.False(catalog.Info(time.GetUtcNow()).Stale);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(catalog.Info(time.GetUtcNow()).Stale);
        Assert.NotNull(catalog.Find("OK4YL"));
        Assert.Equal(("rbn_nodes", false, "503"), health.Calls[^1]);
    }

    [Fact]
    public async Task At_start_the_saved_list_is_used_before_the_first_read()
    {
        store.Saved = new SavedRbnNodes([Node("OK4YL")], Now.AddMinutes(-40));

        await Refresh().LoadSavedAsync(Ct);

        Assert.NotNull(catalog.Find("OK4YL"));
        Assert.Equal(new RbnNodesInfo(Now.AddMinutes(-40), 1, false), catalog.Info(Now));
    }

    [Fact]
    public void Before_any_read_there_is_no_list_and_it_is_stale() => Assert.Equal(new RbnNodesInfo(null, 0, true), catalog.Info(Now));

    [Fact]
    public async Task A_skimmer_is_found_by_its_call_without_the_hash_in_any_case()
    {
        source.Next = new RbnNodeList([Node("OK4YL")], false);
        await Refresh().RefreshAsync(Ct);

        Assert.NotNull(catalog.Find("ok4yl"));
        Assert.Null(catalog.Find("OK4YL-2"));
    }
}
