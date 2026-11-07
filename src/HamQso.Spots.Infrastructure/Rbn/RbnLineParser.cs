using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Infrastructure.Rbn;

/// <summary>A line of the RBN telnet feed as an <see cref="RbnLine"/> (the reading is <see cref="RbnSpotLine"/>); anything else is not a spot.</summary>
public static class RbnLineParser
{
    /// <summary>The spot (the line trimmed, without control characters), received at <paramref name="receivedAt"/>, or null.</summary>
    public static RbnLine? Parse(string line, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = RbnSpotLine.Clean(line);
        return RbnSpotLine.Parse(text) is { } spot ? new RbnLine(text, spot, receivedAt) : null;
    }
}
