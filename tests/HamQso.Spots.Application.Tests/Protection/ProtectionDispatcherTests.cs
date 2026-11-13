using System.Net;
using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Domain.Protection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Protection;

public class ProtectionDispatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(T0);
    private readonly UseAggregator aggregator = new();
    private readonly FakeUplink uplink = new();
    private readonly RecordingHealth health = new();
    private readonly List<FakeProbe> probes = [new(ProtectionSignal.Spot), new(ProtectionSignal.Skimmer), new(ProtectionSignal.PskReporter)];

    private ProtectionDispatcher Dispatcher(ProtectionLimits? limits = null) =>
        new(aggregator, uplink, probes, health, time, limits ?? new ProtectionLimits(), NullLogger<ProtectionDispatcher>.Instance);

    private void Hear(string by, string band = "20m", int secondsIntoMinute = 5, string call = "IN3FOB")
    {
        var at = SkimmerActivityMinute(time.GetUtcNow()).AddSeconds(secondsIntoMinute);
        aggregator.Add(call, new Hearing(call, ProtectionSignal.PskReporter, at, band, 14074.5, "FT8", by, "JO40", -10));
    }

    private static DateTimeOffset SkimmerActivityMinute(DateTimeOffset t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, TimeSpan.Zero);

    private async Task RunAsync(ProtectionDispatcher d, TimeSpan duration, TimeSpan? step = null)
    {
        var end = time.GetUtcNow() + duration;
        while (time.GetUtcNow() < end)
        {
            await d.TickAsync(CancellationToken.None);
            time.Advance(step ?? TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task C5_a_batch_has_at_most_500_uses_and_one_batch_goes_out_every_30_seconds()
    {
        var d = Dispatcher();
        await d.TickAsync(CancellationToken.None); // the first health report
        uplink.Batches.Clear();
        for (var i = 0; i < 1200; i++) Hear($"R{i}", band: "20m", call: $"IN{i}FOB");
        time.Advance(TimeSpan.FromMinutes(1));

        await RunAsync(d, TimeSpan.FromSeconds(100));

        Assert.All(uplink.Batches, b => Assert.True(b.Uses.Count <= 500));
        Assert.Equal(3, uplink.Batches.Count(b => b.Uses.Count > 0));
    }

    [Fact]
    public async Task C5_the_body_stays_under_256_KB_even_with_uses_at_their_largest()
    {
        var d = Dispatcher();
        await d.TickAsync(CancellationToken.None);
        uplink.Batches.Clear();
        for (var i = 0; i < 500; i++)
        {
            for (var r = 0; r < 12; r++) Hear($"LONGCALL{r}-#", call: $"IN{i}FOB");
        }
        time.Advance(TimeSpan.FromMinutes(1));

        await RunAsync(d, TimeSpan.FromMinutes(5));

        Assert.All(uplink.Batches, b => Assert.True(ProtectionPayload.Serialize(b).Length < 256 * 1024));
        Assert.Equal(500, uplink.Batches.Sum(b => b.Uses.Count));
    }

    [Fact]
    public async Task Load_10000_PSKReporter_reports_in_a_minute_for_one_callsign_are_at_most_2_batches_and_one_use_per_minute_per_band()
    {
        var d = Dispatcher();
        await d.TickAsync(CancellationToken.None);
        uplink.Batches.Clear();
        var bands = new[] { "40m", "20m", "15m" };
        for (var i = 0; i < 10_000; i++) Hear($"RX{i % 400}", bands[i % 3], secondsIntoMinute: i % 55);

        await RunAsync(d, TimeSpan.FromMinutes(4));

        var withUses = uplink.Batches.Where(b => b.Uses.Count > 0).ToArray();
        Assert.True(withUses.Length <= 2, $"{withUses.Length} batches");
        var uses = withUses.SelectMany(b => b.Uses).ToArray();
        Assert.Equal(3, uses.Length);
        Assert.Equal(bands.Order(), uses.Select(u => u.Band).Order());
        Assert.All(uses, u => Assert.Equal(400, u.Raw["receivers"]));
        Assert.All(uses, u => Assert.True(((Array)u.Raw["heard"]!).Length <= 10));
        Assert.All(withUses, b => Assert.True(JsonSerializer.SerializeToUtf8Bytes(b.Uses.Select(x => x.Raw)).Length < 3 * 2000));
    }

    [Fact]
    public async Task Overflow_is_dropped_and_reported_once_an_hour_not_once_per_use()
    {
        var d = Dispatcher(new ProtectionLimits { MaxQueue = 10 });
        for (var i = 0; i < 100; i++) Hear("RX", call: $"IN{i}FOB");
        time.Advance(TimeSpan.FromMinutes(1));
        await d.TickAsync(CancellationToken.None);
        for (var i = 0; i < 100; i++) Hear("RX", band: "40m", call: $"IN{i}FOB");
        time.Advance(TimeSpan.FromMinutes(1));
        await d.TickAsync(CancellationToken.None);

        Assert.Single(health.Calls, c => !c.Ok && c.Source == ProtectionDispatcher.HealthName && c.Reason!.Contains("dropped", StringComparison.Ordinal));
        Assert.True(d.Queued <= 10);
    }

    [Fact]
    public async Task A_batch_that_fails_with_a_server_error_is_retried_with_the_same_source_ids_after_a_backoff()
    {
        var d = Dispatcher();
        await d.TickAsync(CancellationToken.None);
        uplink.Batches.Clear();
        Hear("RX1");
        time.Advance(TimeSpan.FromMinutes(1));
        uplink.Next.Enqueue(new HttpRequestException("500"));

        await d.TickAsync(CancellationToken.None);
        var first = uplink.Batches.Single();
        await d.TickAsync(CancellationToken.None); // same instant: backing off
        Assert.Single(uplink.Batches);
        time.Advance(TimeSpan.FromSeconds(31));
        await d.TickAsync(CancellationToken.None);

        Assert.Equal(2, uplink.Batches.Count);
        Assert.Equal(first.Uses.Select(u => u.SourceId), uplink.Batches[1].Uses.Select(u => u.SourceId));
        Assert.Same(first.Uses[0], uplink.Batches[1].Uses[0]);
        Assert.Matches(@"^pskreporter:IN3FOB:20m:\d+$", first.Uses[0].SourceId);
    }

    [Fact]
    public async Task A_4xx_is_never_retried_and_the_batch_is_dropped()
    {
        var d = Dispatcher();
        await d.TickAsync(CancellationToken.None);
        uplink.Batches.Clear();
        Hear("RX1");
        time.Advance(TimeSpan.FromMinutes(1));
        uplink.Next.Enqueue(new UplinkRejectedException(400, "bad"));

        await RunAsync(d, TimeSpan.FromMinutes(2));

        Assert.Single(uplink.Batches, b => b.Uses.Count > 0);
        Assert.Contains(health.Calls, c => !c.Ok && c.Source == ProtectionDispatcher.HealthName);
    }

    [Fact]
    public async Task Health_goes_out_at_least_every_5_minutes_even_without_uses_with_checked_and_failed_sources()
    {
        probes[2].Problem = "not connected";
        var d = Dispatcher();

        await RunAsync(d, TimeSpan.FromMinutes(11));

        Assert.InRange(uplink.Batches.Count, 3, 4);
        Assert.All(uplink.Batches, b => Assert.Empty(b.Uses));
        var batch = uplink.Batches[0];
        Assert.Equal(["spot", "skimmer"], batch.Checked);
        Assert.Equal([new SourceFailure("pskreporter", "not connected")], batch.Failed);
        Assert.Contains(health.Calls, c => c is { Source: "protection_pskreporter", Ok: false });
    }

    [Fact]
    public async Task The_body_is_the_contract_of_callsign_protection()
    {
        Hear("RX1");
        time.Advance(TimeSpan.FromMinutes(1));
        var d = Dispatcher();
        await d.TickAsync(CancellationToken.None);

        using var json = JsonDocument.Parse(ProtectionPayload.Serialize(uplink.Batches.Single()));
        var use = json.RootElement.GetProperty("uses")[0];
        Assert.Equal("IN3FOB", use.GetProperty("callsign").GetString());
        Assert.Equal("pskreporter", use.GetProperty("signal").GetString());
        Assert.Equal("2026-11-13T03:00:05Z", use.GetProperty("used_at").GetString());
        Assert.Equal("20m", use.GetProperty("band").GetString());
        Assert.Equal(14074.5, use.GetProperty("frequency_khz").GetDouble());
        Assert.Equal("RX1", use.GetProperty("heard_by").GetString());
        Assert.Equal("JO40", use.GetProperty("heard_locator").GetString());
        Assert.Equal(1, use.GetProperty("raw").GetProperty("receivers").GetInt32());
        Assert.StartsWith("pskreporter:IN3FOB:20m:", use.GetProperty("source_id").GetString(), StringComparison.Ordinal);
        Assert.Equal(["spot", "skimmer", "pskreporter"], json.RootElement.GetProperty("checked").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, json.RootElement.GetProperty("failed").GetArrayLength());
    }
}
