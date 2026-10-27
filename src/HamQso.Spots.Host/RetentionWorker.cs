using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Host;

/// <summary>Keeps the archive to the 7-day window: deletes older spots every hour.</summary>
public sealed partial class RetentionWorker(ISpotStore store, TimeProvider time, ILogger<RetentionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private static readonly TimeSpan Every = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await store.DeleteOlderThanAsync(time.GetUtcNow() - Window, stoppingToken);
                LogDeleted(logger, deleted);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
            await Task.Delay(Every, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention: {Deleted} spots older than 7 days deleted")]
    private static partial void LogDeleted(ILogger logger, int deleted);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retention failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
