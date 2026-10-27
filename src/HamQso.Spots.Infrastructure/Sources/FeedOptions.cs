namespace HamQso.Spots.Infrastructure.Sources;

/// <summary>Configuration section "Feeds": which HTTP feeds run and how often (keys: HamQTH, DXSummit, POTA, SOTA, WWFF).</summary>
public sealed class FeedOptions
{
    public const string Section = "Feeds";

    public bool Enabled { get; set; } = true;

    /// <summary>Null: the feed's default (<see cref="SpotSourceFactory.Feeds"/>).</summary>
    public TimeSpan? Interval { get; set; }
}
