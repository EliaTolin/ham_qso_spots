using HamQso.Spots.Application.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HamQso.Spots.Host;

/// <summary>
/// /health: degraded when some source is failing, unhealthy when all are. Public,
/// so it shows only the state of each source; the errors are in the log.
/// </summary>
public sealed class SourcesHealthCheck(SourceHealthRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var sources = registry.Snapshot();
        var failing = sources.Where(s => s.IsFailing).ToArray();
        var data = sources.ToDictionary(s => s.Name, object (s) => s.IsFailing ? "failing" : s.LastSuccess is null ? "starting" : "ok");
        var result = failing.Length == 0 ? HealthCheckResult.Healthy("all sources ok", data)
            : failing.Length == sources.Count ? HealthCheckResult.Unhealthy("all sources failing", data: data)
            : HealthCheckResult.Degraded($"{failing.Length} of {sources.Count} sources failing", data: data);
        return Task.FromResult(result);
    }
}
