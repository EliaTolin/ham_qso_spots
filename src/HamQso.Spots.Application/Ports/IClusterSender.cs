namespace HamQso.Spots.Application.Ports;

/// <summary>Sends one command to the DX cluster node, logged in as a user.</summary>
public interface IClusterSender
{
    Task<ClusterSendResult> SendAsync(string login, string line, CancellationToken cancellationToken);
}

public enum ClusterSendStatus
{
    /// <summary>The node took the line.</summary>
    Sent,

    /// <summary>The node could not be reached or did not take the login in time.</summary>
    Unavailable,

    /// <summary>Too many sessions open: no new one is opened now.</summary>
    Busy,
}

/// <summary>The outcome; when sent, <see cref="Replies"/> are the lines the node wrote back right after (spots excluded).</summary>
public sealed record ClusterSendResult(ClusterSendStatus Status, IReadOnlyList<string> Replies)
{
    public static ClusterSendResult Unavailable { get; } = new(ClusterSendStatus.Unavailable, []);

    public static ClusterSendResult Busy { get; } = new(ClusterSendStatus.Busy, []);

    public static ClusterSendResult Sent(IReadOnlyList<string> replies) => new(ClusterSendStatus.Sent, replies);
}
