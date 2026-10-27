using System.Collections.Concurrent;
using HamQso.Spots.Application.Ports;
using Microsoft.Extensions.Logging;

namespace HamQso.Spots.Application.Health;

/// <summary>The last success and failure of every source, for /health; the error itself goes only to the log.</summary>
public sealed partial class SourceHealthRegistry(TimeProvider time, ILogger<SourceHealthRegistry> logger) : ISourceHealth
{
    private readonly ConcurrentDictionary<string, SourceStatus> sources = new();

    public void Register(string source) => sources.TryAdd(source, new SourceStatus(source, null, null, null, false));

    public void Succeeded(string source) =>
        sources.AddOrUpdate(source, s => new SourceStatus(s, time.GetUtcNow(), null, null, false), (_, s) => s with { LastSuccess = time.GetUtcNow(), IsFailing = false });

    public void Failed(string source, string reason)
    {
        LogFailed(logger, source, reason);
        sources.AddOrUpdate(source, s => new SourceStatus(s, null, time.GetUtcNow(), reason, true), (_, s) => s with { LastFailure = time.GetUtcNow(), LastError = reason, IsFailing = true });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} failing: {Reason}")]
    private static partial void LogFailed(ILogger logger, string source, string reason);

    public IReadOnlyList<SourceStatus> Snapshot() => [.. sources.Values.OrderBy(s => s.Name, StringComparer.Ordinal)];
}

/// <summary>
/// A source is failing when its last report was a failure. Kept as the last
/// outcome, not by comparing times: a failure in the same instant as a success
/// must still show.
/// </summary>
public sealed record SourceStatus(string Name, DateTimeOffset? LastSuccess, DateTimeOffset? LastFailure, string? LastError, bool IsFailing);
