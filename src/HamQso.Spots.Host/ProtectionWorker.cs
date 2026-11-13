using HamQso.Spots.Application.Health;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Domain.Protection;
using HamQso.Spots.Infrastructure.Protection;
using Microsoft.Extensions.Options;

namespace HamQso.Spots.Host;

/// <summary>
/// F107, callsign protection: keeps the list of protected callsigns fresh, listens to the RBN feed (the same
/// connection of everyone else) and to PSKReporter (when its flag is on), and takes what was heard to the
/// backend in the limits of <see cref="ProtectionLimits"/>. The cluster spots arrive from
/// <see cref="HamQso.Spots.Application.Ingestion.IngestSpots"/> by <see cref="IProtectionTap"/>.
/// It does not decide who is on air and sends no notification.
/// </summary>
public sealed partial class ProtectionWorker(
    ProtectedCallsignRegistry registry,
    ProtectionCollector collector,
    ProtectionDispatcher dispatcher,
    IRbnFeed rbn,
    IEnumerable<IProtectionProbe> probes,
    IOptions<ProtectionOptions> options,
    SourceHealthRegistry health,
    TimeProvider time,
    ILogger<ProtectionWorker> logger,
    PskReporterFeed? psk = null) : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        health.Register(ProtectionDispatcher.HealthName);
        health.Register(ProtectedCallsignRegistry.HealthName);
        foreach (var probe in probes) health.Register($"{ProtectionDispatcher.HealthName}_{probe.Signal.Wire()}");
        try
        {
            await Task.WhenAll(
                RefreshLoopAsync(stoppingToken),
                ReadRbnAsync(stoppingToken),
                DispatchLoopAsync(stoppingToken),
                psk?.RunAsync(stoppingToken) ?? Task.CompletedTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await dispatcher.FlushAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogFlushFailed(logger, e);
        }
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await registry.RefreshAsync(stoppingToken);
            await Task.Delay(options.Value.CallsignsInterval, time, stoppingToken);
        }
    }

    private async Task DispatchLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.TickAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogTickFailed(logger, e);
            }
            await Task.Delay(options.Value.Tick, time, stoppingToken);
        }
    }

    private async Task ReadRbnAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var line in rbn.ReadAsync(stoppingToken)) collector.OfferRbn(line);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogRbnStopped(logger, e);
            }
            await Task.Delay(RestartDelay, time, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Protection: dispatch failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Protection: RBN reader stopped, restarting")]
    private static partial void LogRbnStopped(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Protection: the last uses were not sent at stop")]
    private static partial void LogFlushFailed(ILogger logger, Exception exception);
}
