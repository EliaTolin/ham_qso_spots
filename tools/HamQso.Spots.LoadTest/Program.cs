using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MQTTnet;

// Load test of the spots broker (F24): N apps connect over WebSocket with a
// Supabase token, subscribe like the app and stay connected. Steps grow until
// the last one; it stops at once if more than 1% of the connections fail or
// /health is no longer Healthy. Distinct usernames with one token: go-auth
// cannot answer from its cache, the server checks every connection.

var options = LoadOptions.Parse(args);
var token = Environment.GetEnvironmentVariable("TOKEN") is { Length: > 0 } t
    ? t
    : throw new InvalidOperationException("TOKEN is not set: TOKEN=$(scripts/mqtt-token.sh)");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var factory = new MqttClientFactory();
var clients = new List<IMqttClient>();
// First and every receipt of each distinct message (topic + payload), to see how long the fan-out takes.
var receipts = new ConcurrentDictionary<string, ConcurrentBag<long>>();
var clock = Stopwatch.StartNew();
var report = new List<string>();

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Broker {options.Broker}, steps {string.Join(", ", options.Steps)}, hold {options.Hold.TotalSeconds} s each"));
try
{
    foreach (var target in options.Steps)
    {
        var health = await HealthAsync(http, options.Health);
        if (health != "Healthy")
        {
            report.Add($"STOP before {target}: /health is {health}");
            break;
        }

        receipts.Clear();
        var toOpen = target - clients.Count;
        var connectTimes = new ConcurrentBag<double>();
        var failures = new ConcurrentBag<string>();
        using var gate = new SemaphoreSlim(options.Parallel);
        var opening = Enumerable.Range(clients.Count, toOpen).Select(async i =>
        {
            await gate.WaitAsync();
            try
            {
                var client = factory.CreateMqttClient();
                client.ApplicationMessageReceivedAsync += e =>
                {
                    var key = e.ApplicationMessage.Topic + "|" + e.ApplicationMessage.Payload.Length + "|" + e.ApplicationMessage.ConvertPayloadToString().GetHashCode(StringComparison.Ordinal);
                    receipts.GetOrAdd(key, _ => []).Add(clock.ElapsedMilliseconds);
                    return Task.CompletedTask;
                };
                var started = clock.Elapsed;
                var result = await client.ConnectAsync(new MqttClientOptionsBuilder()
                    .WithWebSocketServer(o => o.WithUri(options.Broker))
                    .WithCredentials($"load-{i}", token)
                    .WithTimeout(TimeSpan.FromSeconds(30))
                    .Build());
                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    failures.Add(result.ResultCode.ToString());
                    client.Dispose();
                    return;
                }
                connectTimes.Add((clock.Elapsed - started).TotalMilliseconds);
                await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("spots/dx/#").WithTopicFilter("spots/pota").Build());
                lock (clients) clients.Add(client);
            }
            catch (Exception e)
            {
                failures.Add(e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                gate.Release();
            }
        });
        var ramp = Stopwatch.StartNew();
        await Task.WhenAll(opening);
        ramp.Stop();

        // Live messages only: the retained ones arrive at subscribe time.
        await Task.Delay(TimeSpan.FromSeconds(3));
        receipts.Clear();
        await Task.Delay(options.Hold);

        var spread = receipts.Values.Where(r => r.Count >= Math.Max(2, clients.Count / 2))
            .Select(r => (double)(r.Max() - r.Min())).Order().ToArray();
        var connected = clients.Count(c => c.IsConnected);
        health = await HealthAsync(http, options.Health);
        var failRate = toOpen == 0 ? 0 : (double)failures.Count / toOpen;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{target,5} apps | opened {toOpen - failures.Count}/{toOpen} in {ramp.Elapsed.TotalSeconds:0.0} s | connected now {connected} | " +
            $"connect p50 {Pct(connectTimes, 0.5):0} ms p95 {Pct(connectTimes, 0.95):0} ms | " +
            $"live messages {spread.Length}, fan-out p50 {Pct(spread, 0.5):0} ms p95 {Pct(spread, 0.95):0} ms max {(spread.Length > 0 ? spread[^1] : 0):0} ms | health {health}");
        Console.WriteLine(line);
        report.Add(line);
        foreach (var f in failures.GroupBy(f => f).OrderByDescending(g => g.Count()).Take(3)) Console.WriteLine($"      {f.Count()} x {f.Key}");

        if (failRate > options.MaxFailRate || health != "Healthy" || connected < target * (1 - options.MaxFailRate))
        {
            report.Add(string.Create(CultureInfo.InvariantCulture, $"STOP at {target}: failures {failRate:P1}, connected {connected}, health {health}"));
            break;
        }
    }
}
finally
{
    Console.WriteLine($"Closing {clients.Count} connections...");
    await Task.WhenAll(clients.Select(async c =>
    {
        try
        {
            if (c.IsConnected) await c.DisconnectAsync();
        }
        catch (Exception)
        {
            // Closing anyway.
        }
        c.Dispose();
    }));
    Console.WriteLine();
    report.ForEach(Console.WriteLine);
}

static double Pct(IEnumerable<double> values, double p)
{
    var sorted = values.Order().ToArray();
    return sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
}

static async Task<string> HealthAsync(HttpClient http, Uri health)
{
    try
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(health));
        return doc.RootElement.GetProperty("status").GetString() ?? "?";
    }
    catch (Exception e)
    {
        return "unreachable (" + e.GetType().Name + ")";
    }
}

internal sealed record LoadOptions(string Broker, Uri Health, int[] Steps, TimeSpan Hold, int Parallel, double MaxFailRate)
{
    public static LoadOptions Parse(string[] args)
    {
        string Arg(string name, string fallback)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        return new LoadOptions(
            Arg("--broker", "wss://spot.hamqso.app/mqtt"),
            new Uri(Arg("--health", "https://spot.hamqso.app/health")),
            [.. Arg("--steps", "100,250,500,1000").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))],
            TimeSpan.FromSeconds(int.Parse(Arg("--hold", "60"), CultureInfo.InvariantCulture)),
            int.Parse(Arg("--parallel", "25"), CultureInfo.InvariantCulture),
            0.01);
    }
}
