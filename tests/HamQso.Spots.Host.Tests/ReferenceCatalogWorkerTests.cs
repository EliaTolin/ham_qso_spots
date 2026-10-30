using HamQso.Spots.Application.Catalog;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.References;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Host.Tests;

/// <summary>F53.3: the catalog is checked at start and every few hours; a failure is tried again later, never a crash.</summary>
public sealed class ReferenceCatalogWorkerTests
{
    [Fact]
    public async Task At_start_it_builds_the_file_and_a_failure_is_retried_at_the_next_check()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero));
        var source = new FlakySource();
        var current = new CurrentReferenceCatalog();
        var refresh = new RefreshReferenceCatalog(source, new MemoryStore(), current, time) { MinimumCount = 1 };
        using var worker = new ReferenceCatalogWorker(refresh, time, NullLogger<ReferenceCatalogWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => source.Reads == 1);
        Assert.Null(current.File);

        time.Advance(ReferenceCatalogWorker.CheckEvery);
        await WaitFor(() => current.File is not null);
        Assert.Equal(1, current.File!.Count);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private sealed class FlakySource : IReferenceCatalogSource
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<CatalogReference>> ReadActiveAsync(CancellationToken cancellationToken)
        {
            if (++Reads == 1) throw new HttpRequestException("Supabase down");
            return Task.FromResult<IReadOnlyList<CatalogReference>>([new CatalogReference("IT-0001", "POTA", "Park", null, null, null, null, null, null, null)]);
        }
    }

    private sealed class MemoryStore : IReferenceCatalogStore
    {
        private ReferenceCatalogFile? file;

        public Task<ReferenceCatalogFile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(file);

        public Task SaveAsync(ReferenceCatalogFile saved, CancellationToken cancellationToken)
        {
            file = saved;
            return Task.CompletedTask;
        }
    }
}
