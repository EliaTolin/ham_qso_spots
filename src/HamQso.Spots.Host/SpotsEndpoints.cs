using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Infrastructure;

namespace HamQso.Spots.Host;

/// <summary>
/// GET /v1/spots: the starting list of an app, or what changed since its last
/// seq ({"seq", "full", "spots"}, the same shape as the MQTT news).
/// GET /v1/spots/on-frequency: the station a frequency suggests (F88), {"spot"}.
/// Only HamQSO users: the Supabase access token as Bearer, as for the broker.
/// </summary>
public static class SpotsEndpoints
{
    public static void MapSpotsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/spots", async (HttpContext http, ITokenValidator tokens, GetSpots getSpots, CancellationToken ct) =>
        {
            if (await BearerAuth.UserAsync(http, tokens, ct) is null) return Results.Unauthorized();
            var query = SpotQueryParser.Parse(name => http.Request.Query[name], out var error);
            if (query is null) return Results.BadRequest(new { error });
            var page = await getSpots.ExecuteAsync(query.Filter, query.Limit, query.SinceSeq, ct);
            return Results.Text(SpotJson.Serialize(page), "application/json");
        });

        app.MapGet("/v1/spots/on-frequency", async (HttpContext http, ITokenValidator tokens, GetSpotOnFrequency getSpot, CancellationToken ct) =>
        {
            if (await BearerAuth.UserAsync(http, tokens, ct) is null) return Results.Unauthorized();
            var khz = SpotQueryParser.ParseFrequency(name => http.Request.Query[name], out var error);
            if (khz is null) return Results.BadRequest(new { error });
            var spot = await getSpot.ExecuteAsync(khz.Value, ct);
            return Results.Text(SpotJson.SerializeOnFrequency(spot), "application/json");
        });
    }
}
