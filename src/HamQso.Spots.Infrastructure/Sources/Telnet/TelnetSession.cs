using System.Text;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>The login dialogue and line splitting of one connection, without I/O.</summary>
public sealed class TelnetSession(TelnetOptions options, DateTimeOffset start)
{
    private readonly DateTimeOffset started = start;
    private readonly StringBuilder pending = new();
    private DateTimeOffset lastData = start;
    private DateTimeOffset? loginAt;
    private bool backlogSent;

    public TelnetDecoder Decoder { get; } = new();

    /// <summary>How long to wait for data before <see cref="OnSilence"/>.</summary>
    public TimeSpan NextWait(DateTimeOffset now)
    {
        var deadline = loginAt is null ? started + options.LoginFallback
            : !backlogSent ? lastData + options.BacklogDelay
            : lastData + options.IdleTimeout;
        return deadline > now ? deadline - now : TimeSpan.FromMilliseconds(1);
    }

    /// <summary>The command to send after a silence, or null when the node is mute.</summary>
    public string? OnSilence(DateTimeOffset now)
    {
        if (loginAt is null)
        {
            loginAt = now;
            lastData = now;
            return options.Callsign;
        }
        if (!backlogSent)
        {
            backlogSent = true;
            lastData = now;
            return FormattableString.Invariant($"sh/dx {options.Backlog}");
        }
        return null;
    }

    /// <summary>Spots in the text received, and the login when the node asks for it.</summary>
    public (IReadOnlyList<Spot> Spots, string? Command) OnText(string text, string feed, DateTimeOffset now)
    {
        lastData = now;
        pending.Append(text);
        var all = pending.ToString();
        var lines = all.Split('\n');
        pending.Clear().Append(lines[^1]);

        var spots = new List<Spot>();
        foreach (var line in lines[..^1])
        {
            if (DxClusterLineParser.Parse(feed, line, now) is { } spot) spots.Add(spot);
        }

        string? command = null;
        if (loginAt is null && IsLoginPrompt(pending.ToString()))
        {
            // Prompts come without a newline: once answered, drop them.
            pending.Clear();
            loginAt = now;
            command = options.Callsign;
        }
        return (spots, command);
    }

    private static bool IsLoginPrompt(string text)
    {
        var t = text.TrimEnd().ToUpperInvariant();
        return t.EndsWith("LOGIN:", StringComparison.Ordinal) || t.EndsWith("CALL:", StringComparison.Ordinal) || t.EndsWith("CALLSIGN:", StringComparison.Ordinal);
    }
}
