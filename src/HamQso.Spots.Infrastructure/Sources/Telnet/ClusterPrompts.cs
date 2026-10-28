namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>The prompts of DXSpider-compatible nodes, which come without a newline.</summary>
public static class ClusterPrompts
{
    public static bool IsLogin(string text)
    {
        var t = Normalize(text);
        return t.EndsWith("LOGIN:", StringComparison.Ordinal) || t.EndsWith("CALL:", StringComparison.Ordinal) || t.EndsWith("CALLSIGN:", StringComparison.Ordinal);
    }

    public static bool IsPassword(string text) => Normalize(text).EndsWith("PASSWORD:", StringComparison.Ordinal);

    private static string Normalize(string text) => text.TrimEnd().ToUpperInvariant();
}
