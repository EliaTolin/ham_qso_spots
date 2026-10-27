using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>
/// Live spots from one DXSpider-compatible telnet node. Logs in with the
/// server callsign, asks the backlog with sh/dx, then streams "DX de" lines.
/// A dropped or mute connection is reopened with exponential backoff.
/// </summary>
public sealed class TelnetNodeSource(TelnetNodeOptions node, TelnetOptions options, TimeProvider time) : ISpotSource
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public string Name => node.Name;

    public async IAsyncEnumerable<SpotBatch> ReadAsync(ISourceHealth health, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(health);
        var channel = Channel.CreateUnbounded<SpotBatch>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var loop = RunAsync(channel.Writer, health, cancellationToken);
        await foreach (var batch in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return batch;
        }
        await loop.ConfigureAwait(false);
    }

    private async Task RunAsync(ChannelWriter<SpotBatch> writer, ISourceHealth health, CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunSessionAsync(writer, health, () => attempt = 0, cancellationToken).ConfigureAwait(false);
                    health.Failed(Name, "connection closed");
                }
                catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    health.Failed(Name, e.Message);
                }
                var seconds = Math.Min(options.MaxBackoff.TotalSeconds, 2 << Math.Min(attempt++, 6));
                await Task.Delay(TimeSpan.FromSeconds(seconds), time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task RunSessionAsync(ChannelWriter<SpotBatch> writer, ISourceHealth health, Action onSpots, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient { NoDelay = true };
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(options.ConnectTimeout);
            await tcp.ConnectAsync(node.Host, node.Port, connect.Token).ConfigureAwait(false);
        }
        var stream = tcp.GetStream();
        var session = new TelnetSession(options, time.GetUtcNow());
        var buffer = new byte[4096];

        while (true)
        {
            var now = time.GetUtcNow();
            var wait = session.NextWait(now);
            using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var timer = new CancellationTokenSource(wait, time);
            using var link = timer.Token.Register(read.Cancel);
            int count;
            try
            {
                count = await stream.ReadAsync(buffer, read.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                var command = session.OnSilence(time.GetUtcNow())
                    ?? throw new TimeoutException($"no data from {node.Host} for {options.IdleTimeout}");
                await SendAsync(stream, command, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (count == 0) return;

            var replies = new List<byte>();
            var data = session.Decoder.Decode(buffer.AsSpan(0, count), replies);
            if (replies.Count > 0) await stream.WriteAsync(replies.ToArray(), cancellationToken).ConfigureAwait(false);
            var (spots, reply) = session.OnText(Latin1.GetString(data), Name, time.GetUtcNow());
            if (reply is not null) await SendAsync(stream, reply, cancellationToken).ConfigureAwait(false);
            if (spots.Count > 0)
            {
                onSpots();
                health.Succeeded(Name);
                await writer.WriteAsync(new SpotBatch(Name, spots), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task SendAsync(NetworkStream stream, string line, CancellationToken cancellationToken) =>
        stream.WriteAsync(Latin1.GetBytes(line + "\r\n"), cancellationToken).AsTask();
}
