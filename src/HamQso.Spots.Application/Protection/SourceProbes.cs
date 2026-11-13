using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Protection;

namespace HamQso.Spots.Application.Protection;

/// <summary>The RBN is a source of the "skimmer" signal while its connection is up.</summary>
public sealed class SkimmerProbe(IRbnFeed feed) : IProtectionProbe
{
    public ProtectionSignal Signal => ProtectionSignal.Skimmer;

    public string? Problem() => feed.Status.State == RbnFeedState.Up ? null : $"RBN connection {feed.Status.State.ToString().ToLowerInvariant()}";
}

/// <summary>The cluster side ("spot") works while at least one spot source is not failing.</summary>
public sealed class SpotProbe(IEnumerable<ISpotSource> sources, SourceHealthRegistry health) : IProtectionProbe
{
    private readonly string[] names = [.. sources.Select(s => s.Name)];

    public ProtectionSignal Signal => ProtectionSignal.Spot;

    public string? Problem()
    {
        if (names.Length == 0) return "no spot source";
        var snapshot = health.Snapshot().Where(s => names.Contains(s.Name)).ToArray();
        return snapshot.Length > 0 && snapshot.All(s => s.IsFailing) ? "every spot source is failing" : null;
    }
}
