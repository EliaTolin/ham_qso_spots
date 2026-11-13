using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Protection;

/// <summary>
/// POST {Supabase}/functions/v1/callsign_protection with the secret key (as the alerts). Plain HttpClient, not
/// the functions client: the status must be told apart, a 4xx is final (<see cref="UplinkRejectedException"/>)
/// and a 5xx or a network error is thrown to be retried.
/// </summary>
public sealed class SupabaseProtectionUplink(IOptions<SupabaseOptions> supabase, IOptions<ProtectionOptions> options, HttpClient http) : IProtectionUplink
{
    public async Task SendAsync(ProtectionBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var key = supabase.Value.SecretKey;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(supabase.Value.Url, $"functions/v1/{options.Value.Function}"))
        {
            Content = new StringContent(ProtectionPayload.Serialize(batch), Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.Add("apikey", key);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var text = body.Length > 200 ? body[..200] : body;
        if (status is >= 400 and < 500 && response.StatusCode != HttpStatusCode.RequestTimeout && status != 429) throw new UplinkRejectedException(status, text);
        throw new HttpRequestException($"callsign_protection answered {status}: {text}", null, response.StatusCode);
    }
}
