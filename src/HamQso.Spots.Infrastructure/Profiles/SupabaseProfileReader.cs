using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Profiles;

/// <summary>
/// Reads the callsign of a profile from the HamQSO project (PostgREST,
/// <c>public.profiles</c>) with the project secret key the server already has
/// for the alerts: profiles are private (RLS), the secret key reads past it.
/// </summary>
public sealed class SupabaseProfileReader(IOptions<SupabaseOptions> options, HttpClient http) : IProfileReader
{
    public async Task<string?> CallsignAsync(string userId, CancellationToken cancellationToken)
    {
        // Supabase user ids are UUIDs; anything else never reaches the query string.
        if (!Guid.TryParse(userId, out var id)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.Url, $"rest/v1/profiles?id=eq.{id:D}&select=callsign"));
        request.Headers.Add("apikey", options.Value.SecretKey);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return rows.RootElement.EnumerateArray()
            .Select(r => r.TryGetProperty("callsign", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null)
            .FirstOrDefault();
    }
}
