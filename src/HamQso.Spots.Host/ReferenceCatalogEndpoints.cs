using HamQso.Spots.Application.Catalog;
using HamQso.Spots.Application.Ports;
using Microsoft.Net.Http.Headers;

namespace HamQso.Spots.Host;

/// <summary>
/// GET /v1/references/catalog (F53.3): the world reference catalog the apps
/// keep offline, gzip as it is (no Content-Encoding: the app stores the bytes
/// it counted). With the ETag of the file the app has, a 304 without body.
/// Every HamQSO user, anonymous ones too (they log offline as well): the
/// Supabase access token as Bearer, as for the lists.
/// </summary>
public static class ReferenceCatalogEndpoints
{
    public static void MapReferenceCatalogApi(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/references/catalog", async (HttpContext http, ITokenValidator tokens, CurrentReferenceCatalog current, CancellationToken ct) =>
        {
            if (await BearerAuth.UserAsync(http, tokens, ct) is null) return Results.Unauthorized();
            if (current.File is not { } file)
            {
                http.Response.Headers.RetryAfter = "3600";
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            http.Response.Headers.ETag = file.ETag;
            http.Response.Headers.LastModified = file.GeneratedAt.ToString("R");
            http.Response.Headers["X-Reference-Count"] = file.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var asked = http.Request.Headers.IfNoneMatch.ToString();
            if (asked.Length > 0 && EntityTagHeaderValue.ParseList([asked]).Any(t => t.Tag == file.ETag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            return Results.Bytes(file.Gzip, "application/gzip");
        });
}
