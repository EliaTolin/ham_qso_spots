using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ingestion;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Alerts;
using HamQso.Spots.Infrastructure.Mqtt;
using HamQso.Spots.Infrastructure.Postgres;

namespace HamQso.Spots.Host;

/// <summary>
/// Starts every configured source the same way and hands its batches to
/// <see cref="IngestSpots"/>. A source that throws is logged and restarted;
/// the others go on.
/// </summary>
public sealed partial class IngestionWorker(
    IEnumerable<ISpotSource> sources,
    IngestSpots ingest,
    PostgresSpotStore store,
    SourceHealthRegistry health,
    TimeProvider time,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await store.EnsureSchemaAsync(stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Without the archive the broker still gets the spots.
            LogSchemaFailed(logger, e);
        }
        await ingest.StartAsync(stoppingToken);
        var list = sources.ToArray();
        foreach (var source in list) health.Register(source.Name);
        health.Register(MqttSpotPublisher.HealthName);
        health.Register(SupabaseAlertNotifier.HealthName);
        await Task.WhenAll(list.Select(s => RunAsync(s, stoppingToken)));
    }

    private async Task RunAsync(ISpotSource source, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var batch in source.ReadAsync(health, stoppingToken))
                {
                    await ingest.HandleAsync(batch, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                health.Failed(source.Name, e.Message);
                LogSourceCrashed(logger, e, source.Name);
            }
            await Task.Delay(RestartDelay, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Postgres schema could not be applied")]
    private static partial void LogSchemaFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Source {Source} stopped; restarting")]
    private static partial void LogSourceCrashed(ILogger logger, Exception exception, string source);
}
