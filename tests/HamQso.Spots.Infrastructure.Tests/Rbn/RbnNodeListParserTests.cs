using HamQso.Spots.Domain.Rbn;
using HamQso.Spots.Infrastructure.Rbn;

namespace HamQso.Spots.Infrastructure.Tests.Rbn;

/// <summary>
/// The RBN node list page (status.php?t=skt), a short copy written by hand
/// from the real one of 07/11/2026 (Fixtures/rbn_nodes_skt.html), plus a broken row.
/// The page is not ours: anything odd is left out, never trusted.
/// </summary>
public class RbnNodeListParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 5, 0, TimeSpan.Zero);

    private static string Page => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rbn_nodes_skt.html"));

    [Fact]
    public void Every_node_with_its_call_locator_bands_online_and_last_seen()
    {
        var list = RbnNodeListParser.Parse(Page, Now);

        Assert.False(list.FormatChanged);
        Assert.Equal(["OK4YL", "PA5KT-4", "Z61DX", "KM7W"], list.Nodes.Select(n => n.Call));
        var ok = list.Nodes[0];
        Assert.Equal(("jn79fx", true, Now.AddSeconds(-3)), (ok.Locator, ok.Online, ok.LastSeenAt));
        Assert.Equal(["10m", "15m", "20m", "40m"], ok.Bands);
    }

    [Fact]
    public void A_node_that_is_not_online_has_no_bands_and_was_last_seen_long_ago()
    {
        var km7w = RbnNodeListParser.Parse(Page, Now).Nodes.Single(n => n.Call == "KM7W");

        Assert.Equal((false, Now.AddSeconds(-1506717)), (km7w.Online, km7w.LastSeenAt));
        Assert.Empty(km7w.Bands);
    }

    [Fact]
    public void The_locator_is_kept_as_written_its_check_is_the_position_rule()
    {
        var z6 = RbnNodeListParser.Parse(Page, Now).Nodes.Single(n => n.Call == "Z61DX");

        Assert.Equal("KN02", z6.Locator);
        Assert.Equal(SkimmerPosition.Country, SkimmerPosition.Of(z6.Locator, (42.67, 21.17))!.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Service temporarily unavailable</body></html>")]
    [InlineData("<table><tr><td>OK4YL</td></tr></table>")]
    public void A_page_without_node_rows_has_changed_format(string page)
    {
        var list = RbnNodeListParser.Parse(page, Now);

        Assert.True(list.FormatChanged);
        Assert.Empty(list.Nodes);
    }

    [Fact]
    public void A_call_that_is_not_a_call_is_left_out()
    {
        var page = Page.Replace("PA5KT-4 </a>", "PA5KT 4 &amp; x</a>", StringComparison.Ordinal);

        Assert.DoesNotContain(RbnNodeListParser.Parse(page, Now).Nodes, n => n.Call.StartsWith("PA5KT", StringComparison.Ordinal));
    }
}
