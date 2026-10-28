namespace HamQso.Spots.Application.Ports;

/// <summary>Sends one command to the DX cluster node, logged in as a user.</summary>
public interface IClusterSender
{
    /// <summary>
    /// Sends <paramref name="line"/> logged in as <paramref name="login"/>; one
    /// session per <paramref name="userId"/> and per login. The token only bounds the wait for
    /// the user's previous spot: once the spot starts it ends on its own timeouts.
    /// </summary>
    Task<ClusterSendResult> SendAsync(string userId, string login, string line, CancellationToken cancellationToken);
}

public enum ClusterSendStatus
{
    /// <summary>The node took the line.</summary>
    Sent,

    /// <summary>The node could not be reached or did not take the login in time.</summary>
    Unavailable,

    /// <summary>The server is full: no session can be opened now, or too many spots for the whole server.</summary>
    Busy,

    /// <summary>Another user has a session open with this login: two users never share one (the client sees only "busy").</summary>
    LoginInUse,
}

/// <summary>The outcome; when sent, <see cref="Replies"/> are the lines the node wrote back right after (spots excluded).</summary>
public sealed record ClusterSendResult(ClusterSendStatus Status, IReadOnlyList<string> Replies)
{
    public static ClusterSendResult Unavailable { get; } = new(ClusterSendStatus.Unavailable, []);

    public static ClusterSendResult Busy { get; } = new(ClusterSendStatus.Busy, []);

    public static ClusterSendResult LoginInUse { get; } = new(ClusterSendStatus.LoginInUse, []);

    public static ClusterSendResult Sent(IReadOnlyList<string> replies) => new(ClusterSendStatus.Sent, replies);
}
