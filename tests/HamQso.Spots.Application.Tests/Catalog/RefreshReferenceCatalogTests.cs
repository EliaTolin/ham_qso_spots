using HamQso.Spots.Application.Catalog;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.References;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Catalog;

/// <summary>
/// F53.3: the server rebuilds the catalog file from the references of HamQSO
/// once a week, keeps it across restarts and never replaces a good file with a
/// broken read.
/// </summary>
public sealed class RefreshReferenceCatalogTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero));
    private readonly FakeSource source = new();
    private readonly FakeStore store = new();
    private readonly CurrentReferenceCatalog current = new();

    private RefreshReferenceCatalog Refresh() => new(source, store, current, time) { MinimumCount = 2 };

    private static CatalogReference Ref(string code) => new(code, "POTA", "Park " + code, null, 248, null, 45, 11, null, null);

    [Fact]
    public async Task At_the_first_start_the_file_is_built_saved_and_served()
    {
        source.Rows = [Ref("IT-0001"), Ref("IT-0002")];

        await Refresh().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, current.File!.Count);
        Assert.Same(current.File, store.Saved);
    }

    [Fact]
    public async Task After_a_restart_the_saved_file_is_served_and_not_rebuilt_before_a_week()
    {
        store.Saved = ReferenceCatalogFormat.Write([Ref("IT-0001"), Ref("IT-0002")], time.GetUtcNow().AddDays(-3));

        await Refresh().RunAsync(TestContext.Current.CancellationToken);

        Assert.Same(store.Saved, current.File);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task A_week_later_it_is_rebuilt()
    {
        store.Saved = ReferenceCatalogFormat.Write([Ref("IT-0001"), Ref("IT-0002")], time.GetUtcNow().AddDays(-8));
        source.Rows = [Ref("IT-0001"), Ref("IT-0002"), Ref("IT-0003")];

        await Refresh().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, current.File!.Count);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task Supabase_down_or_a_short_read_keeps_the_file_there_is()
    {
        var old = store.Saved = ReferenceCatalogFormat.Write([Ref("IT-0001"), Ref("IT-0002")], time.GetUtcNow().AddDays(-8));
        source.Rows = [Ref("IT-0001")];
        await Refresh().RunAsync(TestContext.Current.CancellationToken);
        Assert.Same(old, current.File);

        source.Fail = true;
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Refresh().RunAsync(TestContext.Current.CancellationToken));
        Assert.Same(old, current.File);
        Assert.Same(old, store.Saved);
    }

    [Fact]
    public async Task The_same_references_keep_the_file_and_its_ETag()
    {
        var old = store.Saved = ReferenceCatalogFormat.Write([Ref("IT-0001"), Ref("IT-0002")], time.GetUtcNow().AddDays(-8));
        source.Rows = [Ref("IT-0002"), Ref("IT-0001")];

        await Refresh().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(old.ETag, current.File!.ETag);
        // Only the new time is saved, so the next check waits a week.
        Assert.Equal(1, store.Saves);
        Assert.Equal(time.GetUtcNow(), current.File.GeneratedAt);
    }

    private sealed class FakeSource : IReferenceCatalogSource
    {
        public IReadOnlyList<CatalogReference> Rows { get; set; } = [];

        public bool Fail { get; set; }

        public int Reads { get; private set; }

        public async Task<IReadOnlyList<CatalogReference>> ReadActiveAsync(CancellationToken cancellationToken)
        {
            Reads++;
            await Task.Yield();
            if (Fail) throw new HttpRequestException("Supabase down");
            return Rows;
        }
    }

    private sealed class FakeStore : IReferenceCatalogStore
    {
        public ReferenceCatalogFile? Saved { get; set; }

        public int Saves { get; private set; }

        public Task<ReferenceCatalogFile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

        public Task SaveAsync(ReferenceCatalogFile file, CancellationToken cancellationToken)
        {
            Saved = file;
            Saves++;
            return Task.CompletedTask;
        }
    }
}
