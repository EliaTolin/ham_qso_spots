namespace HamQso.Spots.Application.Ports;

/// <summary>The HamQSO profile of a user (Supabase public.profiles).</summary>
public interface IProfileReader
{
    /// <summary>The callsign of the profile, or null when the user has none (or no profile).</summary>
    Task<string?> CallsignAsync(string userId, CancellationToken cancellationToken);
}
