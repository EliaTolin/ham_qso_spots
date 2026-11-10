using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Callsigns;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.VoacapCalibration;

/// <summary>
/// A simulated coverage test from the RBN history: a station whose locator is known
/// (it is also an RBN node) starts calling CQ in CW on a band after at least 15
/// minutes of silence there; the test lasts 3 minutes from its first spot. Listening:
/// the server's rule (<see cref="SkimmerActivity"/>, a spot on the band from 10
/// minutes before), the station itself excluded. Heard: who spotted it in the 3 minutes.
/// </summary>
public sealed record CalibrationEvent(
    string Station,
    string StationLocator,
    SkimmerPosition StationPosition,
    string Band,
    double FrequencyMhz,
    DateTimeOffset Start,
    IReadOnlyList<SkimmerInfo> Listening,
    IReadOnlyList<string> Heard,
    IReadOnlyList<HeardSpot>? HeardSpots = null);

/// <summary>How a skimmer heard the test: the dB of its first spot and the seconds from the start of the test.</summary>
public sealed record HeardSpot(string Call, int SnrDb, int AfterS);

public static class CalibrationEvents
{
    public static readonly TimeSpan TestLength = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan Silence = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<CalibrationEvent> Build(IReadOnlyList<HistorySpot> spots, LocateSkimmer locate)
    {
        ArgumentNullException.ThrowIfNull(spots);
        ArgumentNullException.ThrowIfNull(locate);
        var activity = spots
            .GroupBy(s => (Skimmer: RbnSpotLine.SkimmerCall(s.Skimmer), s.Band, Minute: SkimmerActivity.MinuteOf(s.At)))
            .Select(g => new ActivityMinute(g.Key.Skimmer, g.Key.Band, g.Key.Minute, g.Count(), g.Max(s => s.At)))
            .ToLookup(m => m.Band);
        var events = new List<CalibrationEvent>();
        foreach (var station in spots.GroupBy(s => (Station: Callsign.Base(s.Dx), s.Band)))
        {
            if (locate.Locate(station.Key.Station).Position is not { Source: SkimmerPosition.Node } at) continue;
            var ordered = station.OrderBy(s => s.At).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var first = ordered[i];
                var fresh = i == 0 ? first.At - first.At.UtcDateTime.Date >= Silence : first.At - ordered[i - 1].At >= Silence;
                if (!fresh || first.Kind != "CQ" || first.TxMode != "CW") continue;
                var end = first.At + TestLength;
                var test = ordered.Skip(i).TakeWhile(s => s.At <= end).Where(s => s.Kind == "CQ").ToArray();
                var window = new ActivityWindow(first.At - SkimmerActivity.Lead, end);
                var listening = SkimmerActivity.Listening(activity[station.Key.Band], station.Key.Band, window)
                    .Where(l => Callsign.Base(l.Call) != station.Key.Station)
                    .Select(l => locate.Locate(l.Call))
                    .Where(l => l.Position is not null)
                    .ToArray();
                var heard = test.Select(s => RbnSpotLine.SkimmerCall(s.Skimmer)).Where(h => listening.Any(l => l.Call == h)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (heard.Length == 0) continue;
                HeardSpot[] details = [.. heard.Select(h => test.First(s => RbnSpotLine.SkimmerCall(s.Skimmer) == h))
                    .Select(s => new HeardSpot(RbnSpotLine.SkimmerCall(s.Skimmer), s.Db, (int)(s.At - first.At).TotalSeconds))];
                events.Add(new CalibrationEvent(station.Key.Station, at.Locator!, at, station.Key.Band, test.Average(s => s.Khz) / 1000, first.At, listening, heard, details));
            }
        }
        return events;
    }

    /// <summary>Day at the station: 6 to 18 local solar time (UTC + longitude / 15).</summary>
    public static bool IsDay(DateTimeOffset at, double lon)
    {
        var local = ((at.UtcDateTime.TimeOfDay.TotalHours + lon / 15) % 24 + 24) % 24;
        return local is >= 6 and < 18;
    }
}
