namespace HamQso.Spots.Domain.Protection;

/// <summary>Where a use of a protected callsign was seen (F107). The wire names are the backend's.</summary>
public enum ProtectionSignal
{
    Spot,
    Skimmer,
    PskReporter,
}

public static class ProtectionSignals
{
    public static IReadOnlyList<ProtectionSignal> All { get; } = [ProtectionSignal.Spot, ProtectionSignal.Skimmer, ProtectionSignal.PskReporter];

    /// <summary>"spot", "skimmer", "pskreporter": what callsign_protection takes.</summary>
    public static string Wire(this ProtectionSignal signal) => signal switch
    {
        ProtectionSignal.Spot => "spot",
        ProtectionSignal.Skimmer => "skimmer",
        ProtectionSignal.PskReporter => "pskreporter",
        _ => throw new ArgumentOutOfRangeException(nameof(signal)),
    };
}

/// <summary>One time somebody heard a protected callsign: a spot, a skimmer line or a PSKReporter report.</summary>
public sealed record Hearing(
    string Callsign,
    ProtectionSignal Signal,
    DateTimeOffset At,
    string Band,
    double? FrequencyKhz,
    string? Mode,
    string HeardBy,
    string? HeardLocator,
    int? SnrDb);

/// <summary>
/// The use sent to the backend: all the hearings of one callsign on one band
/// from one signal in one minute (<see cref="UseAggregator"/>).
/// <see cref="SourceId"/> is fixed by what the use is, so a retry sends the same key.
/// </summary>
public sealed record ProtectedUse(
    string Callsign,
    ProtectionSignal Signal,
    DateTimeOffset UsedAt,
    string Band,
    double? FrequencyKhz,
    string? Mode,
    string HeardBy,
    string? HeardLocator,
    IReadOnlyDictionary<string, object?> Raw,
    string SourceId);
