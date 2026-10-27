namespace HamQso.Spots.Host;

/// <summary>
/// Keeps the /mqtt/* endpoints to the internal port. The check is on the port
/// the connection arrived on: the Host header is chosen by the client.
/// </summary>
public static class InternalPortOnly
{
    public static bool Allows(HttpContext context, int internalPort)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.LocalPort == internalPort;
    }

    /// <summary>404 for /mqtt/* requests that did not come in on the internal port.</summary>
    public static TBuilder RequireInternalPort<TBuilder>(this TBuilder builder, int internalPort)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            Allows(context.HttpContext, internalPort) ? await next(context) : Results.NotFound());
}
