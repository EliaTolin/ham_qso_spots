using HamQso.Spots.Domain.References;

namespace HamQso.Spots.Domain.Tests.References;

/// <summary>F53.3: the file of the world reference catalog the apps download.</summary>
public sealed class ReferenceCatalogFormatTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 3, 0, 0, TimeSpan.Zero);

    private static readonly CatalogReference Lavaze = new(
        "IT-1271", "POTA", "Torbiere del Lavaze'", "Trentino-Alto Adige, Italy", 248, "JN56NH", 46.35541, 11.49127, null, null);

    private static readonly CatalogReference Cimone = new(
        "I/ER-001", "SOTA", "Monte Cimone", "Emilia-Romagna", 248, "JN54MF", 44.19333, 10.70111, 2165, 10);

    [Fact]
    public void Every_reference_comes_back_the_same()
    {
        var file = ReferenceCatalogFormat.Write([Lavaze, Cimone], At);

        var read = ReferenceCatalogFormat.Read(file.Gzip);

        Assert.Equal(At, read.GeneratedAt);
        Assert.Equal([Cimone, Lavaze], read.References);
        Assert.Equal(2, file.Count);
    }

    [Fact]
    public void It_is_gzip()
    {
        var file = ReferenceCatalogFormat.Write([Lavaze], At);

        Assert.Equal([0x1f, 0x8b], file.Gzip[..2]);
    }

    [Fact]
    public void A_tab_or_a_new_line_in_a_name_never_breaks_a_row()
    {
        var odd = Lavaze with { Name = "Parco\tdel\nLago", Location = "A\r\nB" };

        var read = ReferenceCatalogFormat.Read(ReferenceCatalogFormat.Write([odd], At).Gzip);

        Assert.Equal(("Parco del Lago", "A B"), (read.References[0].Name, read.References[0].Location));
    }

    [Fact]
    public void The_same_references_a_week_later_keep_the_ETag_so_the_apps_download_nothing()
    {
        var before = ReferenceCatalogFormat.Write([Lavaze, Cimone], At);
        var after = ReferenceCatalogFormat.Write([Cimone, Lavaze], At.AddDays(7));

        Assert.Equal(before.ETag, after.ETag);
        Assert.NotEqual(before.ETag, ReferenceCatalogFormat.Write([Lavaze], At).ETag);
    }

    [Fact]
    public void References_are_in_code_order()
    {
        var read = ReferenceCatalogFormat.Read(ReferenceCatalogFormat.Write([Lavaze, Cimone], At).Gzip);

        Assert.Equal(["I/ER-001", "IT-1271"], read.References.Select(r => r.Ref));
    }
}
