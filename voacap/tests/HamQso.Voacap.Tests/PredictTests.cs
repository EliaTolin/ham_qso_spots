using HamQso.Voacap.Core;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Voacap.Tests;

public class PredictTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PredictRequest Request() => new(
        11, 14, 14.045, new ModelSettings(30, -150, 3, 24), new AntennaSpec("isotropic", 0, null), new GeoPoint(45.6, 9.2),
        [
            new Scenario("theory", 118, 100, new AntennaSpec("dipole", null, 10)),
            new Scenario("station", 131, 100, new AntennaSpec("yagi", 8.5, 12)),
        ],
        [new Receiver("OK4YL", 49.98, 14.46), new Receiver("Z61DX", 42.67, 21.17)]);

    [Fact]
    public async Task One_run_per_scenario_with_its_ssn_power_and_antenna_and_every_receiver()
    {
        var engine = new FakeEngine { Answer = (rx, ssn) => new CircuitResult(ssn / 1000 + (rx.Lat > 45 ? 0.5 : 0.1), (int)ssn, 21.0, "1F2") };

        var result = await new Predict(engine, new FakeTimeProvider()).ExecuteAsync(Request(), Ct);

        Assert.Equal(2, engine.Runs.Count);
        Assert.Equal("voacapl 0.7.6", result.Engine);
        Assert.Equal(["OK4YL", "Z61DX"], result.Results["theory"].Select(r => r.Id));
        Assert.Equal(new ReceiverResult("OK4YL", 0.62, 118, 21.0, "1F2"), result.Results["theory"][0]);
        Assert.Equal(new ReceiverResult("Z61DX", 0.23, 131, 21.0, "1F2"), result.Results["station"][1]);
        var station = engine.Runs.Single(r => r.Deck.Contains("SUNSPOT    131.", StringComparison.Ordinal));
        Assert.Contains(station.Antennas, a => a.Content!.Contains("  24    [ 2]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_that_fails_fails_the_prediction()
    {
        var engine = new FakeEngine { Failure = new VoacapEngineException("PAUSE error in readant") };

        var e = await Assert.ThrowsAsync<VoacapEngineException>(() => new Predict(engine, new FakeTimeProvider()).ExecuteAsync(Request(), Ct));

        Assert.Equal("PAUSE error in readant", e.Message);
    }

    [Fact]
    public async Task An_output_with_fewer_circuits_than_asked_is_an_engine_failure()
    {
        var engine = new ShortEngine();

        await Assert.ThrowsAsync<VoacapEngineException>(() => new Predict(engine, new FakeTimeProvider()).ExecuteAsync(Request(), Ct));
    }

    private sealed class ShortEngine : IVoacapEngine
    {
        public string Name => "voacapl";

        public Task<string> RunAsync(string deck, IReadOnlyList<AntennaFile> antennas, CancellationToken cancellationToken) =>
            Task.FromResult(FakeEngine.Page(new CircuitResult(0.5, 30, 20, "1F2")) + " *****END OF RUN*****\n");
    }
}

public class EffectiveSsnTests
{
    [Fact]
    public async Task The_ssn_with_which_voacap_gives_the_measured_muf_coarse_then_fine()
    {
        // A MUF that grows with the SSN, as in VOACAP: 10 MHz at 0, +0.06 per unit.
        var engine = new FakeEngine { Answer = (_, ssn) => new CircuitResult(0.5, 30, Math.Round(10 + ssn * 0.06, 1), "1F2") };

        var r = await new EffectiveSsn(engine).ExecuteAsync(new EffectiveSsnRequest(11, 14, new GeoPoint(41.9, 12.5), 18.2, 118), TestContext.Current.CancellationToken);

        // 136 and 137 both give 18.2: the one nearer the monthly SSN.
        Assert.Equal(new EffectiveSsnResult("voacapl 0.7.6", 136, 18.2, 2), r);
        Assert.Equal(31, engine.Runs[0].Deck.Split("EXECUTE").Length - 1);
        Assert.Equal(21, engine.Runs[1].Deck.Split("EXECUTE").Length - 1);
    }
}
