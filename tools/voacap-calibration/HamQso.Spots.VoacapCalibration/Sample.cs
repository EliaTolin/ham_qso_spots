namespace HamQso.Spots.VoacapCalibration;

/// <summary>The events used: at most n per band and day or night, taken in turn from each station (so a few busy stations do not fill it), repeatable with the seed.</summary>
public static class Sample
{
    public static IReadOnlyList<CalibrationEvent> Stratified(IReadOnlyList<CalibrationEvent> events, int perStratum, int seed)
    {
        ArgumentNullException.ThrowIfNull(events);
        var random = new Random(seed);
        var picked = new List<CalibrationEvent>();
        foreach (var stratum in events.GroupBy(e => (e.Band, Day: CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon))).OrderBy(g => g.Key.Band, StringComparer.Ordinal).ThenBy(g => g.Key.Day))
        {
            var queues = stratum.GroupBy(e => e.Station).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Queue<CalibrationEvent>(g.OrderBy(e => e.Start).OrderBy(_ => random.Next())))
                .OrderBy(_ => random.Next())
                .ToList();
            var taken = 0;
            while (taken < perStratum && queues.Count > 0)
            {
                foreach (var q in queues.ToArray())
                {
                    if (taken == perStratum) break;
                    picked.Add(q.Dequeue());
                    taken++;
                    if (q.Count == 0) queues.Remove(q);
                }
            }
        }
        return picked;
    }
}
