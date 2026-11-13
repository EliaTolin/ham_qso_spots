using System.Text.Json;
using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Domain.Protection;

/// <summary>A PSKReporter report (MQTT feed v2): <see cref="Sender"/> was heard by <see cref="Receiver"/>.</summary>
public sealed record PskReport(string Sender, string Receiver, string? ReceiverLocator, string Band, double FrequencyKhz, string? Mode, int? SnrDb);

public static class PskReportParser
{
    /// <summary>The report of a message payload (keys sc, rc, rl, f in Hz, md, rp, b), or null when it is not a usable report.</summary>
    public static PskReport? Parse(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var sender = Text(root, "sc");
            var receiver = Text(root, "rc");
            if (sender is null || receiver is null || !root.TryGetProperty("f", out var f) || !f.TryGetDouble(out var hz)) return null;
            var khz = hz / 1000;
            var band = Text(root, "b") is { } b && Bands.Labels.Contains(b) ? b : Bands.Of(khz);
            if (band is null) return null;
            return new PskReport(
                sender.ToUpperInvariant(),
                receiver.ToUpperInvariant(),
                Text(root, "rl"),
                band,
                khz,
                Text(root, "md"),
                root.TryGetProperty("rp", out var rp) && rp.TryGetInt32(out var snr) ? snr : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}

/// <summary>The MQTT topics to listen to: only the protected callsigns, never the whole feed.</summary>
public static class PskTopics
{
    /// <summary>The ways a portable suffix can come in a topic segment if the feed writes "/" as "_" (the payload decides in the end).</summary>
    public static readonly string[] SuffixVariants = ["_P", "_M", "_QRP", "_MM", "_A"];

    /// <summary>
    /// For every callsign: the sender as it is, and the variants. A "/" kept in the topic is covered by the
    /// trailing "#" of the first. Format: pskr/filter/v2/{band}/{mode}/{sender}/...
    /// </summary>
    public static IReadOnlyList<string> For(IEnumerable<string> baseCallsigns)
    {
        ArgumentNullException.ThrowIfNull(baseCallsigns);
        return [.. baseCallsigns.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .SelectMany(c => new[] { c }.Concat(SuffixVariants.Select(s => c + s)))
            .Select(sender => $"pskr/filter/v2/+/+/{sender}/#")];
    }
}
