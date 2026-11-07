namespace HamQso.Voacap.Core;

/// <summary>A point on the Earth, degrees, north and east positive.</summary>
public sealed record GeoPoint(double Lat, double Lon);

/// <summary>An antenna of the service: <c>isotropic</c>, <c>dipole</c>, <c>vertical</c>, <c>yagi</c>, <c>loop</c>; gain (dBi) and height (m) optional.</summary>
public sealed record AntennaSpec(string Model, double? GainDbi = null, double? HeightM = null);

/// <summary>Method (30 only), man-made noise at 3 MHz (dBW), minimum take-off angle (degrees), required SNR (dB-Hz).</summary>
public sealed record ModelSettings(int Method, double NoiseDbw, double MinTakeoffDeg, double RequiredSnrDbhz);

/// <summary>One prediction of the same transmitter: its SSN, power and antenna.</summary>
public sealed record Scenario(string Id, double Ssn, double PowerW, AntennaSpec Antenna);

public sealed record Receiver(string Id, double Lat, double Lon);

/// <summary>POST /v1/predict: one transmitter, a few scenarios, many receivers (plan F71, 3.3).</summary>
public sealed record PredictRequest(
    int Month,
    int UtcHour,
    double FrequencyMhz,
    ModelSettings Settings,
    AntennaSpec ReceiveAntenna,
    GeoPoint Transmitter,
    IReadOnlyList<Scenario> Scenarios,
    IReadOnlyList<Receiver> Receivers);

/// <summary>What voacapl says of a circuit at the frequency asked: reliability (0..1), median SNR (dB-Hz, null without a path), circuit MUF, mode.</summary>
public sealed record CircuitResult(double Rel, int? SnrDbhz, double? MufMhz, string? Mode);

public sealed record ReceiverResult(string Id, double Rel, int? SnrDbhz, double? MufMhz, string? Mode);

public sealed record PredictResult(string Engine, long ElapsedMs, IReadOnlyDictionary<string, IReadOnlyList<ReceiverResult>> Results);

/// <summary>POST /v1/effective-ssn: the SSN with which VOACAP gives the MUF(3000) measured by an ionosonde.</summary>
public sealed record EffectiveSsnRequest(int Month, int UtcHour, GeoPoint Ionosonde, double MeasuredMufdMhz, double SsnMonth);

/// <summary><see cref="Iterations"/>: the runs of voacapl it took.</summary>
public sealed record EffectiveSsnResult(string Engine, int SsnEffective, double PredictedMufdMhz, int Iterations);

/// <summary>An antenna file for voacapl: its name in the deck (at most 21 characters) and its text (null: a file of voacapl).</summary>
public sealed record AntennaFile(string Name, string? Content);

/// <summary>voacapl failed: the message is its own, short.</summary>
public sealed class VoacapEngineException : Exception
{
    public VoacapEngineException()
    {
    }

    public VoacapEngineException(string message)
        : base(message)
    {
    }

    public VoacapEngineException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>Runs voacapl: the deck, with the antenna files it names, gives the text of its output.</summary>
public interface IVoacapEngine
{
    /// <summary>"voacapl 0.7.6".</summary>
    string Name { get; }

    /// <summary>The output of a finished run; <see cref="VoacapEngineException"/> when it does not finish.</summary>
    Task<string> RunAsync(string deck, IReadOnlyList<AntennaFile> antennas, CancellationToken cancellationToken);
}
