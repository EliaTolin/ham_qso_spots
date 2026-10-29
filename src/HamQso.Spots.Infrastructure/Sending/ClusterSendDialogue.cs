using HamQso.Spots.Infrastructure.Sources.Telnet;

namespace HamQso.Spots.Infrastructure.Sending;

/// <summary>
/// The login dialogue of a sending session, without I/O: the user's login at
/// the prompt (or after the fallback), an empty password if asked (DXFun: "If
/// you do not have a password set, don't enter one", as the app), and the
/// lines a user should see. Spots broadcast by the node are not replies.
/// </summary>
public sealed class ClusterSendDialogue(string login, DateTimeOffset start)
{
    private readonly DateTimeOffset started = start;
    private readonly LineBuffer lines = new();
    private DateTimeOffset lastData = start;
    private DateTimeOffset? loginAt;
    private bool passwordSent;

    public TelnetDecoder Decoder { get; } = new();

    public bool LoginSent => loginAt is not null;

    /// <summary>Logged in and the node quiet for <paramref name="welcomeSilence"/>: commands may go.</summary>
    public bool IsReady(DateTimeOffset now, TimeSpan welcomeSilence) =>
        loginAt is { } at && now - (lastData > at ? lastData : at) >= welcomeSilence;

    /// <summary>The login when the node has not asked for it by <paramref name="fallback"/>.</summary>
    public string? OnTick(DateTimeOffset now, TimeSpan fallback)
    {
        if (loginAt is not null || now - started < fallback) return null;
        loginAt = now;
        return login;
    }

    /// <summary>The lines for the user in the text received, and the answer to a prompt.</summary>
    public (IReadOnlyList<string> Lines, string? Command) OnText(string text, DateTimeOffset now)
    {
        lastData = now;
        var shown = lines.Append(text)
            .Select(l => new string([.. l.Where(c => !char.IsControl(c))]).Trim())
            .Where(l => l.Length > 0 && DxClusterLineParser.Parse("", l, now) is null)
            .ToArray();

        string? command = null;
        var prompt = lines.Pending;
        if (loginAt is null && ClusterPrompts.IsLogin(prompt))
        {
            loginAt = now;
            command = login;
        }
        else if (loginAt is not null && !passwordSent && ClusterPrompts.IsPassword(prompt))
        {
            passwordSent = true;
            command = "";
        }
        // Prompts come without a newline: once answered, drop them.
        if (command is not null) lines.ClearPending();
        return (shown, command);
    }
}
