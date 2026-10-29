namespace HamQso.Spots.Application.Ports;

/// <summary>Checks the Supabase access token an app sends (MQTT password, REST Bearer).</summary>
public interface ITokenValidator
{
    /// <summary>The user of a valid token, or null.</summary>
    Task<TokenUser?> ValidateAsync(string token, CancellationToken cancellationToken);
}

/// <summary>
/// Who a valid token belongs to: the Supabase user id ("sub"), whether the
/// token comes from a reader project (HamQRG), whose users may only read, and
/// whether the user is anonymous (claim "is_anonymous": no signed-in account).
/// </summary>
public sealed record TokenUser(string UserId, bool IsReader, bool IsAnonymous);
