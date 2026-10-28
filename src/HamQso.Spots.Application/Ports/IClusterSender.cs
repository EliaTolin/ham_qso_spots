namespace HamQso.Spots.Application.Ports;

/// <summary>Sends one command to the DX cluster node, logged in as a user.</summary>
public interface IClusterSender
{
    Task<ClusterSendResult> SendAsync(string login, string line, CancellationToken cancellationToken);
}

/// <summary>
/// Sent: the node took the line; <see cref="Replies"/> are the lines it wrote
/// back right after (spots excluded). Not sent: the node could not be reached
/// or did not take the login in time.
/// </summary>
public sealed record ClusterSendResult(bool Sent, IReadOnlyList<string> Replies)
{
    public static ClusterSendResult Unavailable { get; } = new(false, []);
}
