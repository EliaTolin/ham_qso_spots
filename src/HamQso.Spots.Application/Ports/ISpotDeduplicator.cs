using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ports;

/// <summary>Remembers recent spots and merges duplicates (rule: <see cref="DuplicateRule"/>).</summary>
public interface ISpotDeduplicator
{
    /// <summary>Remembers spots already handled (from the archive at startup) without reporting them.</summary>
    void Seed(IEnumerable<Spot> spots);

    DedupResult Accept(Spot spot);
}

public enum DedupOutcome
{
    /// <summary>First time seen.</summary>
    New,

    /// <summary>A duplicate that added a feed, a spotter or a mode.</summary>
    Merged,

    /// <summary>A duplicate with nothing new.</summary>
    Unchanged,
}

/// <summary><see cref="Spot"/> is the spot as now known (merged when a duplicate).</summary>
public sealed record DedupResult(DedupOutcome Outcome, Spot Spot);
