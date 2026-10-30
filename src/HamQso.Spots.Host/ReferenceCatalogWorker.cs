using HamQso.Spots.Application.Catalog;

namespace HamQso.Spots.Host;

/// <summary>
/// F53.3: at start and every <see cref="CheckEvery"/> asks <see cref="RefreshReferenceCatalog"/>
/// (which rebuilds the file only once a week). A failure is logged and tried
/// at the next check; the file there is stays served.
/// </summary>
public sealed partial class ReferenceCatalogWorker(RefreshReferenceCatalog refresh, TimeProvider time, ILogger<ReferenceCatalogWorker> logger) : BackgroundService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await refresh.RunAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
            await Task.Delay(CheckEvery, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reference catalog: refresh failed, kept the file there is")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
