namespace HamQso.Spots.VoacapCalibration;

/// <summary>
/// One simulated test for the app's titles (calibration_tests.csv): listening
/// skimmers, expected (sum of the theory's probabilities), heard, the app's margin
/// (only with at least 3 expected), farthest heard (km), continents heard, surprises
/// (heard with a theory below 30%, the app's rule).
/// </summary>
public sealed record TestRow(int Listening, double ExpectedTheory, int Heard, double? Margin, double FarthestKm, int Continents, int Surprises)
{
    public const double SurpriseBelow = 0.3;

    public static TestRow Of(CalibrationEvent e, IReadOnlyDictionary<string, double> rel)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(rel);
        var listening = e.Listening.Where(l => rel.ContainsKey(l.Call)).ToArray();
        var heard = listening.Where(l => e.Heard.Contains(l.Call)).ToArray();
        var expected = listening.Sum(l => rel[l.Call]);
        return new TestRow(
            listening.Length,
            expected,
            heard.Length,
            expected >= Score.MinExpected ? (heard.Length - expected) / expected : null,
            heard.Length == 0 ? 0 : heard.Max(h => Geo.DistanceKm(e.StationPosition.Lat, e.StationPosition.Lon, h.Position!.Lat, h.Position.Lon)),
            heard.Select(h => h.Continent).Where(c => c is not null).Distinct(StringComparer.Ordinal).Count(),
            heard.Count(h => rel[h.Call] < SurpriseBelow));
    }
}

/// <summary>Great-circle distance on a sphere of 6371 km.</summary>
public static class Geo
{
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var h = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lon2 - lon1) / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
