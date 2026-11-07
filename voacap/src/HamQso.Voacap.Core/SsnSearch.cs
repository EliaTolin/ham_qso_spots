namespace HamQso.Voacap.Core;

/// <summary>
/// The search of the effective SSN (plan F71, 3.3): VOACAP's MUF of a 3000 km
/// circuit centred on the ionosonde (east-west) for a coarse grid of SSN (0 to 300
/// every 10), then every 1 around the best; the best is the SSN whose MUF is
/// nearest the measured one, on a tie the one nearest the monthly SSN. A grid,
/// not a bisection: VOACAP's MUF grows with the SSN but not strictly, and two
/// runs of voacapl are enough.
/// </summary>
public static class SsnSearch
{
    public const int Max = 300;
    private const int Step = 10;
    private const double HalfCircuitKm = 1500;

    public static IReadOnlyList<int> Coarse() => [.. Enumerable.Range(0, Max / Step + 1).Select(i => i * Step)];

    public static IReadOnlyList<int> Fine(int around) =>
        [.. Enumerable.Range(Math.Max(0, around - Step), Math.Min(Max, around + Step) - Math.Max(0, around - Step) + 1)];

    public static (int Ssn, double Muf) Best(IEnumerable<(int Ssn, double Muf)> mufs, double measured, double ssnMonth) =>
        mufs.OrderBy(m => Math.Round(Math.Abs(m.Muf - measured), 6)).ThenBy(m => Math.Abs(m.Ssn - ssnMonth)).ThenBy(m => m.Ssn).First();

    /// <summary>The ends of the circuit: 1500 km west and east of the ionosonde.</summary>
    public static (GeoPoint West, GeoPoint East) Circuit(GeoPoint ionosonde) =>
        (GreatCircle.Destination(ionosonde, 270, HalfCircuitKm), GreatCircle.Destination(ionosonde, 90, HalfCircuitKm));
}
