using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HamQso.Voacap.Core;

namespace HamQso.Voacap.Tests;

/// <summary>
/// voacapl without voacapl: answers a deck with one page per circuit, as the real
/// output is laid out. <see cref="Answer"/> gives the result of a circuit from its
/// receiver and SSN.
/// </summary>
public sealed partial class FakeEngine : IVoacapEngine
{
    public string Name => "voacapl 0.7.6";

    public List<(string Deck, IReadOnlyList<AntennaFile> Antennas)> Runs { get; } = [];

    public Func<GeoPoint, double, CircuitResult> Answer { get; set; } = (_, _) => new CircuitResult(0.5, 30, 20.0, "1F2");

    public Exception? Failure { get; set; }

    public Task<string> RunAsync(string deck, IReadOnlyList<AntennaFile> antennas, CancellationToken cancellationToken)
    {
        lock (Runs) Runs.Add((deck, antennas));
        if (Failure is not null) return Task.FromException<string>(Failure);
        var output = new StringBuilder();
        var ssn = 0.0;
        foreach (var line in deck.Split('\n'))
        {
            if (line.StartsWith("SUNSPOT", StringComparison.Ordinal)) ssn = double.Parse(line[10..].Trim().TrimEnd('.'), CultureInfo.InvariantCulture);
            var c = Circuit().Match(line);
            if (!c.Success) continue;
            var rx = new GeoPoint(Coord(c.Groups[1].Value, c.Groups[2].Value), Coord(c.Groups[3].Value, c.Groups[4].Value));
            output.Append(Page(Answer(rx, ssn)));
        }
        output.Append(" *****END OF RUN*****     VOACAP 16.1207W\n");
        return Task.FromResult(output.ToString());
    }

    public static string Page(CircuitResult r) =>
        "     CCIR Coefficients        ~METHOD 30   VOACAP L 16.1207W  PAGE   1\n" +
        $"  14.0{F(r.MufMhz ?? 0, "0.0")}{F(14.1, "0.0")}  0.0  0.0  0.0  0.0  0.0  0.0  0.0  0.0  0.0  0.0 FREQ\n" +
        $"      {(r.Mode ?? "-").PadLeft(5)}{(r.Mode ?? "-").PadLeft(5)}   -    -    -    -    -    -    -    -    -    -  MODE  \n" +
        $"      {F(r.SnrDbhz ?? -999, "0")}{F(r.SnrDbhz ?? -999, "0")}   -    -    -    -    -    -    -    -    -    -  SNR   \n" +
        $"      {F(r.Rel, "0.00")}{F(r.Rel, "0.00")}   -    -    -    -    -    -    -    -    -    -  REL   \n";

    private static string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture).PadLeft(5);

    private static double Coord(string value, string side) =>
        double.Parse(value, CultureInfo.InvariantCulture) * (side is "S" or "W" ? -1 : 1);

    [GeneratedRegex(@"^CIRCUIT\s+[\d.]+[NS]\s+[\d.]+[EW]\s+([\d.]+)([NS])\s+([\d.]+)([EW])")]
    private static partial Regex Circuit();
}
