using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Infrastructure;

namespace HamQso.Spots.Host;

/// <summary>
/// GET /v1/spots: the starting list of an app, or what changed since its last
/// seq ({"seq", "full", "spots"}, the same shape as the MQTT news). Only HamQSO
/// users: the Supabase access token as Bearer, as for the broker.
/// </summary>
public static class SpotsEndpoints
{
    private const string Bearer = "Bearer ";

    public static void MapSpotsApi(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/spots", async (HttpContext http, ITokenValidator tokens, GetSpots getSpots, CancellationToken ct) =>
        {
            var authorization = http.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith(Bearer, StringComparison.Ordinal) || !await tokens.IsValidAsync(authorization[Bearer.Length..], ct))
            {
                return Results.Unauthorized();
            }
            var query = SpotQueryParser.Parse(name => http.Request.Query[name], out var error);
            if (query is null) return Results.BadRequest(new { error });
            var page = await getSpots.ExecuteAsync(query.Filter, query.Limit, query.SinceSeq, ct);
            return Results.Text(SpotJson.Serialize(page), "application/json");
        });
}
