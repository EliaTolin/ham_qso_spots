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

    /// <summary>Name of the reference: park (POTA, WWFF) or summit (SOTA).</summary>
    public string? ReferenceName { get; init; }

    /// <summary>Name of the activator, as SOTAwatch gives it.</summary>
    public string? ActivatorName { get; init; }

    /// <summary>Altitude of the summit in metres (SOTA).</summary>
    public int? AltitudeM { get; init; }

    /// <summary>Activation points of the summit (SOTA).</summary>
    public int? Points { get; init; }

    /// <summary>How many times the activation was spotted on this band (xOTA; counted by the archive). DX: 1.</summary>
    public int SpotCount { get; init; } = 1;

    /// <summary>The station confirms on LoTW / eQSL (HamQTH); null when the source does not tell.</summary>
    public bool? Lotw { get; init; }

    public bool? Eqsl { get; init; }

    /// <summary>Location of the reference as the program gives it (POTA "US-MI"): the US state of an alert.</summary>
    public string? Location { get; init; }

    /// <summary>Locator of the spotter, sent by some telnet nodes.</summary>
    public string? SpotterLocator { get; init; }
}
