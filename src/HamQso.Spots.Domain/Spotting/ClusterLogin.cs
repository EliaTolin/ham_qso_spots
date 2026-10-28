using System.Globalization;
using System.Text.RegularExpressions;

namespace HamQso.Spots.Domain.Spotting;

/// <summary>
/// The telnet login of a user who spots through the server: the callsign of
/// their profile with the SSID of their install (<c>IU4VRB-7</c>), as the app
/// logs in. The SSID keeps two installs of the same user apart on the node.
/// </summary>
public static partial class ClusterLogin
{
    public const int MinSsid = 1;

    public const int MaxSsid = 99;

    public static bool IsValidSsid(int? ssid) => ssid is >= MinSsid and <= MaxSsid;

    /// <summary>
    /// The callsign as it goes in the login (trimmed, uppercase), or null when it
    /// is not one: the same pattern as the check on <c>public.profiles.callsign</c>,
    /// checked again here so nothing else ever reaches the node as a login.
    /// </summary>
    public static string? NormalizeCallsign(string? callsign) =>
        callsign?.Trim().ToUpperInvariant() is { } c && Callsign().IsMatch(c) ? c : null;

    public static string Create(string callsign, int ssid)
    {
        var call = NormalizeCallsign(callsign) ?? throw new ArgumentException($"'{callsign}' is not a callsign", nameof(callsign));
        ArgumentOutOfRangeException.ThrowIfLessThan(ssid, MinSsid);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ssid, MaxSsid);
        return $"{call}-{ssid.ToString(CultureInfo.InvariantCulture)}";
    }

    // \z, not $: $ would let a final newline through.
    [GeneratedRegex(@"^[A-Z0-9]{1,8}(?:/[A-Z0-9]{1,8}){0,2}\z")]
    private static partial Regex Callsign();
}
