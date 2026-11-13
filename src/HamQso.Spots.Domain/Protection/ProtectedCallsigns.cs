using HamQso.Spots.Domain.Callsigns;

namespace HamQso.Spots.Domain.Protection;

/// <summary>
/// The callsigns under protection, by base callsign: a hearing of IN3FOB/P or
/// DL/IN3FOB belongs to IN3FOB (<see cref="Callsign.Base"/>, the backend's rule).
/// </summary>
public sealed class ProtectedCallsigns
{
    public static readonly ProtectedCallsigns None = new([]);

    private readonly HashSet<string> bases;

    public ProtectedCallsigns(IEnumerable<string> callsigns)
    {
        ArgumentNullException.ThrowIfNull(callsigns);
        bases = [.. callsigns.Where(Callsign.IsPlausible).Select(Callsign.Base)];
    }

    public int Count => bases.Count;

    public IReadOnlyCollection<string> Bases => bases;

    /// <summary>The protected base callsign <paramref name="heard"/> belongs to, or null.</summary>
    public string? Match(string? heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return null;
        var b = Callsign.Base(heard.Trim());
        return bases.Contains(b) ? b : null;
    }

    /// <summary>
    /// The call of a spotter without its RBN "-#" or SSID ("DF7GB-#" and "DF7GB-2" are "DF7GB"): for the
    /// self-spot check.
    /// </summary>
    public static string StationOf(string spotter)
    {
        ArgumentNullException.ThrowIfNull(spotter);
        var call = spotter.Trim().ToUpperInvariant();
        var dash = call.IndexOf('-', StringComparison.Ordinal);
        return Callsign.Base(dash > 0 ? call[..dash] : call);
    }
}
