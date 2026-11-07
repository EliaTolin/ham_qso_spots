using System.Globalization;

namespace HamQso.Voacap.Core;

/// <summary>
/// The output of voacapl, method 30: one page per circuit, each with one block
/// for the hour asked. In a block every row has fixed-width columns: 6 characters
/// for the hour (FREQ row only), then 12 columns of 5, the first being the circuit
/// MUF and the second the first frequency asked; the row's name is at the end.
/// Columns are read by position (a mode can have a space: "1 E").
/// </summary>
public static class VoacapOutput
{
    private const int First = 6;
    private const int Width = 5;
    private const int Columns = 12;
    private const string End = "END OF RUN";

    /// <summary>Whether voacapl got to the end of the deck.</summary>
    public static bool Completed(string output) => output?.Contains(End, StringComparison.Ordinal) == true;

    /// <summary>One result per circuit, in the order of the deck, for the first frequency.</summary>
    public static IReadOnlyList<CircuitResult> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var results = new List<CircuitResult>();
        double? muf = null;
        string? mode = null;
        int? snr = null;
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            switch (Label(line))
            {
                case "FREQ":
                    (muf, mode, snr) = (Number(Column(line, 0)), null, null);
                    break;
                case "MODE":
                    mode = Column(line, 1).Replace(" ", "", StringComparison.Ordinal) is { Length: > 0 } m && m != "-" ? m : null;
                    break;
                case "SNR":
                    snr = Number(Column(line, 1)) is { } v && v > -999 ? (int)v : null;
                    break;
                case "REL":
                    results.Add(new CircuitResult(Number(Column(line, 1)) ?? 0, snr, muf, mode));
                    break;
            }
        }
        return results;
    }

    /// <summary>What voacapl said when it failed, on one line, at most 300 characters.</summary>
    public static string ErrorMessage(string text)
    {
        var line = string.Join(' ', (text ?? "").Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= 300 ? line : line[..300];
    }

    private static string? Label(string line)
    {
        if (line.Length <= First + Columns * Width) return null;
        var label = line[(First + Columns * Width)..].Trim();
        return label is "FREQ" or "MODE" or "SNR" or "REL" ? label : null;
    }

    private static string Column(string line, int index)
    {
        var start = First + index * Width;
        return line.Length >= start + Width ? line.Substring(start, Width).Trim() : "";
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
