using System.Text.Json;
using System.Text.Json.Serialization;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure;

/// <summary>
/// The one spot format of the server: snake_case fields and enum values
/// ("program": "dx", "mode_source": "band_plan"), the same as the dx_spots
/// columns. Used by the MQTT news, the REST answers and the archive.
/// </summary>
public static class SpotJson
{
    private static readonly JsonNamingPolicy Naming = JsonNamingPolicy.SnakeCaseLower;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = Naming,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(Naming) },
    };

    /// <summary>{"seq", "full", "spots"}: the MQTT news and the REST answers.</summary>
    public static string Serialize(SpotPage page) => JsonSerializer.Serialize(page, Options);

    public static SpotPage Deserialize(string json) =>
        JsonSerializer.Deserialize<SpotPage>(json, Options) ?? throw new JsonException("empty spot page");

    /// <summary>The text of an enum value ("BandPlan" is "band_plan").</summary>
    public static string Name<T>(T value)
        where T : struct, Enum => Naming.ConvertName(value.ToString());

    public static T Parse<T>(string name)
        where T : struct, Enum =>
        Enum.GetValues<T>().First(v => Name(v) == name);
}
