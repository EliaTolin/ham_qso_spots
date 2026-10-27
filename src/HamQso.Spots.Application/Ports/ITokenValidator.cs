namespace HamQso.Spots.Application.Ports;

/// <summary>Checks the Supabase access token an app sends as MQTT password.</summary>
public interface ITokenValidator
{
    Task<bool> IsValidAsync(string token, CancellationToken cancellationToken);
}
