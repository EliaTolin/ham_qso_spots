using System.Text.Json;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.References;
using HamQso.Spots.Infrastructure.Alerts;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Infrastructure.Catalog;

/// <summary>
/// Every active reference of HamQSO (PostgREST, <c>public.xota_references</c>)
/// with the project secret key, a page at a time after the last code read
/// (keyset: no offset to skip over 300,000 rows). Once a week (F53.3).
/// </summary>
public sealed class SupabaseReferenceCatalogSource(IOptions<SupabaseOptions> options, HttpClient http) : IReferenceCatalogSource
{
    private const string Columns = "ref,program,name,location,dxcc,grid,lat,lon,altitude_m,points";

    /// <summary>PostgREST answers at most 1,000 rows (Supabase max rows).</summary>
    public int PageSize { get; init; } = 1000;

    public async Task<IReadOnlyList<CatalogReference>> ReadActiveAsync(CancellationToken cancellationToken)
    {
        var all = new List<CatalogReference>();
        string? after = null;
        while (true)
        {
            var path = $"rest/v1/xota_references?select={Columns}&active=is.true&order=ref.asc&limit={PageSize}"
                + (after is null ? "" : "&ref=gt." + Uri.EscapeDataString(after));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.Url, path));
            request.Headers.Add("apikey", options.Value.SecretKey);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var rows = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            var page = rows.RootElement.EnumerateArray().Select(Reference).OfType<CatalogReference>().ToList();
            all.AddRange(page);
            if (page.Count < PageSize) return all;
            after = page[^1].Ref;
        }
    }

    private static CatalogReference? Reference(JsonElement r)
    {
        var code = Text(r, "ref");
        var program = Text(r, "program");
        var name = Text(r, "name");
        if (code is null || program is null || name is null) return null;
        return new CatalogReference(code, program, name, Text(r, "location"), Int(r, "dxcc"), Text(r, "grid"),
            Num(r, "lat"), Num(r, "lon"), Int(r, "altitude_m"), Int(r, "points"));
    }

    private static string? Text(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? Num(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
