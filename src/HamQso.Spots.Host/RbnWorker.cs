using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Infrastructure.Rbn;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>
/// F71, with Rbn:AlwaysOn: keeps the RBN connection open for good, counting
/// every spot as skimmer activity (<see cref="RecordSkimmerActivity"/>), and
/// reads the RBN node list every Rbn:NodesInterval (<see cref="RefreshRbnNodes"/>).
/// Both report to /health ("rbn", "rbn_nodes").
/// </summary>
public sealed partial class RbnWorker(
    PostgresRbnStore store,
    RecordSkimmerActivity record,
    RefreshRbnNodes refresh,
    SourceHealthRegistry health,
    IOptions<RbnOptions> options,
    TimeProvider time,
    ILogger<RbnWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SchemaRetry = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        health.Register(RbnTelnetFeed.HealthName);
        health.Register(RefreshRbnNodes.HealthName);
        await EnsureSchemaAsync(stoppingToken);
        await Task.WhenAll(
            record.RunAsync(options.Value.ActivityFlush, stoppingToken),
            RefreshLoopAsync(stoppingToken));
    }

    private async Task EnsureSchemaAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.EnsureSchemaAsync(stoppingToken);
                await refresh.LoadSavedAsync(stoppingToken);
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogSchemaFailed(logger, e);
                await Task.Delay(SchemaRetry, time, stoppingToken);
            }
        }
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await refresh.RefreshAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The list was read but could not be saved: the next read tries again.
                    LogRefreshFailed(logger, e);
                }
                await Task.Delay(options.Value.NodesInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "RBN tables or saved node list not ready, retrying")]
    private static partial void LogSchemaFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "RBN node list not saved")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
