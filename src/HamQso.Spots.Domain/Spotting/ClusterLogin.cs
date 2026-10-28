using System.Globalization;

namespace HamQso.Spots.Domain.Spotting;

/// <summary>
/// The telnet login of a user who spots through the server: the callsign of
/// their profile with the SSID of their install (<c>IU4VRB-7</c>), as the app
/// logs in. The SSID keeps two installs of the same user apart on the node.
/// </summary>
public static class ClusterLogin
{
    public const int MinSsid = 1;

    public const int MaxSsid = 99;

    public static bool IsValidSsid(int? ssid) => ssid is >= MinSsid and <= MaxSsid;

    public static string Create(string callsign, int ssid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callsign);
        ArgumentOutOfRangeException.ThrowIfLessThan(ssid, MinSsid);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ssid, MaxSsid);
        return $"{callsign.Trim().ToUpperInvariant()}-{ssid.ToString(CultureInfo.InvariantCulture)}";
    }
}
