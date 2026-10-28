using System.Text;

namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>
/// Splits the text of a node into lines and keeps the part after the last
/// newline (a prompt, or a line still arriving) for the next text.
/// </summary>
public sealed class LineBuffer
{
    /// <summary>A cluster line is under 100 characters: text without newline beyond this is dropped.</summary>
    public const int MaxPendingChars = 4096;

    private readonly StringBuilder pending = new();

    /// <summary>Characters received after the last newline.</summary>
    public int PendingLength => pending.Length;

    public string Pending => pending.ToString();

    /// <summary>The complete lines in what came so far; the rest waits.</summary>
    public IReadOnlyList<string> Append(string text)
    {
        pending.Append(text);
        var lines = pending.ToString().Split('\n');
        pending.Clear().Append(lines[^1]);
        // A node that never sends a newline must not grow the buffer forever.
        if (pending.Length > MaxPendingChars) pending.Clear();
        return lines[..^1];
    }

    /// <summary>Drops the pending text (a prompt already answered).</summary>
    public void ClearPending() => pending.Clear();
}
