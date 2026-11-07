using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Application.Tests.Rbn;

public class LocateSkimmerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 5, 0, TimeSpan.Zero);

    private static LocateSkimmer Locate(params RbnNode[] nodes)
    {
        var catalog = new RbnNodeCatalog();
        catalog.Set(nodes, Now);
        return new LocateSkimmer(catalog);
    }

    [Fact]
    public void A_skimmer_of_the_node_list_is_where_its_locator_says_with_its_entity()
    {
        var s = Locate(new RbnNode("OK4YL", "JN79FX", [], true, Now)).Locate("OK4YL");

        Assert.Equal(new SkimmerInfo("OK4YL", new SkimmerPosition("JN79FX", 49.98, 14.46, "node"), 503, "EU"), s);
    }

    [Fact]
    public void A_skimmer_outside_the_list_is_at_the_centre_of_its_country()
    {
        var s = Locate().Locate("Z61DX");

        Assert.Equal(new SkimmerInfo("Z61DX", new SkimmerPosition(null, 42.67, 21.17, "country"), 522, "EU"), s);
    }

    [Fact]
    public void A_skimmer_of_no_known_entity_has_no_position() =>
        Assert.Equal(new SkimmerInfo("Q0XYZ", null, null, null), Locate().Locate("Q0XYZ"));
}
