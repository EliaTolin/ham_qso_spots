using System.Diagnostics;

namespace HamQso.Voacap.Core;

/// <summary>
/// POST /v1/predict: one voacapl run per scenario (its SSN, power and antenna; every
/// receiver a circuit), run at the same time. A run whose output has not one result
/// per receiver is an engine failure.
/// </summary>
public sealed class Predict(IVoacapEngine engine, TimeProvider time)
{
    public async Task<PredictResult> ExecuteAsync(PredictRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();
        var receive = AntennaFiles.For(request.ReceiveAntenna, request.FrequencyMhz);
        var runs = request.Scenarios.Select(async s =>
        {
            var transmit = AntennaFiles.For(s.Antenna, request.FrequencyMhz);
            var deck = VoacapDeck.Build(new DeckSpec(
                time.GetUtcNow().Year, request.Month, request.UtcHour, request.FrequencyMhz, request.Settings, request.Transmitter,
                transmit.Name, s.PowerW, receive.Name,
                [.. request.Receivers.Select(r => new DeckCircuit(new GeoPoint(r.Lat, r.Lon), s.Ssn))]));
            var circuits = VoacapOutput.Parse(await engine.RunAsync(deck, [transmit, receive], cancellationToken).ConfigureAwait(false));
            if (circuits.Count != request.Receivers.Count) throw new VoacapEngineException($"{circuits.Count} results for {request.Receivers.Count} circuits");
            IReadOnlyList<ReceiverResult> results = [.. request.Receivers.Zip(circuits, (r, c) => new ReceiverResult(r.Id, c.Rel, c.SnrDbhz, c.MufMhz, c.Mode))];
            return (s.Id, results);
        });
        var done = await Task.WhenAll(runs).ConfigureAwait(false);
        return new PredictResult(engine.Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, done.ToDictionary(d => d.Id, d => d.results, StringComparer.Ordinal));
    }
}
