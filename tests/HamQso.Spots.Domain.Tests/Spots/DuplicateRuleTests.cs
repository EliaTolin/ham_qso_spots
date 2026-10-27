using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Domain.Tests.Spots;

public class DuplicateRuleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static Spot Dx(string feed, double khz = 14025.0, int minutes = 0, string spotter = "EA4RCH") => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = "x",
        Callsign = "JA1ABC",
        FrequencyKhz = khz,
        SpotTime = T0.AddMinutes(minutes),
        Feeds = [feed],
        Spotters = [spotter],
    };

    [Fact]
    public void The_same_spot_from_DXFun_and_HamQTH_within_10_minutes_is_one_spot_with_both_feeds()
    {
        var fromDxFun = Dx("DXFun");
        var fromHamQth = Dx("HamQTH", khz: 14025.2, minutes: 9, spotter: "IK5PWJ");

        Assert.True(DuplicateRule.IsDuplicate(fromDxFun, fromHamQth));
        var merged = DuplicateRule.Merge(fromDxFun, fromHamQth);
        Assert.Equal(["DXFun", "HamQTH"], merged.Feeds);
        Assert.Equal(["EA4RCH", "IK5PWJ"], merged.Spotters);
    }

    [Fact]
    public void After_10_minutes_it_is_a_new_spot() =>
        Assert.False(DuplicateRule.IsDuplicate(Dx("DXFun"), Dx("HamQTH", minutes: 11)));

    [Fact]
    public void Another_kHz_is_another_spot() =>
        Assert.False(DuplicateRule.IsDuplicate(Dx("DXFun"), Dx("HamQTH", khz: 14027.0)));

    [Fact]
    public void A_reported_mode_wins_over_the_band_plan()
    {
        var guessed = Dx("DXFun") with { Mode = "CW", ModeSource = ModeSource.BandPlan, ModeCategory = ModeCategory.Cw };
        var reported = Dx("HamQTH") with { Mode = "RTTY", ModeSource = ModeSource.Reported, ModeCategory = ModeCategory.Digital };

        var merged = DuplicateRule.Merge(guessed, reported);

        Assert.Equal(("RTTY", (ModeSource?)ModeSource.Reported, (ModeCategory?)ModeCategory.Digital), (merged.Mode, merged.ModeSource, merged.ModeCategory));
    }

    [Fact]
    public void Xota_spots_merge_only_by_their_own_id()
    {
        var pota = Dx("POTA") with { Program = SpotProgram.Pota, SpotKey = "123" };
        Assert.True(DuplicateRule.IsDuplicate(pota, pota with { FrequencyKhz = 7100 }));
        Assert.False(DuplicateRule.IsDuplicate(pota, pota with { SpotKey = "124" }));
    }
}
