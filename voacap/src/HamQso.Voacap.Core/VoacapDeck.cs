using System.Globalization;
using System.Text;

namespace HamQso.Voacap.Core;

/// <summary>A circuit of a deck: the receiver and the SSN for it.</summary>
public sealed record DeckCircuit(GeoPoint Receiver, double Ssn);

/// <summary>Everything a deck says: one transmitter, its antenna and power, one receive antenna, the circuits.</summary>
public sealed record DeckSpec(
    int Year,
    int Month,
    int UtcHour,
    double FrequencyMhz,
    ModelSettings Settings,
    GeoPoint Transmitter,
    string TransmitAntenna,
    double PowerW,
    string ReceiveAntenna,
    IReadOnlyList<DeckCircuit> Circuits);

/// <summary>
/// The input deck of voacapl, method 30 (fixed-width cards, as in voacapl's own
/// samples). voacapl takes the ANTENNA cards once, before the first EXECUTE: so a
/// deck has one transmitter antenna and power; then one CIRCUIT and EXECUTE per
/// receiver, with a SUNSPOT card where the SSN changes.
/// </summary>
public static class VoacapDeck
{
    public static string Build(DeckSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Circuits.Count == 0) throw new ArgumentException("a deck needs a circuit", nameof(spec));
        var hour = spec.UtcHour == 0 ? 24 : spec.UtcHour;
        var s = spec.Settings;
        var deck = new StringBuilder();
        Card(deck, "LINEMAX      55       number of lines-per-page");
        Card(deck, "COEFFS    CCIR");
        Card(deck, $"TIME      {I(hour)}{I(hour)}{I(1)}{I(1)}");
        Card(deck, $"MONTH     {I(spec.Year)}{F(spec.Month, "0.00", 6)}");
        Card(deck, Sunspot(spec.Circuits[0].Ssn));
        Card(deck, "LABEL     TX                  RX");
        Card(deck, Circuit(spec.Transmitter, spec.Circuits[0].Receiver));
        Card(deck, $"SYSTEM    {Dot(1)}{Dot(-s.NoiseDbw)}{F(s.MinTakeoffDeg, "0.00")}{Dot(90)}{F(s.RequiredSnrDbhz, "0.0")}{F(3, "0.00")}{F(0.1, "0.00")}");
        Card(deck, "FPROB      1.00 1.00 1.00 0.00");
        Card(deck, Antenna(1, spec.TransmitAntenna, spec.PowerW / 1000));
        Card(deck, Antenna(2, spec.ReceiveAntenna, 0));
        Card(deck, $"FREQUENCY {F(spec.FrequencyMhz, "0.00")}{string.Concat(Enumerable.Repeat(" 0.00", 10))}");
        Card(deck, "METHOD       30    0");
        Card(deck, "EXECUTE");
        for (var i = 1; i < spec.Circuits.Count; i++)
        {
            if (spec.Circuits[i].Ssn != spec.Circuits[i - 1].Ssn) Card(deck, Sunspot(spec.Circuits[i].Ssn));
            Card(deck, Circuit(spec.Transmitter, spec.Circuits[i].Receiver));
            Card(deck, "EXECUTE");
        }
        Card(deck, "QUIT");
        return deck.ToString();
    }

    private static string Sunspot(double ssn) => $"SUNSPOT   {Dot(ssn)}";

    /// <summary>A whole number with a point, as voacapl's F5.0 fields ("  118.").</summary>
    private static string Dot(double value) => (Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + ".").PadLeft(5);

    private static string Circuit(GeoPoint tx, GeoPoint rx) =>
        $"CIRCUIT   {F(Math.Abs(tx.Lat), "0.00")}{(tx.Lat < 0 ? 'S' : 'N')}{F(Math.Abs(tx.Lon), "0.00", 9)}{(tx.Lon < 0 ? 'W' : 'E')}" +
        $"{F(Math.Abs(rx.Lat), "0.00", 9)}{(rx.Lat < 0 ? 'S' : 'N')}{F(Math.Abs(rx.Lon), "0.00", 9)}{(rx.Lon < 0 ? 'W' : 'E')}  S     0";

    // ANTENNA, number, 1 transmit / 2 receive, 2-30 MHz, design frequency, [file of 21], azimuth, power kW.
    private static string Antenna(int number, string file, double kw) =>
        $"ANTENNA   {I(number)}{I(number)}{I(2)}{I(30)}{F(0, "0.000", 10)}[{file,-21}]{F(0, "0.0")}{F(kw, "0.0000", 10)}";

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture).PadLeft(5);

    private static string F(double value, string format, int width = 5) => value.ToString(format, CultureInfo.InvariantCulture).PadLeft(width);

    private static void Card(StringBuilder deck, string card) => deck.Append(card).Append('\n');
}
