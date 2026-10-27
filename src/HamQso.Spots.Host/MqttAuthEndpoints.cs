using HamQso.Spots.Application.Access;

namespace HamQso.Spots.Host;

/// <summary>
/// The HTTP backend of mosquitto-go-auth (http_params_mode json,
/// http_response_mode status): 200 allows, 403 denies. Served only on the
/// internal port, reachable by Mosquitto alone.
/// </summary>
public static class MqttAuthEndpoints
{
    public static void MapMqttAuth(this IEndpointRouteBuilder app, string host)
    {
        var mqtt = app.MapGroup("/mqtt").RequireHost(host);
        mqtt.MapPost("/user", async (UserRequest request, MqttAccess access, CancellationToken ct) =>
            await access.AuthenticateAsync(request.Username, request.Password, ct) ? Results.Ok() : Results.StatusCode(StatusCodes.Status403Forbidden));
        // No superusers: every client goes through the ACL.
        mqtt.MapPost("/superuser", () => Results.StatusCode(StatusCodes.Status403Forbidden));
        mqtt.MapPost("/acl", (AclRequest request, MqttAccess access) =>
            Enum.IsDefined((MqttAccessKind)request.Acc) && access.Authorize(request.Username, request.Topic, (MqttAccessKind)request.Acc)
                ? Results.Ok()
                : Results.StatusCode(StatusCodes.Status403Forbidden));
    }

    public sealed record UserRequest(string? Username, string? Password, string? Clientid);

    public sealed record AclRequest(string? Username, string? Topic, string? Clientid, int Acc);
}
