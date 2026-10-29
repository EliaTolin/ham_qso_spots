using System.Net;
using System.Net.Sockets;

namespace HamQso.Spots.Application.Proxy;

/// <summary>
/// The key a client address counts under: IPv4 as it is (an IPv4-mapped IPv6
/// address is its IPv4), native IPv6 by its /64 (one home or phone usually has
/// a whole /64, so per address it could get a new limit on every call).
/// </summary>
public static class AddressKey
{
    public static string Of(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
