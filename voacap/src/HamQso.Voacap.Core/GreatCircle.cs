namespace HamQso.Voacap.Core;

/// <summary>Great-circle geometry on a sphere of 6371 km.</summary>
public static class GreatCircle
{
    private const double EarthKm = 6371.0;

    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var (p1, p2) = (Rad(a.Lat), Rad(b.Lat));
        var dp = p2 - p1;
        var dl = Rad(b.Lon - a.Lon);
        var h = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * EarthKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>The point <paramref name="km"/> from <paramref name="from"/> going <paramref name="bearingDeg"/> (clockwise from north).</summary>
    public static GeoPoint Destination(GeoPoint from, double bearingDeg, double km)
    {
        ArgumentNullException.ThrowIfNull(from);
        var d = km / EarthKm;
        var b = Rad(bearingDeg);
        var p1 = Rad(from.Lat);
        var l1 = Rad(from.Lon);
        var p2 = Math.Asin(Math.Sin(p1) * Math.Cos(d) + Math.Cos(p1) * Math.Sin(d) * Math.Cos(b));
        var l2 = l1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(p1), Math.Cos(d) - Math.Sin(p1) * Math.Sin(p2));
        var lon = (Deg(l2) + 540) % 360 - 180;
        return new GeoPoint(Deg(p2), lon);
    }

    private static double Rad(double deg) => deg * Math.PI / 180;

    private static double Deg(double rad) => rad * 180 / Math.PI;
}
