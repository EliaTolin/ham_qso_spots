namespace HamQso.Spots.Domain.Spots;

/// <summary>
/// MQTT topics: spots/dx/&lt;band&gt;/&lt;mode&gt; for the DX cluster, spots/&lt;program&gt;
/// for xOTA. Every message is the whole current list of its topic, retained.
/// </summary>
public static class SpotTopics
{
    public const string Root = "spots";

    /// <summary>Band or mode not known.</summary>
    public const string Other = "other";

    public static string Of(Spot spot) => spot.Program == SpotProgram.Dx
        ? $"{Root}/dx/{spot.Band ?? Other}/{ModeOf(spot)}"
        : OfProgram(spot.Program);

    public static string OfProgram(SpotProgram program) => $"{Root}/{program.ToString().ToLowerInvariant()}";

    private static string ModeOf(Spot spot) => spot.ModeCategory switch
    {
        Radio.ModeCategory.Cw => "cw",
        Radio.ModeCategory.Phone => "phone",
        Radio.ModeCategory.Digital => "digital",
        _ => Other,
    };
}
