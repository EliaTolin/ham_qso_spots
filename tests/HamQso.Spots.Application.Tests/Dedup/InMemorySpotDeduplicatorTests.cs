using HamQso.Spots.Application.Dedup;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Dedup;

public class InMemorySpotDeduplicatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static Spot Dx(string feed, int minutes = 0) => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = $"JA1ABC_{feed}_{minutes}",
        Callsign = "JA1ABC",
        FrequencyKhz = 14025,
        SpotTime = Now.AddMinutes(minutes),
        Feeds = [feed],
    };

    [Fact]
    public void A_spot_is_new_then_merged_then_unchanged()
    {
        var dedup = new InMemorySpotDeduplicator(new FakeTimeProvider(Now));

        Assert.Equal(DedupOutcome.New, dedup.Accept(Dx("DXFun")).Outcome);
        Assert.Equal(DedupOutcome.Merged, dedup.Accept(Dx("HamQTH", 2)).Outcome);
        Assert.Equal(DedupOutcome.Unchanged, dedup.Accept(Dx("HamQTH", 3)).Outcome);
    }

    [Fact]
    public void The_same_call_and_kHz_after_the_window_is_a_new_spot()
    {
        var dedup = new InMemorySpotDeduplicator(new FakeTimeProvider(Now));
        dedup.Accept(Dx("DXFun"));

        Assert.Equal(DedupOutcome.New, dedup.Accept(Dx("DXFun", 15)).Outcome);
    }

    [Fact]
    public void Spots_older_than_twice_the_window_are_forgotten()
    {
        var time = new FakeTimeProvider(Now);
        var dedup = new InMemorySpotDeduplicator(time);
        dedup.Seed([Dx("DXFun")]);

        time.Advance(TimeSpan.FromMinutes(25));

        // The same old spot again (a telnet backlog): no longer remembered.
        Assert.Equal(DedupOutcome.New, dedup.Accept(Dx("DXFun")).Outcome);
    }
}
