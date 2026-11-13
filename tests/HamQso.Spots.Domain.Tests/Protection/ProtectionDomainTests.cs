using System.Text;
using HamQso.Spots.Domain.Protection;

namespace HamQso.Spots.Domain.Tests.Protection;

public class ProtectionDomainTests
{
    private static readonly DateTimeOffset Minute = new(2026, 11, 13, 3, 14, 0, TimeSpan.Zero);

    private static Hearing Hear(string by, int second = 5, string band = "40m", ProtectionSignal signal = ProtectionSignal.Skimmer) =>
        new("IN3FOB/P", signal, Minute.AddSeconds(second), band, 7020, "CW", by, null, 12);

    [Fact]
    public void A_protected_callsign_matches_its_variants_by_base_callsign()
    {
        var set = new ProtectedCallsigns(["in3fob", "not a call"]);

        Assert.Equal("IN3FOB", set.Match("IN3FOB/P"));
        Assert.Equal("IN3FOB", set.Match("DL/IN3FOB"));
        Assert.Equal("IN3FOB", set.Match("IN3FOB/QRP"));
        Assert.Null(set.Match("IN3FOC"));
        Assert.Equal(1, set.Count);
    }

    [Theory]
    [InlineData("DF7GB-#", "DF7GB")]
    [InlineData("IN3FOB-2", "IN3FOB")]
    [InlineData("in3fob/p", "IN3FOB")]
    public void The_station_of_a_spotter_has_no_ssid_and_no_portable_part(string spotter, string expected) =>
        Assert.Equal(expected, ProtectedCallsigns.StationOf(spotter));

    [Fact]
    public void A_signal_heard_by_100_receivers_is_one_use_with_the_count_and_the_first_10()
    {
        var agg = new UseAggregator();
        for (var i = 0; i < 100; i++) agg.Add("IN3FOB", Hear($"RX{i}", signal: ProtectionSignal.PskReporter));

        var use = Assert.Single(agg.Drain(Minute.AddMinutes(1)));

        Assert.Equal(100, use.Raw["receivers"]);
        Assert.Equal(100, use.Raw["reports"]);
        var heard = (Dictionary<string, object?>[])use.Raw["heard"]!;
        Assert.Equal(10, heard.Length);
        Assert.Equal("RX0", heard[0]["by"]);
        Assert.Equal("RX0", use.HeardBy);
    }

    [Fact]
    public void There_is_one_use_per_callsign_band_signal_and_minute_and_the_open_minute_waits()
    {
        var agg = new UseAggregator();
        agg.Add("IN3FOB", Hear("A"));
        agg.Add("IN3FOB", Hear("B", band: "20m"));
        agg.Add("IN3FOB", Hear("C", second: 65));

        Assert.Equal(2, agg.Drain(Minute.AddSeconds(70)).Count);
        Assert.Single(agg.Drain(Minute.AddSeconds(130)));
        Assert.Empty(agg.Drain(Minute.AddHours(1), everything: true));
    }

    [Fact]
    public void The_source_id_depends_only_on_what_the_use_is()
    {
        UseAggregator Fill()
        {
            var a = new UseAggregator();
            a.Add("IN3FOB", Hear("A", 3));
            a.Add("IN3FOB", Hear("B", 40));
            return a;
        }

        var one = Fill().Drain(Minute.AddMinutes(2)).Single().SourceId;
        var two = Fill().Drain(Minute.AddMinutes(9)).Single().SourceId;

        Assert.Equal(one, two);
        Assert.Equal($"skimmer:IN3FOB:40m:{Minute.ToUnixTimeSeconds()}", one);
    }

    [Fact]
    public void Memory_is_bounded_receivers_and_open_uses()
    {
        var agg = new UseAggregator();
        for (var i = 0; i < UseAggregator.MaxReceivers + 300; i++) agg.Add("IN3FOB", Hear($"RX{i}"));
        Assert.Equal(UseAggregator.MaxReceivers, agg.Drain(Minute.AddMinutes(1)).Single().Raw["receivers"]);

        var accepted = Enumerable.Range(0, UseAggregator.MaxOpen + 10).Count(i => agg.Add($"C{i}", Hear("X")));
        Assert.Equal(UseAggregator.MaxOpen, accepted);
    }

    [Fact]
    public void The_largest_raw_stays_under_2_KB()
    {
        var agg = new UseAggregator();
        for (var i = 0; i < 50; i++) agg.Add("IN3FOB", new Hearing("IN3FOB", ProtectionSignal.PskReporter, Minute, "20m", 14074, "FT8", $"SP9ABC/MM{i:00}", "JO40XX", -24));

        var raw = agg.Drain(Minute.AddMinutes(1)).Single().Raw;

        Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(raw).Length < 2000);
    }

    [Fact]
    public void A_PSKReporter_message_is_parsed_and_a_broken_one_is_not_a_report()
    {
        const string json = """{"sq":1,"f":14074500,"md":"FT8","rp":-12,"t":1,"sc":"in3fob","sl":"JN56","rc":"DL1ABC","rl":"JO40","sa":248,"ra":230,"b":"20m"}""";

        var report = PskReportParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(new PskReport("IN3FOB", "DL1ABC", "JO40", "20m", 14074.5, "FT8", -12), report);
        Assert.Null(PskReportParser.Parse("not json"u8));
        Assert.Null(PskReportParser.Parse("""{"sc":"A1B"}"""u8));
        Assert.Null(PskReportParser.Parse("[1]"u8));
    }

    [Fact]
    public void The_subscriptions_are_narrow_one_set_of_topics_per_protected_callsign()
    {
        var topics = PskTopics.For(["IN3FOB", "IN3FOB"]);

        Assert.Equal("pskr/filter/v2/+/+/IN3FOB/#", topics[0]);
        Assert.Contains("pskr/filter/v2/+/+/IN3FOB_P/#", topics);
        Assert.DoesNotContain(topics, t => t.EndsWith("/+/#", StringComparison.Ordinal) && t.Contains("v2/+/+/+", StringComparison.Ordinal));
        Assert.Equal(1 + PskTopics.SuffixVariants.Length, topics.Count);
        Assert.Empty(PskTopics.For([]));
    }
}
