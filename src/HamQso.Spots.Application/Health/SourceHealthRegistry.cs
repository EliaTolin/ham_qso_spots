using System.Collections.Concurrent;
using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Application.Health;

/// <summary>The last success and failure of every source, for /health.</summary>
public sealed class SourceHealthRegistry(TimeProvider time) : ISourceHealth
{
    private readonly ConcurrentDictionary<string, SourceStatus> sources = new();

    public void Register(string source) => sources.TryAdd(source, new SourceStatus(source, null, null, null));

    public void Succeeded(string source) =>
        sources.AddOrUpdate(source, s => new SourceStatus(s, time.GetUtcNow(), null, null), (_, s) => s with { LastSuccess = time.GetUtcNow() });

    public void Failed(string source, string reason) =>
        sources.AddOrUpdate(source, s => new SourceStatus(s, null, time.GetUtcNow(), reason), (_, s) => s with { LastFailure = time.GetUtcNow(), LastError = reason });

    public IReadOnlyList<SourceStatus> Snapshot() => [.. sources.Values.OrderBy(s => s.Name, StringComparer.Ordinal)];
}

/// <summary>A source is failing when its last failure is newer than its last success.</summary>
public sealed record SourceStatus(string Name, DateTimeOffset? LastSuccess, DateTimeOffset? LastFailure, string? LastError)
{
    public bool IsFailing => LastFailure is { } failure && (LastSuccess is not { } success || failure > success);
}
