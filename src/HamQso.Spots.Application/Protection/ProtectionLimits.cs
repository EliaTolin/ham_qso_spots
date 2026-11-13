namespace HamQso.Spots.Application.Protection;

/// <summary>"Mai invii in massa" (F107): how much and how often the spots server may write to the backend.</summary>
public sealed record ProtectionLimits
{
    /// <summary>At most one batch of uses this often.</summary>
    public TimeSpan BatchEvery { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The backend refuses more (413).</summary>
    public int MaxUsesPerBatch { get; init; } = 500;

    /// <summary>The backend refuses more than 256 KB; this leaves room.</summary>
    public int MaxBatchBytes { get; init; } = 200 * 1024;

    /// <summary>Uses waiting for a batch; the rest is dropped.</summary>
    public int MaxQueue { get; init; } = 2000;

    /// <summary>Health goes out at least this often, even with no uses.</summary>
    public TimeSpan HealthEvery { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>At most one failure report for dropped uses in this time.</summary>
    public TimeSpan OverflowReportEvery { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan RetryFrom { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryUpTo { get; init; } = TimeSpan.FromMinutes(5);
}
