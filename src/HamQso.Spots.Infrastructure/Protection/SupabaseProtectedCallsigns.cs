using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Protection;

/// <summary>
/// The callsigns of the profiles with the protection on (PostgREST, <c>public.profiles</c>, secret key as
/// <see cref="Profiles.SupabaseProfileReader"/>), in pages. Who is eligible (app opened lately, Pro) is
/// decided by the backend when it ingests: here the whole list may be read, narrowing is only cheaper.
/// </summary>
public sealed class SupabaseProtectedCallsigns(IOptions<SupabaseOptions> options, HttpClient http) : IProtectedCallsignSource
{
    public const int PageSize = 1000;

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        var all = new List<string>();
        for (var offset = 0; ; offset += PageSize)
        {
            var url = new Uri(options.Value.Url, $"rest/v1/profiles?callsign_protection=eq.true&callsign=not.is.null&select=callsign&order=id&limit={PageSize}&offset={offset}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("apikey", options.Value.SecretKey);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var page = rows.RootElement.EnumerateArray()
                .Select(r => r.TryGetProperty("callsign", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null)
                .OfType<string>()
                .ToArray();
            all.AddRange(page);
            if (page.Length < PageSize) return all;
        }
    }
}
