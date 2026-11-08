using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Ingestion;

/// <summary>
/// Which of the spots just accepted go to the alerts: every new spot, and a
/// respot for every new spotter of a merged duplicate (F101: one push per person
/// who spots you; Supabase decides who gets it and drops the skimmers).
/// </summary>
public static class AlertableSpots
{
    /// <summary>Older spots (a telnet backlog after a reconnect) and QRT spots (B29) never trigger an alert.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(15);

    /// <summary>The rows for the alerts, in the order the spots arrived.</summary>
    public static AlertSpot[] Of(IEnumerable<(Spot Incoming, DedupResult Result)> accepted, DateTimeOffset now) =>
        accepted.SelectMany(a => Rows(a.Incoming, a.Result)).Where(a => IsFresh(a.Spot, now)).ToArray();

    private static IEnumerable<AlertSpot> Rows(Spot incoming, DedupResult result) => result.Outcome switch
    {
        DedupOutcome.New => [new AlertSpot(result.Spot, Respot: false)],
        DedupOutcome.Merged => result.NewSpotters.Select(spotter => new AlertSpot(Respot(result.Spot, incoming, spotter), Respot: true)),
        _ => [],
    };

    /// <summary>
    /// The stored spot (same spot_key) as spotted by <paramref name="spotter"/>: with
    /// the time and, when it has one, the comment of the incoming spot.
    /// </summary>
    public static Spot Respot(Spot merged, Spot incoming, string spotter)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(incoming);
        return merged with
        {
            Spotters = [spotter],
            SpotTime = incoming.SpotTime,
            Comment = string.IsNullOrWhiteSpace(incoming.Comment) ? merged.Comment : incoming.Comment,
            Qrt = merged.Qrt || incoming.Qrt,
        };
    }

    public static bool IsFresh(Spot spot, DateTimeOffset now) => !spot.Qrt && now - spot.SpotTime <= Freshness;
}
