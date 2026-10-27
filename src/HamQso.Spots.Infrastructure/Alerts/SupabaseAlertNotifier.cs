using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Options;
using Supabase.Functions;

namespace HamQso.Spots.Infrastructure.Alerts;

/// <summary>
/// Hands new spots to the alerts edge function with the official C# client.
/// The function checks the secret key; matching, anti-spam, US states, pushes
/// and statistics stay in Supabase, in one place.
/// </summary>
public sealed class SupabaseAlertNotifier(IOptions<SupabaseOptions> options, HttpClient http) : IAlertNotifier
{
    private readonly Client functions = new(
        new Uri(options.Value.Url, "functions/v1").ToString(),
        null,
        new ClientOptions { HttpClient = http });

    public async Task NotifyAsync(IReadOnlyList<Spot> spots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spots);
        if (spots.Count == 0) return;
        var key = options.Value.SecretKey;
        await functions.Invoke(options.Value.AlertsFunction, key, new Client.InvokeFunctionOptions
        {
            Headers = { ["apikey"] = key },
            Body = { ["spots"] = spots.Select(AlertSpotPayload.From).ToArray() },
        }, cancellationToken).ConfigureAwait(false);
    }
}
