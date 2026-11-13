using System.Text.Json;
using System.Text.Json.Serialization;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Protection;

namespace HamQso.Spots.Application.Protection;

/// <summary>The JSON body of callsign_protection (the contract of package B).</summary>
public static class ProtectionPayload
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string Serialize(ProtectionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return JsonSerializer.Serialize(new
        {
            uses = batch.Uses.Select(Use).ToArray(),
            @checked = batch.Checked,
            failed = batch.Failed.Select(f => new { source = f.Source, message = f.Message }).ToArray(),
        }, Options);
    }

    /// <summary>The size of the use in the body, to keep a batch under the 256 KB the backend takes.</summary>
    public static int SizeOf(ProtectedUse use) => JsonSerializer.SerializeToUtf8Bytes(Use(use), Options).Length + 1;

    private static Dictionary<string, object?> Use(ProtectedUse u) => new()
    {
        ["callsign"] = u.Callsign,
        ["signal"] = u.Signal.Wire(),
        ["used_at"] = u.UsedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["band"] = u.Band,
        ["frequency_khz"] = u.FrequencyKhz is { } f ? Math.Round(f, 3) : null,
        ["mode"] = u.Mode,
        ["heard_by"] = u.HeardBy,
        ["heard_locator"] = u.HeardLocator,
        ["raw"] = u.Raw,
        ["source_id"] = u.SourceId,
    };
}
