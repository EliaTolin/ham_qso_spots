using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.VoacapCalibration;

/// <summary>
/// A real CQ (CW) of a station whose locator is known (it is also an RBN node), in a
/// 10-minute slot on one band: the skimmers listening then (the server's rule,
/// <see cref="SkimmerActivity"/>) and those that heard it.
/// </summary>
public sealed record CalibrationEvent(
    string Station,
    string StationLocator,
    SkimmerPosition StationPosition,
    string Band,
    double FrequencyMhz,
    DateTimeOffset Slot,
    IReadOnlyList<SkimmerInfo> Listening,
    IReadOnlyList<string> Heard);

public static class CalibrationEvents
{
    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<CalibrationEvent> Build(IReadOnlyList<HistorySpot> spots, LocateSkimmer locate)
    {
        ArgumentNullException.ThrowIfNull(spots);
        ArgumentNullException.ThrowIfNull(locate);
        var activity = spots
            .GroupBy(s => (Skimmer: RbnSpotLine.SkimmerCall(s.Skimmer), s.Band, Minute: SkimmerActivity.MinuteOf(s.At)))
            .Select(g => new ActivityMinute(g.Key.Skimmer, g.Key.Band, g.Key.Minute, g.Count(), g.Max(s => s.At)))
            .ToLookup(m => m.Band);
        var events = new List<CalibrationEvent>();
        foreach (var cq in spots.Where(s => s.Kind == "CQ" && s.TxMode == "CW").GroupBy(s => (Station: Callsign.Base(s.Dx), s.Band, Slot: SlotOf(s.At))))
        {
            var station = locate.Locate(cq.Key.Station);
            if (station.Position is not { Source: SkimmerPosition.Node } at) continue;
            var window = new ActivityWindow(cq.Key.Slot - SkimmerActivity.Lead, cq.Key.Slot + SlotLength - TimeSpan.FromTicks(1));
            var listening = SkimmerActivity.Listening(activity[cq.Key.Band], cq.Key.Band, window)
                .Where(l => Callsign.Base(l.Call) != cq.Key.Station)
                .Select(l => locate.Locate(l.Call))
                .Where(l => l.Position is not null)
                .ToArray();
            if (listening.Length == 0) continue;
            var heard = cq.Select(s => RbnSpotLine.SkimmerCall(s.Skimmer)).Where(h => listening.Any(l => l.Call == h)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            events.Add(new CalibrationEvent(cq.Key.Station, at.Locator!, at, cq.Key.Band, cq.Average(s => s.Khz) / 1000, cq.Key.Slot, listening, heard));
        }
        return events;
    }

    public static DateTimeOffset SlotOf(DateTimeOffset at)
    {
        var m = SkimmerActivity.MinuteOf(at);
        return m.AddMinutes(-(m.Minute % (int)SlotLength.TotalMinutes));
    }
}
