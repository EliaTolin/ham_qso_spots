using System.Globalization;
using System.Text.Json;

namespace HamQso.Spots.VoacapCalibration;

/// <summary>
/// The monthly SSN of the "theory" for the days of the history: NOAA SWPC's own
/// observed number (observed_swpc_ssn, public domain; never the SILSO "ssn", which
/// is non-commercial) where the month is over, else NOAA's prediction (predicted_ssn).
/// </summary>
public static class SsnByMonth
{
    public static IReadOnlyDictionary<(int Year, int Month), double> From(string observedJson, string predictedJson)
    {
        var map = new Dictionary<(int, int), double>();
        foreach (var e in JsonElement.Parse(predictedJson).EnumerateArray())
        {
            if (e.TryGetProperty("predicted_ssn", out var v) && v.ValueKind == JsonValueKind.Number) map[Month(e)] = v.GetDouble();
        }
        foreach (var e in JsonElement.Parse(observedJson).EnumerateArray())
        {
            if (e.TryGetProperty("observed_swpc_ssn", out var v) && v.ValueKind == JsonValueKind.Number && v.GetDouble() >= 0) map[Month(e)] = v.GetDouble();
        }
        return map;
    }

    private static (int, int) Month(JsonElement e)
    {
        var t = DateTime.ParseExact(e.GetProperty("time-tag").GetString()!, "yyyy-MM", CultureInfo.InvariantCulture);
        return (t.Year, t.Month);
    }
}
