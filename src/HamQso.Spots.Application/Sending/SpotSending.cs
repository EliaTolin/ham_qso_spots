namespace HamQso.Spots.Application.Sending;

/// <summary>Whether POST /v1/dx/spots works on this server, and why not when it does not (for the log).</summary>
public sealed record SpotSending(bool Enabled, string? OffReason = null)
{
    public static SpotSending On { get; } = new(true);
}
