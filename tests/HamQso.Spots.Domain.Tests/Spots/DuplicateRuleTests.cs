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
    public void The_LoTW_and_eQSL_flags_of_HamQTH_survive_the_merge_with_a_telnet_spot()
    {
        var telnet = Dx("DXFun");
        var hamQth = Dx("HamQTH", minutes: 1) with { Lotw = true, Eqsl = false };

        var merged = DuplicateRule.Merge(telnet, hamQth);

        Assert.Equal(((bool?)true, (bool?)false), (merged.Lotw, merged.Eqsl));
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

    private static Spot Pota(string id, double khz = 7074, int minutes = 0, string reference = "US-6701", string call = "K8RLE") => SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Pota,
        SpotKey = id,
        Callsign = call,
        FrequencyKhz = khz,
        SpotTime = T0.AddMinutes(minutes),
        Feeds = ["POTA"],
        Mode = "FT8",
        Reference = reference,
        Comment = $"spot {id}",
    });

    [Fact]
    public void An_xOTA_spot_is_keyed_by_its_activation_and_band_not_by_the_program_id()
    {
        Assert.Equal("K8RLE_US-6701_40m", Pota("59545154").SpotKey);
    }

    [Fact]
    public void A_new_spot_of_the_same_activation_and_band_updates_it_hours_later_too()
    {
        var first = Pota("1");
        var respot = Pota("2", khz: 7076, minutes: 180);

        Assert.True(DuplicateRule.IsDuplicate(first, respot));
        var merged = DuplicateRule.Merge(first, respot);
        Assert.Equal((7076.0, T0.AddMinutes(180), "spot 2"), (merged.FrequencyKhz, merged.SpotTime, merged.Comment));
    }

    [Fact]
    public void An_older_spot_of_the_activation_never_replaces_the_newer_one()
    {
        var newer = Pota("2", khz: 7076, minutes: 30);

        Assert.Equal(newer, DuplicateRule.Merge(newer, Pota("1")));
    }

    [Fact]
    public void Another_band_or_another_reference_is_another_spot()
    {
        Assert.False(DuplicateRule.IsDuplicate(Pota("1"), Pota("2", khz: 14074)));
        Assert.False(DuplicateRule.IsDuplicate(Pota("1"), Pota("2", reference: "US-0001")));
    }
}
