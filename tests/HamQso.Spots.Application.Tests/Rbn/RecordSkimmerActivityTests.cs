using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;
using Microsoft.Extensions.Logging.Abstractions;

namespace HamQso.Spots.Application.Tests.Rbn;

/// <summary>Every RBN spot counted per skimmer, band and minute, written in batches (plan F71, A2).</summary>
public class RecordSkimmerActivityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset M32 = new(2026, 11, 6, 14, 32, 0, TimeSpan.Zero);

    [Fact]
    public void Spots_are_counted_per_skimmer_band_and_minute_of_arrival_with_the_last_one()
    {
        var buffer = new SkimmerActivityBuffer();
        buffer.Add(RbnLines.Line("IU2DEMO", "OK4YL-#", 14045.1, M32.AddSeconds(5)));
        buffer.Add(RbnLines.Line("K1ABC", "OK4YL-#", 14025.0, M32.AddSeconds(40)));
        buffer.Add(RbnLines.Line("K1ABC", "OK4YL-#", 7025.0, M32.AddSeconds(41)));
        buffer.Add(RbnLines.Line("K1ABC", "KM3T-2-#", 14025.0, M32.AddSeconds(59)));
        buffer.Add(RbnLines.Line("K1ABC", "OK4YL-#", 14025.0, M32.AddSeconds(61)));
        buffer.Add(RbnLines.Line("K1ABC", "OK4YL-#", 5000.0, M32)); // outside the bands

        var rows = buffer.Drain().OrderBy(r => (r.Skimmer, r.Band, r.Minute)).ToArray();

        Assert.Equal(
            [
                new ActivityMinute("KM3T-2", "20m", M32, 1, M32.AddSeconds(59)),
                new ActivityMinute("OK4YL", "20m", M32, 2, M32.AddSeconds(40)),
                new ActivityMinute("OK4YL", "20m", M32.AddMinutes(1), 1, M32.AddSeconds(61)),
                new ActivityMinute("OK4YL", "40m", M32, 1, M32.AddSeconds(41)),
            ],
            rows);
        Assert.Empty(buffer.Drain());
    }

    [Fact]
    public async Task The_feed_is_read_for_good_and_the_counts_written_at_every_flush()
    {
        var feed = new FakeRbnFeed { Preloaded = [RbnLines.Line("A1A", at: M32), RbnLines.Line("B1B", at: M32)] };
        var store = new FakeActivityStore();
        var record = new RecordSkimmerActivity(feed, store, NullLogger<RecordSkimmerActivity>.Instance);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var running = record.RunAsync(TimeSpan.FromMilliseconds(50), stop.Token);
        for (var i = 0; i < 100 && store.Rows.Count == 0; i++) await Task.Delay(20, Ct);
        Assert.Equal(1, feed.Readers);
        await stop.CancelAsync();
        await running;

        Assert.Equal(new ActivityMinute("OK4YL", "20m", M32, 2, M32), store.Rows.Single());
    }

    [Fact]
    public async Task Counts_that_cannot_be_written_are_kept_for_the_next_flush()
    {
        var feed = new FakeRbnFeed { Preloaded = [RbnLines.Line("A1A", at: M32)] };
        var store = new FakeActivityStore { Failure = new InvalidOperationException("db down") };
        var record = new RecordSkimmerActivity(feed, store, NullLogger<RecordSkimmerActivity>.Instance);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var running = record.RunAsync(TimeSpan.FromMilliseconds(30), stop.Token);
        await Task.Delay(150, Ct);
        store.Failure = null;
        for (var i = 0; i < 100 && store.Rows.Count == 0; i++) await Task.Delay(20, Ct);
        await stop.CancelAsync();
        await running;

        Assert.Equal(1, store.Rows.Single().Spots);
    }
}
