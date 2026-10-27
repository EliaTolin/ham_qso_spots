using System.Text.Json;
using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Tests.Radio;

public class BandPlanTests
{
    public static TheoryData<BandPlanCase> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "band_plan_cases.json")));
        var data = new TheoryData<BandPlanCase>();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(new BandPlanCase(
                c.GetProperty("khz").GetDouble(),
                c.GetProperty("region").GetString(),
                c.GetProperty("comment").GetString(),
                c.GetProperty("spotter").GetString(),
                c.GetProperty("mode").GetString()!,
                c.GetProperty("source").GetString(),
                c.TryGetProperty("note", out var note) ? note.GetString() : null));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolves_the_same_mode_as_the_app_and_match_alerts(BandPlanCase c)
    {
        IaruRegion? region = c.Region switch
        {
            "R1" => IaruRegion.R1,
            "R2" => IaruRegion.R2,
            "R3" => IaruRegion.R3,
            "FCC" => IaruRegion.Fcc,
            _ => null,
        };
        ModeSource? source = c.Source switch
        {
            "reported" => ModeSource.Reported,
            "band_plan" => ModeSource.BandPlan,
            _ => null,
        };

        var resolved = BandPlan.Resolve(c.Khz, c.Comment, c.Spotter, region);

        Assert.Equal(new ResolvedMode(c.Mode, source), resolved);
    }

    [Theory]
    [InlineData("CW", ModeCategory.Cw)]
    [InlineData("usb", ModeCategory.Phone)]
    [InlineData("FT8", ModeCategory.Digital)]
    [InlineData("", null)]
    [InlineData("XYZ", null)]
    public void Groups_modes_like_the_app(string mode, ModeCategory? expected) =>
        Assert.Equal(expected, BandPlan.CategoryOf(mode));

    [Theory]
    [InlineData(291, "NA", IaruRegion.Fcc)]
    [InlineData(248, "EU", IaruRegion.R1)]
    [InlineData(15, "AS", IaruRegion.R1)]
    [InlineData(339, "AS", IaruRegion.R3)]
    [InlineData(100, "SA", IaruRegion.R2)]
    [InlineData(null, null, null)]
    public void Picks_the_band_plan_region(int? dxcc, string? continent, IaruRegion? expected) =>
        Assert.Equal(expected, BandPlan.RegionOf(dxcc, continent));
}

public sealed record BandPlanCase(double Khz, string? Region, string? Comment, string? Spotter, string Mode, string? Source, string? Note)
{
    public override string ToString() => FormattableString.Invariant($"{Khz} {Region} '{Comment}' {Note}");
}
