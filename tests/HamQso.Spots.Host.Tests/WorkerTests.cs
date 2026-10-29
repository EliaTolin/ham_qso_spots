using System.Runtime.CompilerServices;
using HamQso.Spots.Application.Dedup;
using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ingestion;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Host.Tests;

public sealed class WorkerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Now);
    private readonly FakeStore store = new();
    private readonly SourceHealthRegistry health;
    private readonly IngestSpots ingest;

    public WorkerTests()
    {
        health = new SourceHealthRegistry(time, NullLogger<SourceHealthRegistry>.Instance);
        ingest = new IngestSpots(new InMemorySpotDeduplicator(time), store, new NoPublisher(), new NoAlerts(), time, NullLogger<IngestSpots>.Instance);
    }

    public void Dispose() => ingest.Dispose();

    private static Spot Dx(string call) => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = call,
        Callsign = call,
        FrequencyKhz = 14025,
        SpotTime = Now,
        Feeds = ["DXFun"],
    };

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "condition not reached in 5 s");
    }

    /// <summary>
    /// Moves the fake clock by <paramref name="step"/> until the condition holds:
    /// the worker may not be waiting yet when the first step comes.
    /// </summary>
    private async Task AdvanceUntil(TimeSpan step, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            time.Advance(step);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), "condition not reached in 5 s");
    }

    [Fact]
    public async Task Every_source_runs_and_a_broken_one_shows_in_health_while_the_others_go_on()
    {
        var schema = new FakeSchema();
        ISpotSource[] sources = [new OneBatchSource("DXFun", [Dx("JA1ABC")]), new BrokenSource("HamQTH")];
        using var worker = new IngestionWorker(sources, ingest, schema, health, new NoAlerts(), time, NullLogger<IngestionWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => store.Saved.Count == 1 && health.Snapshot().Any(s => s.Name == "HamQTH" && s.IsFailing));
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, schema.Calls);
        Assert.Equal("JA1ABC", store.Saved[0].Callsign);
        Assert.Contains(health.Snapshot(), s => s.Name == "broker");
        Assert.Contains(health.Snapshot(), s => s.Name == "alerts");
        Assert.Contains(health.Snapshot(), s => s.Name == "archive");
    }

    [Fact]
    public async Task With_the_alerts_off_health_has_no_alerts_entry()
    {
        using var worker = new IngestionWorker([new OneBatchSource("DXFun", [Dx("JA1ABC")])], ingest, new FakeSchema(), health, new NullAlertNotifier(), time, NullLogger<IngestionWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => store.Saved.Count == 1);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(health.Snapshot(), s => s.Name == "alerts");
        Assert.Contains(health.Snapshot(), s => s.Name == "broker");
    }

    [Fact]
    public async Task A_schema_that_cannot_be_created_does_not_stop_the_sources()
    {
        using var worker = new IngestionWorker([new OneBatchSource("DXFun", [Dx("JA1ABC")])], ingest, new FakeSchema { Fail = true }, health, new NoAlerts(), time, NullLogger<IngestionWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => store.Saved.Count == 1);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_source_that_ends_is_started_again_after_10_s()
    {
        var source = new OneBatchSource("DXFun", [Dx("JA1ABC")]);
        using var worker = new IngestionWorker([source], ingest, new FakeSchema(), health, new NoAlerts(), time, NullLogger<IngestionWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => source.Reads == 1);
        await AdvanceUntil(TimeSpan.FromSeconds(10), () => source.Reads == 2);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Retention_deletes_spots_older_than_7_days_at_start_and_every_hour_also_after_an_error()
    {
        using var worker = new RetentionWorker(store, time, NullLogger<RetentionWorker>.Instance);
        store.FailDeletes = 1;

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => store.DeleteCalls.Count == 1);
        await AdvanceUntil(TimeSpan.FromHours(1), () => store.DeleteCalls.Count >= 2);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now - RetentionWorker.Window, store.DeleteCalls[0]);
        Assert.Equal(TimeSpan.FromHours(1), store.DeleteCalls[1] - store.DeleteCalls[0]);
    }

    private sealed class OneBatchSource(string name, Spot[] spots) : ISpotSource
    {
        private int reads;

        public int Reads => Volatile.Read(ref reads);

        public string Name => name;

        public async IAsyncEnumerable<SpotBatch> ReadAsync(ISourceHealth health, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref reads);
            health.Succeeded(name);
            yield return new SpotBatch(name, spots);
            await Task.CompletedTask;
        }
    }

    private sealed class BrokenSource(string name) : ISpotSource
    {
        public string Name => name;

/// <summary>Breaks as soon as it starts, like a node that closes the connection.</summary>
        public async IAsyncEnumerable<SpotBatch> ReadAsync(ISourceHealth health, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!cancellationToken.IsCancellationRequested) throw new InvalidOperationException("node closed the connection");
            yield break;
        }
    }

    private sealed class FakeSchema : ISpotArchiveSchema
    {
        public int Calls { get; private set; }

        public bool Fail { get; init; }

        public Task EnsureSchemaAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? throw new InvalidOperationException("Postgres down") : Task.CompletedTask;
        }
    }

    private sealed class FakeStore : ISpotStore
    {
        private long seq;

        public List<Spot> Saved { get; } = [];

        public List<DateTimeOffset> DeleteCalls { get; } = [];

        public int FailDeletes { get; set; }

        public Task<IReadOnlyList<Spot>> SaveAsync(IReadOnlyCollection<Spot> spots, CancellationToken cancellationToken)
        {
            var saved = spots.Select(s => s with { Seq = ++seq }).ToArray();
            lock (Saved) Saved.AddRange(saved);
            return Task.FromResult<IReadOnlyList<Spot>>(saved);
        }

        public Task<IReadOnlyList<Spot>> LoadSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spot>>([]);

        public Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken)
        {
            lock (DeleteCalls) DeleteCalls.Add(before);
            if (FailDeletes-- > 0) throw new InvalidOperationException("Postgres down");
            return Task.FromResult(0);
        }
    }

    private sealed class NoPublisher : ISpotPublisher
    {
        public Task PublishAsync(string topic, IReadOnlyList<Spot> spots, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoAlerts : IAlertNotifier
    {
        public Task NotifyAsync(IReadOnlyList<Spot> spots, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
