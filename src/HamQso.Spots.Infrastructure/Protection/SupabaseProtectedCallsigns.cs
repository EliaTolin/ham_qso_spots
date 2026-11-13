using System.Text;
using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Protection;

/// <summary>
/// The callsigns to watch: the backend's <c>callsign_protection_watchlist</c> (PostgREST RPC, secret key as
/// <see cref="Profiles.SupabaseProfileReader"/>), which returns only the eligible owners
/// (<c>[{"callsign":"IN3FOB/P","base":"IN3FOB"}, …]</c>), not every profile with a callsign.
/// </summary>
public sealed class SupabaseProtectedCallsigns(IOptions<SupabaseOptions> options, HttpClient http) : IProtectedCallsignSource
{
    public const string Rpc = "rest/v1/rpc/callsign_protection_watchlist";

    /// <summary>PostgREST answers at most <c>max_rows</c> (1000) rows: the list is read in pages of this size.</summary>
    public const int PageSize = 1000;

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        var all = new List<string>();
        for (var offset = 0; ; offset += PageSize)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Value.Url, $"{Rpc}?order=callsign&limit={PageSize}&offset={offset}"))
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("apikey", options.Value.SecretKey);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var page = rows.RootElement.EnumerateArray().ToArray();
            all.AddRange(page
                .Select(r => r.TryGetProperty("callsign", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null)
                .OfType<string>());
            if (page.Length < PageSize) return all;
        }
    }
}
