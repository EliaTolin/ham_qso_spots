using System.Security.Cryptography;
using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Access;

/// <summary>What a client asks the broker for (mosquitto-go-auth "acc").</summary>
public enum MqttAccessKind
{
    Read = 1,
    Write = 2,
    ReadWrite = 3,
    Subscribe = 4,
}

/// <summary>
/// Who may use the broker: only HamQSO users (a valid Supabase token as
/// password) read the spots; only the server's own account writes. Mosquitto
/// asks at connect (<see cref="AuthenticateAsync"/>) and for every topic
/// (<see cref="Authorize"/>).
/// </summary>
public sealed class MqttAccess(ITokenValidator tokens, string publisherUsername, string publisherPassword)
{
    private const string SpotsFilter = SpotTopics.Root + "/";

    public async Task<bool> AuthenticateAsync(string? username, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;
        // The publisher name is reserved: it never logs in with a token.
        if (username == publisherUsername) return publisherPassword.Length > 0 && FixedTimeEquals(password, publisherPassword);
        return await tokens.ValidateAsync(password, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>Called only for clients that passed <see cref="AuthenticateAsync"/>.</summary>
    public bool Authorize(string? username, string? topic, MqttAccessKind access)
    {
        if (string.IsNullOrEmpty(topic) || !(topic == SpotTopics.Root || topic.StartsWith(SpotsFilter, StringComparison.Ordinal))) return false;
        if (username == publisherUsername) return true;
        return access is MqttAccessKind.Read or MqttAccessKind.Subscribe;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
