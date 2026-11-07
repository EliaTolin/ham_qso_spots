using System.Text.RegularExpressions;

namespace HamQso.Spots.Domain.Spots;

/// <summary>
/// The spotter says the activator went QRT: "QRT" as a word in the comment
/// ("qrt off to next peak", not "QRTX"). Same rule as the app's commentSaysQrt
/// (lib/common/programs/qrt.dart, shared cases in on_frequency_cases.json).
/// </summary>
public static partial class QrtComment
{
    public static bool Says(string? comment) => comment is not null && Qrt().IsMatch(comment);

    /// <summary>ECMAScript: \b as in Dart, on ASCII letters only, so that "QRTé" is a QRT in both.</summary>
    [GeneratedRegex(@"\bQRT\b", RegexOptions.IgnoreCase | RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Qrt();
}
