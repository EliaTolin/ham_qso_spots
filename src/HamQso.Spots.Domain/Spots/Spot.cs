using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Spots;

/// <summary>
/// A spot from any feed, cleaned once. Same fields as the dx_spots table and the
/// MQTT messages. <see cref="Feeds"/> and <see cref="Spotters"/> grow when the
/// same spot arrives from several feeds (see <see cref="DuplicateRule"/>).
/// </summary>
public sealed record Spot
{
    public required SpotProgram Program { get; init; }

    /// <summary>
    /// Order of the save, given by the archive: grows at every new or updated
    /// spot. The apps keep the highest they have seen (since_seq).
    /// </summary>
    public long? Seq { get; init; }

    /// <summary>Id of the spot at its source (xOTA) or built from call, kHz and time (DX).</summary>
    public required string SpotKey { get; init; }

    public required string Callsign { get; init; }

    public required double FrequencyKhz { get; init; }

    public required DateTimeOffset SpotTime { get; init; }

    /// <summary>Feeds that delivered it ("DXFun", "IZ8MBW-6", "HamQTH", "DXSummit", "POTA"...).</summary>
    public required IReadOnlyList<string> Feeds { get; init; }

    public IReadOnlyList<string> Spotters { get; init; } = [];

    public string? Band { get; init; }

    /// <summary>Mode as reported or from the band plan; empty when unknown.</summary>
    public string Mode { get; init; } = "";

    public ModeSource? ModeSource { get; init; }

    public ModeCategory? ModeCategory { get; init; }

    /// <summary>Program reference ("IT-0123", "I/LO-001", "IFF-0001").</summary>
    public string? Reference { get; init; }

    public int? Dxcc { get; init; }

    public string? Continent { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? Comment { get; init; }

    /// <summary>Location of the reference as the program gives it (POTA "US-MI"): the US state of an alert.</summary>
    public string? Location { get; init; }

    /// <summary>Locator of the spotter, sent by some telnet nodes.</summary>
    public string? SpotterLocator { get; init; }
}
