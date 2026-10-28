using System.Text.RegularExpressions;

namespace HamQso.Spots.Domain.Spotting;

/// <summary>
/// The only cluster command a user may send through the server, as the app
/// builds it: <c>DX &lt;kHz with one decimal&gt; &lt;callsign&gt; &lt;text&gt;</c>
/// (<c>DX 14025.0 K1ABC CQ CW</c>). The text may be missing, as in the app
/// when there is no reference, mode or comment. One line of printable ASCII
/// only: no control characters, no telnet bytes, nothing after it, so no other
/// command can ride along.
/// </summary>
public static partial class DxSpotLine
{
    /// <summary>"DX", frequency, a long callsign and the app's 60-character comment with references and mode.</summary>
    public const int MaxLength = 120;

    public static bool IsValid(string? line) =>
        line is { Length: > 0 and <= MaxLength } && Pattern().Match(line) is { Success: true } m && m.Groups[1].Value.TrimStart('0').Length > 0;

    // \z, not $: $ would let a final newline through. The text is printable ASCII only: no
    // control characters and nothing above 0x7E (0xFF is the telnet IAC byte).
    [GeneratedRegex(@"^DX (\d{1,7})\.\d [A-Za-z0-9]{1,10}(?:/[A-Za-z0-9]{1,10}){0,2}(?: [\x21-\x7E][\x20-\x7E]*)?\z")]
    private static partial Regex Pattern();
}
