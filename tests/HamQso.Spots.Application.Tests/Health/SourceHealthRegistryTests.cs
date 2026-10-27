using HamQso.Spots.Application.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Health;

public class SourceHealthRegistryTests
{
    [Fact]
    public void A_source_is_failing_until_it_succeeds_again()
    {
        var time = new FakeTimeProvider();
        var registry = new SourceHealthRegistry(time, NullLogger<SourceHealthRegistry>.Instance);
        registry.Register("DXFun");

        registry.Failed("DXFun", "closed");
        var failing = registry.Snapshot().Single();
        time.Advance(TimeSpan.FromSeconds(1));
        registry.Succeeded("DXFun");
        var back = registry.Snapshot().Single();

        Assert.Equal((true, "closed"), (failing.IsFailing, failing.LastError));
        Assert.False(back.IsFailing);
    }

    [Fact]
    public void Sources_are_listed_by_name_and_a_new_one_starts_without_state()
    {
        var registry = new SourceHealthRegistry(new FakeTimeProvider(), NullLogger<SourceHealthRegistry>.Instance);
        registry.Register("WWFF");
        registry.Succeeded("DXFun");
        registry.Failed("POTA", "timeout");

        var snapshot = registry.Snapshot();
        Assert.Equal(["DXFun", "POTA", "WWFF"], snapshot.Select(s => s.Name));
        Assert.Null(snapshot[2].LastSuccess);
        Assert.False(snapshot[2].IsFailing);
    }

    [Fact]
    public void Nobody_reads_the_null_health()
    {
        NullSourceHealth.Instance.Succeeded("x");
        NullSourceHealth.Instance.Failed("x", "y");
    }
}
