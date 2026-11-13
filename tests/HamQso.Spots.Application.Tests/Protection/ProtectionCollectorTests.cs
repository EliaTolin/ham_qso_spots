using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Application.Tests.Rbn;
using HamQso.Spots.Domain.Protection;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Tests.Protection;

public class ProtectionCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 14, 20, TimeSpan.Zero);

    private static AlertSpot Cluster(string call, string spotter, double khz = 7020, DateTimeOffset? at = null) => new(SpotEnrichment.Enrich(new Spot
    {
        Program = SpotProgram.Dx,
        SpotKey = $"{call}_{khz}_{spotter}",
        Callsign = call,
        FrequencyKhz = khz,
        SpotTime = at ?? Now,
        Feeds = ["DXFun"],
        Spotters = [spotter],
        Comment = "CW 12 dB",
    }), Respot: false);

    [Fact]
    public async Task C2_an_RBN_line_of_a_protected_callsign_is_a_skimmer_use_and_the_others_are_dropped()
    {
        var agg = new UseAggregator();
        var collector = await Protected.CollectorAsync(agg, "IN3FOB");

        collector.OfferRbn(RbnLines.Line("IN3FOB/P", "DF7GB-#", 7020.0, Now));
        collector.OfferRbn(RbnLines.Line("DL1XYZ", "DF7GB-#", 7020.0, Now));

        var use = Assert.Single(agg.Drain(Now.AddMinutes(1)));
        Assert.Equal("IN3FOB/P", use.Callsign);
        Assert.Equal(ProtectionSignal.Skimmer, use.Signal);
        Assert.Equal("40m", use.Band);
        Assert.Equal("DF7GB-#", use.HeardBy);
        Assert.Equal("CW", use.Mode);
    }

    [Fact]
    public async Task C2_a_skimmer_hearing_its_own_owner_station_is_not_a_use()
    {
        var agg = new UseAggregator();
        var collector = await Protected.CollectorAsync(agg, "IN3FOB");

        collector.OfferRbn(RbnLines.Line("IN3FOB", "IN3FOB-#", 7020.0, Now));

        Assert.Empty(agg.Drain(Now.AddMinutes(1)));
    }

    [Fact]
    public async Task C3_a_cluster_spot_is_a_spot_use_and_a_skimmer_spotter_a_skimmer_use()
    {
        var agg = new UseAggregator();
        var collector = await Protected.CollectorAsync(agg, "IN3FOB");

        collector.OfferSpots([Cluster("IN3FOB", "IK2ABC"), Cluster("IN3FOB", "DF7GB-#", 14025), Cluster("DL1XYZ", "IK2ABC")]);

        var uses = agg.Drain(Now.AddMinutes(1));
        Assert.Equal([ProtectionSignal.Spot, ProtectionSignal.Skimmer], uses.OrderBy(u => u.Signal).Select(u => u.Signal));
        Assert.Equal("IK2ABC", uses.Single(u => u.Signal == ProtectionSignal.Spot).HeardBy);
    }

    [Fact]
    public async Task C3_a_self_spot_is_excluded_whatever_the_ssid()
    {
        var agg = new UseAggregator();
        var collector = await Protected.CollectorAsync(agg, "IN3FOB");

        collector.OfferSpots([Cluster("IN3FOB", "IN3FOB"), Cluster("IN3FOB/P", "IN3FOB-2"), Cluster("IN3FOB", "IN3FOB-#")]);

        Assert.Empty(agg.Drain(Now.AddMinutes(1)));
    }

    [Fact]
    public async Task C3_the_same_skimmer_from_the_RBN_and_from_a_cluster_is_one_use_with_one_receiver()
    {
        var agg = new UseAggregator();
        var collector = await Protected.CollectorAsync(agg, "IN3FOB");

        collector.OfferRbn(RbnLines.Line("IN3FOB", "DF7GB-#", 7020.0, Now));
        collector.OfferSpots([Cluster("IN3FOB", "DF7GB-#")]);

        var use = Assert.Single(agg.Drain(Now.AddMinutes(1)));
        Assert.Equal(1, use.Raw["receivers"]);
        Assert.Equal(2, use.Raw["reports"]);
    }

    [Fact]
    public async Task C4_a_PSKReporter_report_of_a_protected_callsign_is_a_pskreporter_use_the_others_are_dropped_and_own_reception_excluded()
    {
        var agg = new UseAggregator();
        var collector = await Protected.CollectorAsync(agg, "IN3FOB");

        collector.OfferPsk(Protected.Psk("IN3FOB/P"), Now);
        collector.OfferPsk(Protected.Psk("DL1XYZ"), Now);
        collector.OfferPsk(Protected.Psk("IN3FOB", receiver: "IN3FOB/P", band: "40m"), Now);

        var use = Assert.Single(agg.Drain(Now.AddMinutes(1)));
        Assert.Equal(ProtectionSignal.PskReporter, use.Signal);
        Assert.Equal("IN3FOB/P", use.Callsign);
        Assert.Equal("JO40", use.HeardLocator);
        Assert.Equal(14074.5, use.FrequencyKhz);
    }

    [Fact]
    public async Task C1_a_failed_read_keeps_the_last_set_of_callsigns()
    {
        var source = new FakeCallsignSource("IN3FOB");
        var health = new RecordingHealth();
        var registry = new ProtectedCallsignRegistry(source, health);
        await registry.RefreshAsync(CancellationToken.None);
        var version = registry.Version;

        source.Fails = true;
        await registry.RefreshAsync(CancellationToken.None);

        Assert.NotNull(registry.Current.Match("IN3FOB/P"));
        Assert.Equal(version, registry.Version);
        Assert.False(health.Calls[^1].Ok);
    }

    [Fact]
    public async Task F107_review_11b_the_same_skimmer_from_the_RBN_and_from_a_cluster_is_one_receiver_in_one_minute()
    {
        var agg = new UseAggregator();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 3, 14, 59, TimeSpan.Zero));
        var collector = await Protected.CollectorAsync(agg, clock, "IN3FOB");

        collector.OfferRbn(RbnLines.Line("IN3FOB", "DF7GB-#", 7020.0, clock.GetUtcNow()));
        // the cluster stamps the spot in its own clock, a few seconds later (next minute)
        collector.OfferSpots([Cluster("IN3FOB", "DF7GB-#", 7020, at: Now.AddSeconds(45))]);

        var use = Assert.Single(agg.Drain(Now.AddMinutes(5)));
        Assert.Equal(1, use.Raw["receivers"]);
    }
}
