using HamQso.Voacap.Core;

namespace HamQso.Voacap.Tests;

public class RequestRulesTests
{
    private static PredictRequest Valid(int receivers = 1) => new(
        11, 14, 14.045, new ModelSettings(30, -150, 3, 24), new AntennaSpec("isotropic", 0, null), new GeoPoint(45.6, 9.2),
        [new Scenario("theory", 118, 100, new AntennaSpec("dipole", null, 10))],
        [.. Enumerable.Range(0, receivers).Select(i => new Receiver($"R{i}", 49.98, 14.46))]);

    [Fact]
    public void A_valid_request_has_no_error() => Assert.Null(RequestRules.Check(Valid()));

    [Fact]
    public void Six_hundred_receivers_are_allowed_one_more_is_too_many()
    {
        Assert.Null(RequestRules.Check(Valid(600)));
        Assert.Equal("too_many_receivers", RequestRules.Check(Valid(601)));
    }

    public static TheoryData<string, PredictRequest> Invalid => new()
    {
        { "month", Valid() with { Month = 13 } },
        { "hour", Valid() with { UtcHour = 24 } },
        { "frequency low", Valid() with { FrequencyMhz = 1.5 } },
        { "frequency high", Valid() with { FrequencyMhz = 30.1 } },
        { "method", Valid() with { Settings = new ModelSettings(25, -150, 3, 24) } },
        { "noise", Valid() with { Settings = new ModelSettings(30, -90, 3, 24) } },
        { "angle", Valid() with { Settings = new ModelSettings(30, -150, 45, 24) } },
        { "latitude", Valid() with { Transmitter = new GeoPoint(91, 0) } },
        { "longitude", Valid() with { Transmitter = new GeoPoint(0, 181) } },
        { "no scenario", Valid() with { Scenarios = [] } },
        { "two scenarios same id", Valid() with { Scenarios = [new Scenario("a", 1, 100, new AntennaSpec("dipole", null, null)), new Scenario("a", 2, 100, new AntennaSpec("dipole", null, null))] } },
        { "five scenarios", Valid() with { Scenarios = [.. Enumerable.Range(0, 5).Select(i => new Scenario($"s{i}", 1, 100, new AntennaSpec("dipole", null, null)))] } },
        { "scenario id", Valid() with { Scenarios = [new Scenario("the ory", 1, 100, new AntennaSpec("dipole", null, null))] } },
        { "ssn", Valid() with { Scenarios = [new Scenario("a", 301, 100, new AntennaSpec("dipole", null, null))] } },
        { "power", Valid() with { Scenarios = [new Scenario("a", 1, 0, new AntennaSpec("dipole", null, null))] } },
        { "model", Valid() with { Scenarios = [new Scenario("a", 1, 100, new AntennaSpec("hexBeam", null, null))] } },
        { "gain", Valid() with { Scenarios = [new Scenario("a", 1, 100, new AntennaSpec("yagi", 40, null))] } },
        { "height", Valid() with { Scenarios = [new Scenario("a", 1, 100, new AntennaSpec("yagi", null, 0))] } },
        { "receive model", Valid() with { ReceiveAntenna = new AntennaSpec("nope", null, null) } },
        { "no receiver", Valid() with { Receivers = [] } },
        { "receiver id", Valid() with { Receivers = [new Receiver("", 1, 1)] } },
        { "receiver same id", Valid() with { Receivers = [new Receiver("A", 1, 1), new Receiver("A", 2, 2)] } },
        { "receiver latitude", Valid() with { Receivers = [new Receiver("A", -91, 1)] } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Anything_out_of_range_is_an_invalid_request(string what, PredictRequest request) =>
        Assert.True(RequestRules.Check(request) == "invalid_request", what);

    [Theory]
    [InlineData(0, 11, 41.9, 12.5, 18.2, 118, null)]
    [InlineData(13, 11, 41.9, 12.5, 18.2, 118, "invalid_request")]
    [InlineData(11, 24, 41.9, 12.5, 18.2, 118, "invalid_request")]
    [InlineData(11, 14, 95, 12.5, 18.2, 118, "invalid_request")]
    [InlineData(11, 14, 41.9, 12.5, 0.5, 118, "invalid_request")]
    [InlineData(11, 14, 41.9, 12.5, 18.2, -1, "invalid_request")]
    public void An_effective_ssn_request_in_range(int month, int hour, double lat, double lon, double muf, double ssn, string? error) =>
        Assert.Equal(error, RequestRules.Check(new EffectiveSsnRequest(month == 0 ? 11 : month, hour, new GeoPoint(lat, lon), muf, ssn)));
}
