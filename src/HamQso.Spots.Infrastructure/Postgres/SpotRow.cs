using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Postgres;

/// <summary>A dx_spots row: enums as snake_case text, times as UTC.</summary>
internal sealed record SpotRow
{
    public required string Program { get; init; }

    public required string SpotKey { get; init; }

    public required string Callsign { get; init; }

    public required double FrequencyKhz { get; init; }

    public required DateTime SpotTime { get; init; }

    public required string[] Feeds { get; init; }

    public required string[] Spotters { get; init; }

    public string? Band { get; init; }

    public required string Mode { get; init; }

    public string? ModeSource { get; init; }

    public string? ModeCategory { get; init; }

    public string? Reference { get; init; }

    public string? Location { get; init; }

    public int? Dxcc { get; init; }

    public string? Continent { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? Comment { get; init; }

    public string? SpotterLocator { get; init; }

    public static SpotRow From(Spot s) => new()
    {
        Program = SpotJson.Name(s.Program),
        SpotKey = s.SpotKey,
        Callsign = s.Callsign,
        FrequencyKhz = s.FrequencyKhz,
        SpotTime = s.SpotTime.UtcDateTime,
        Feeds = [.. s.Feeds],
        Spotters = [.. s.Spotters],
        Band = s.Band,
        Mode = s.Mode,
        ModeSource = s.ModeSource is { } source ? SpotJson.Name(source) : null,
        ModeCategory = s.ModeCategory is { } category ? SpotJson.Name(category) : null,
        Reference = s.Reference,
        Location = s.Location,
        Dxcc = s.Dxcc,
        Continent = s.Continent,
        Latitude = s.Latitude,
        Longitude = s.Longitude,
        Comment = s.Comment,
        SpotterLocator = s.SpotterLocator,
    };

    public Spot ToSpot() => new()
    {
        Program = SpotJson.Parse<SpotProgram>(Program),
        SpotKey = SpotKey,
        Callsign = Callsign,
        FrequencyKhz = FrequencyKhz,
        SpotTime = new DateTimeOffset(DateTime.SpecifyKind(SpotTime, DateTimeKind.Utc)),
        Feeds = Feeds,
        Spotters = Spotters,
        Band = Band,
        Mode = Mode,
        ModeSource = ModeSource is null ? null : SpotJson.Parse<ModeSource>(ModeSource),
        ModeCategory = ModeCategory is null ? null : SpotJson.Parse<ModeCategory>(ModeCategory),
        Reference = Reference,
        Location = Location,
        Dxcc = Dxcc,
        Continent = Continent,
        Latitude = Latitude,
        Longitude = Longitude,
        Comment = Comment,
        SpotterLocator = SpotterLocator,
    };
}
