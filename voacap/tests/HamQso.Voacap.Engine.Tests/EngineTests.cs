using System.Diagnostics;
using System.Text.Json;
using HamQso.Voacap.Core;
using HamQso.Voacap.Host;
using Microsoft.Extensions.Options;

namespace HamQso.Voacap.Engine.Tests;

/// <summary>
/// The service with the real voacapl, in the Docker image of the tests
/// (voacap/test-engine.sh): the deck is read by voacapl as written, the output
/// parsed, and the numbers make physical sense. Not run on a machine without voacapl.
/// </summary>
public sealed class EngineTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ProcessVoacapEngine engine = new(Options.Create(new VoacapOptions
    {
        Token = "unused-unused-unused",
        Itshfbc = Environment.GetEnvironmentVariable("Voacap__Itshfbc") ?? "/opt/itshfbc",
        Engine = "voacapl 0.7.6",
    }));

    public void Dispose() => engine.Dispose();

    private static PredictRequest Fixture() =>
        JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "voacap_predict_v1.json")))
            .GetProperty("request").Deserialize<PredictRequest>(VoacapEndpoints.Json)!;

    private Task<PredictResult> Predict(PredictRequest r) => new Predict(engine, TimeProvider.System).ExecuteAsync(r, Ct);

    [Fact]
    public async Task The_request_of_the_contract_gives_three_sensible_predictions()
    {
        var r = await Predict(Fixture());

        var (theory, sky, station) = (r.Results["theory"].Single(), r.Results["sky"].Single(), r.Results["station"].Single());
        // Milan to Prague (625 km) on 20 m, November afternoon, SSN 118: an open, one-hop F2 path.
        Assert.InRange(theory.Rel, 0.5, 1);
        Assert.StartsWith("1", theory.Mode, StringComparison.Ordinal);
        Assert.InRange(theory.MufMhz!.Value, 10, 35);
        Assert.True(sky.MufMhz >= theory.MufMhz, "a higher SSN, a higher MUF");
        Assert.True(station.SnrDbhz > sky.SnrDbhz, "a yagi over a dipole, more signal");
    }

    [Fact]
    public async Task Six_hundred_receivers_and_three_scenarios_within_the_10_seconds_of_the_contract()
    {
        var rnd = new Random(71);
        var request = Fixture() with
        {
            Receivers = [.. Enumerable.Range(0, 600).Select(i => new Receiver($"S{i}", rnd.NextDouble() * 140 - 70, rnd.NextDouble() * 360 - 180))],
        };
        var watch = Stopwatch.StartNew();

        var r = await Predict(request);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"{watch.Elapsed}");
        Assert.All(r.Results.Values, list => Assert.Equal(600, list.Count));
        Assert.All(r.Results.Values.SelectMany(l => l), x => Assert.InRange(x.Rel, 0, 1));
    }

    [Fact]
    public async Task Every_antenna_model_160_m_and_midnight_are_read_by_voacapl()
    {
        foreach (var model in new[] { "isotropic", "dipole", "vertical", "yagi", "loop" })
        {
            var request = Fixture() with
            {
                FrequencyMhz = 1.83,
                UtcHour = 0,
                Scenarios = [new Scenario("a", 50, 400, new AntennaSpec(model, model == "isotropic" ? 2 : null, model == "vertical" ? null : 15))],
            };

            var r = await Predict(request);

            Assert.InRange(r.Results["a"].Single().Rel, 0, 1);
        }
    }

    [Fact]
    public async Task A_receive_antenna_with_gain_gives_more_signal()
    {
        var flat = await Predict(Fixture());
        var gain = await Predict(Fixture() with { ReceiveAntenna = new AntennaSpec("isotropic", 6) });

        Assert.InRange(gain.Results["theory"][0].SnrDbhz!.Value - flat.Results["theory"][0].SnrDbhz!.Value, 4, 8);
    }

    [Fact]
    public async Task The_effective_ssn_gives_back_the_measured_muf_and_grows_with_it()
    {
        var search = new EffectiveSsn(engine);
        var rome = new GeoPoint(41.9, 12.5);

        // Rome, November, 14 UTC: VOACAP's MUF of the 3000 km circuit goes from 22 MHz (SSN 0) to 45 MHz (SSN 300).
        var low = await search.ExecuteAsync(new EffectiveSsnRequest(11, 14, rome, 30, 118), Ct);
        var high = await search.ExecuteAsync(new EffectiveSsnRequest(11, 14, rome, 36, 118), Ct);

        Assert.Equal(2, low.Iterations);
        Assert.InRange(low.PredictedMufdMhz, 29.7, 30.3);
        Assert.InRange(high.PredictedMufdMhz, 35.7, 36.3);
        Assert.True(high.SsnEffective > low.SsnEffective);
    }

    [Fact]
    public async Task A_measured_muf_below_what_voacap_gives_at_ssn_0_is_ssn_0()
    {
        var r = await new EffectiveSsn(engine).ExecuteAsync(new EffectiveSsnRequest(11, 14, new GeoPoint(41.9, 12.5), 16, 118), Ct);

        Assert.Equal(0, r.SsnEffective);
        Assert.True(r.PredictedMufdMhz > 16);
    }

    [Fact]
    public async Task A_deck_voacapl_cannot_read_fails_at_once_with_its_message()
    {
        var deck = VoacapDeck.Build(new DeckSpec(2026, 11, 14, 14.045, new ModelSettings(30, -150, 3, 24), new GeoPoint(45.6, 9.2), "hq/does-not-exist", 100, AntennaFiles.Isotrope,
            [new DeckCircuit(new GeoPoint(49.98, 14.46), 118)]));
        var watch = Stopwatch.StartNew();

        var e = await Assert.ThrowsAsync<VoacapEngineException>(() => engine.RunAsync(deck, [], Ct));

        Assert.Contains("OPENing antenna file", e.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }
}
