using System.Globalization;
using System.Text.Json;

namespace HamQso.Spots.Infrastructure.Sources.Http;

/// <summary>Lenient reads of feed JSON: a bad field gives null, never an exception.</summary>
internal static class JsonValues
{
    public static string? Text(this JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s.Trim() : null;

    /// <summary>A number, also when the feed sends it as text ("7074").</summary>
    public static double? Number(this JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>A true/false field; null when missing or not a boolean.</summary>
    public static bool? Bool(this JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    /// <summary>A whole number, also when the feed sends it as text; null otherwise.</summary>
    public static int? Int(this JsonElement row, string name) =>
        row.Number(name) is { } n && n == Math.Floor(n) && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;

    /// <summary>Id as text, whether the feed sends a number or a string.</summary>
    public static string? Id(this JsonElement row, string name) =>
        !row.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.Number ? v.GetRawText()
        : v.ValueKind == JsonValueKind.String ? v.GetString()
        : null;

    /// <summary>ISO time; a time without zone is UTC. Null when missing or malformed.</summary>
    public static DateTimeOffset? Time(this JsonElement row, string name) =>
        DateTimeOffset.TryParse(row.Text(name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    /// <summary>The rows of a JSON array; none when the body is not one.</summary>
    public static IEnumerable<JsonElement> Rows(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : [];
}
