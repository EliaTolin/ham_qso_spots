using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Domain.Protection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace HamQso.Spots.Infrastructure.Protection;

/// <summary>
/// The PSKReporter live feed (MQTT, mqtt.pskreporter.info, run by M0LTE with the permission of the owner of
/// pskreporter.info: public, no account, no polling, no stated terms or limits). One connection for all the
/// users, narrow on purpose: it subscribes only to the topics of the protected callsigns
/// (<see cref="PskTopics"/>, never the whole feed); a message that is still not about one is dropped at once
/// (<see cref="ProtectionCollector.OfferPsk"/>). Reconnects by itself; /health "protection_pskreporter" comes from
/// <see cref="Probe"/>.
/// </summary>
public sealed partial class PskReporterFeed(
    IOptions<ProtectionOptions> options,
    ProtectedCallsignRegistry registry,
    ProtectionCollector collector,
    TimeProvider time,
    ILogger<PskReporterFeed> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Resync = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);
    private const int SubscribeChunk = 100;

    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private readonly HashSet<string> subscribed = new(StringComparer.Ordinal);
    private string? lastError;

    public bool IsConnected => client.IsConnected;

    private string Host => options.Value.PskReporterHost;

    /// <summary>The probe for the health of the "pskreporter" signal.</summary>
    public IProtectionProbe Probe => new FeedProbe(this);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        client.ApplicationMessageReceivedAsync += OnMessage;
        var backoff = TimeSpan.FromSeconds(5);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                    lastError = null;
                    backoff = TimeSpan.FromSeconds(5);
                    await StayAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = e.Message;
                    LogDropped(logger, options.Value.PskReporterHost, e.Message);
                }
                subscribed.Clear();
                await Task.Delay(backoff, time, cancellationToken).ConfigureAwait(false);
                backoff = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, backoff.Ticks * 2));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One message of the feed: dropped unless it is a report about a protected callsign.</summary>
    public void Handle(ReadOnlySpan<byte> payload)
    {
        if (PskReportParser.Parse(payload) is { } report) collector.OfferPsk(report, time.GetUtcNow());
    }

    private Task OnMessage(MqttApplicationMessageReceivedEventArgs e)
    {
        Handle(System.Buffers.BuffersExtensions.ToArray(e.ApplicationMessage.Payload));
        return Task.CompletedTask;
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var builder = new MqttClientOptionsBuilder().WithTcpServer(o.PskReporterHost, o.PskReporterPort).WithCleanSession().WithTimeout(ConnectTimeout);
        if (o.PskReporterTls) builder = builder.WithTlsOptions(t => t.UseTls());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        await client.ConnectAsync(builder.Build(), timeout.Token).ConfigureAwait(false);
    }

    private async Task StayAsync(CancellationToken cancellationToken)
    {
        var syncedVersion = -1;
        while (client.IsConnected)
        {
            if (registry.Version != syncedVersion)
            {
                syncedVersion = registry.Version;
                await SyncAsync(cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(Resync, time, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("connection to the PSKReporter feed lost");
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var wanted = PskTopics.For(WantedCallsigns()).ToHashSet(StringComparer.Ordinal);
        foreach (var chunk in subscribed.Except(wanted).Chunk(SubscribeChunk))
        {
            var unsub = new MqttClientUnsubscribeOptionsBuilder();
            foreach (var t in chunk) unsub.WithTopicFilter(t);
            await client.UnsubscribeAsync(unsub.Build(), cancellationToken).ConfigureAwait(false);
            subscribed.ExceptWith(chunk);
        }
        foreach (var chunk in wanted.Except(subscribed).Chunk(SubscribeChunk))
        {
            var sub = new MqttClientSubscribeOptionsBuilder();
            foreach (var t in chunk) sub.WithTopicFilter(t, MqttQualityOfServiceLevel.AtMostOnce);
            await client.SubscribeAsync(sub.Build(), cancellationToken).ConfigureAwait(false);
            subscribed.UnionWith(chunk);
        }
    }

    /// <summary>The callsigns subscribed: all of them up to the cap, in alphabetical order (the rest is reported by <see cref="CapProblem"/>).</summary>
    public IReadOnlyList<string> WantedCallsigns() => [.. registry.Current.Bases.Order(StringComparer.Ordinal).Take(options.Value.PskReporterMaxCallsigns)];

    /// <summary>"cap exceeded: N &gt; M" when there are more protected callsigns than the cap, else null.</summary>
    public string? CapProblem() =>
        registry.Current.Count > options.Value.PskReporterMaxCallsigns ? $"cap exceeded: {registry.Current.Count} > {options.Value.PskReporterMaxCallsigns}" : null;

    public async ValueTask DisposeAsync()
    {
        if (client.IsConnected) await client.DisconnectAsync().ConfigureAwait(false);
        client.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PSKReporter feed {Host} not available, retrying: {Reason}")]
    private static partial void LogDropped(ILogger logger, string host, string reason);

    private sealed class FeedProbe(PskReporterFeed feed) : IProtectionProbe
    {
        public ProtectionSignal Signal => ProtectionSignal.PskReporter;

        public string? Problem() => feed.IsConnected ? feed.CapProblem() : $"not connected to {feed.Host}: {feed.lastError ?? "starting"}";

        /// <summary>The cap is a standing condition: said once an hour, not at every batch.</summary>
        public TimeSpan? ReportEvery => feed.IsConnected && feed.CapProblem() is not null ? TimeSpan.FromHours(1) : null;
    }
}
