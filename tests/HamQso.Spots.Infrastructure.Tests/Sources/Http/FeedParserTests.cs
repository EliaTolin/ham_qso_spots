using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Sources.Http;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Http;

public class FeedParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<Spot> Parse(IPolledFeed feed, string fixture) =>
        feed.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), Now);

    [Fact]
    public void POTA_gives_every_activation_and_skips_the_malformed_row()
    {
        var spots = Parse(new PotaFeed(), "pota.json");

        Assert.Equal(38, spots.Count);
        var first = spots[0];
        Assert.Equal(("K8RLE", 7074.0, "FT8", "US-6701", "US-MI"), (first.Callsign, first.FrequencyKhz, first.Mode, first.Reference, first.Location));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 11, 43, 53, TimeSpan.Zero), first.SpotTime);
        Assert.Equal("Haymarsh Lake State Game Land", first.ReferenceName);
        Assert.All(spots, s => Assert.Equal(SpotProgram.Pota, s.Program));
    }

    [Fact]
    public void SOTA_converts_MHz_keeps_QRT_and_skips_test_and_malformed_spots()
    {
        var spots = Parse(new SotaFeed(), "sota.json");

        Assert.Equal(50, spots.Count);
        Assert.DoesNotContain(spots, s => s.Callsign == "G0TST/P" || s.Callsign == "G0ABC/P");
        var qrt = spots.Where(s => s.Qrt).ToArray();
        Assert.Equal(["EA3HP/P", "EA4DON/P", "DF5WC", "OE6HMM/P", "HB9DGW/P"], qrt.Select(s => s.Callsign));
        Assert.Equal([0, 0, 14058.5, 145525, 0], qrt.Select(s => s.FrequencyKhz));
        var first = spots[0];
        Assert.Equal(("HB9CBR/P", 24904.0, "HB/SO-023"), (first.Callsign, first.FrequencyKhz, first.Reference));
        Assert.Equal(("Punkt 1239 bei Zentner", "Bruno", 1239, 2), (first.ReferenceName, first.ActivatorName, first.AltitudeM, first.Points));
    }

    [Fact]
    public void B29_KN4OK_manda_QRT_alle_2017_e_lo_spot_QRT_arriva_segnato_QRT()
    {
        const string body = """
            [
              {"id": 2, "timeStamp": "2026-10-28T20:17:00Z", "activatorCallsign": "KN4OK", "callsign": "KN4OK", "comments": "thanks all",
               "frequency": 7.218, "mode": "SSB", "summitCode": "W4C/WM-001", "type": "QRT"},
              {"id": 1, "timeStamp": "2026-10-28T20:11:00Z", "activatorCallsign": "KN4OK", "callsign": "KN4OK", "comments": "",
               "frequency": 7.218, "mode": "SSB", "summitCode": "W4C/WM-001", "type": "NORMAL"}
            ]
            """;

        var spots = new SotaFeed().Parse(body, Now);

        Assert.Equal([true, false], spots.Select(s => s.Qrt));
        Assert.Equal(("KN4OK", 7218.0, "W4C/WM-001"), (spots[0].Callsign, spots[0].FrequencyKhz, spots[0].Reference));
    }

    [Fact]
    public void WWFF_reads_Unix_time_and_skips_QRT_spots()
    {
        var spots = Parse(new WwffFeed(), "wwff.json");

        Assert.Equal(43, spots.Count);
        Assert.DoesNotContain(spots, s => s.Callsign == "ON4QRT");
        var first = spots[0];
        Assert.Equal(("HB9CBR/P", 24904.0, "HBFF-0040"), (first.Callsign, first.FrequencyKhz, first.Reference));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793101495), first.SpotTime);
        Assert.Equal("Natur park Thal,  Balstahl (SO)", first.ReferenceName);
    }

    [Fact]
    public void HamQTH_keeps_its_DXCC_and_continent_but_not_its_mode_guess()
    {
        var spots = Parse(new HamQthFeed(), "hamqth_v2.json");

        Assert.Equal(50, spots.Count);
        var first = spots[0];
        Assert.Equal(("DL/HB9BIN/P", 7023.9, "DL1HBT", "DM/BW-003"), (first.Callsign, first.FrequencyKhz, first.Spotters[0], first.Comment));
        Assert.Equal((230, "EU"), (first.Dxcc, first.Continent));
        Assert.Equal(((bool?)true, (bool?)false), (first.Lotw, first.Eqsl));
        Assert.Equal("", first.Mode);
        Assert.Equal(["HamQTH"], first.Feeds);
    }

    [Fact]
    public void DX_Summit_reads_UTC_times_without_zone()
    {
        var spots = Parse(new DxSummitFeed(), "dxsummit.json");

        Assert.Equal(50, spots.Count);
        var first = spots[0];
        Assert.Equal(("DL/HB9BIN/P", 7023.9, "DL1HBT"), (first.Callsign, first.FrequencyKhz, first.Spotters[0]));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 11, 45, 35, TimeSpan.Zero), first.SpotTime);
    }

    [Fact]
    public void The_same_spot_on_HamQTH_and_DX_Summit_is_a_duplicate()
    {
        var hamQth = SpotEnrichment.Enrich(Parse(new HamQthFeed(), "hamqth_v2.json")[0]);
        var dxSummit = SpotEnrichment.Enrich(Parse(new DxSummitFeed(), "dxsummit.json")[0]);

        Assert.True(DuplicateRule.IsDuplicate(hamQth, dxSummit));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[{\"bad\": true}]")]
    public void A_body_without_spots_gives_no_spots(string body)
    {
        IPolledFeed[] feeds = [new PotaFeed(), new SotaFeed(), new WwffFeed(), new DxSummitFeed(), new HamQthFeed()];
        foreach (var feed in feeds)
        {
            if (body.Length == 0) Assert.ThrowsAny<System.Text.Json.JsonException>(() => feed.Parse(body, Now));
            else Assert.Empty(feed.Parse(body, Now));
        }
    }
}
