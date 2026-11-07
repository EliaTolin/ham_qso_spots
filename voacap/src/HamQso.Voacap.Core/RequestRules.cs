using System.Text.RegularExpressions;

namespace HamQso.Voacap.Core;

/// <summary>What the service accepts (plan F71, 3.3): the error code of a request, or null.</summary>
public static partial class RequestRules
{
    public const int MaxReceivers = 600;
    public const int MaxScenarios = 4;
    public const string Invalid = "invalid_request";
    public const string TooManyReceivers = "too_many_receivers";

    public static string? Check(PredictRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Receivers is null || r.Scenarios is null || r.Settings is null || r.Transmitter is null || r.ReceiveAntenna is null) return Invalid;
        if (r.Receivers.Count > MaxReceivers) return TooManyReceivers;
        var ok = Time(r.Month, r.UtcHour)
            && r.FrequencyMhz is >= 1.8 and <= 30
            && r.Settings is { Method: 30, NoiseDbw: >= -170 and <= -100, MinTakeoffDeg: >= 0.1 and <= 40, RequiredSnrDbhz: >= -20 and <= 100 }
            && Point(r.Transmitter.Lat, r.Transmitter.Lon)
            && Antenna(r.ReceiveAntenna)
            && r.Scenarios.Count is >= 1 and <= MaxScenarios
            && r.Scenarios.All(s => s is not null && Id().IsMatch(s.Id ?? "") && Ssn(s.Ssn) && s.PowerW is > 0 and <= 100_000 && Antenna(s.Antenna))
            && r.Scenarios.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == r.Scenarios.Count
            && r.Receivers.Count >= 1
            && r.Receivers.All(x => x is not null && x.Id is { Length: >= 1 and <= 32 } && Point(x.Lat, x.Lon))
            && r.Receivers.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() == r.Receivers.Count;
        return ok ? null : Invalid;
    }

    public static string? Check(EffectiveSsnRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var ok = Time(r.Month, r.UtcHour)
            && r.Ionosonde is not null && Point(r.Ionosonde.Lat, r.Ionosonde.Lon)
            && r.MeasuredMufdMhz is >= 1 and <= 60
            && Ssn(r.SsnMonth);
        return ok ? null : Invalid;
    }

    private static bool Time(int month, int hour) => month is >= 1 and <= 12 && hour is >= 0 and <= 23;

    private static bool Point(double lat, double lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    private static bool Ssn(double ssn) => ssn is >= 0 and <= SsnSearch.Max;

    private static bool Antenna(AntennaSpec? a) =>
        a is not null && AntennaFiles.IsKnown(a.Model) && a.GainDbi is null or (>= -20 and <= 30) && a.HeightM is null or (> 0 and <= 300);

    [GeneratedRegex("^[a-z0-9_-]{1,20}$")]
    private static partial Regex Id();
}
