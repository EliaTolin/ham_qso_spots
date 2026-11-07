using System.Globalization;
using System.Text.RegularExpressions;
using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.VoacapCalibration;

/// <summary>A spot of the RBN history: skimmer (as written, no "-#"), kHz, band, station heard, kind (CQ, BEACON…), dB, time (UTC), mode.</summary>
public sealed record HistorySpot(string Skimmer, double Khz, string Band, string Dx, string Kind, int Db, DateTimeOffset At, string TxMode);

/// <summary>
/// The daily CSV of https://www.reversebeacon.net/raw_data/ ("callsign,de_pfx,de_cont,freq,band,dx,dx_pfx,dx_cont,mode,db,date,speed,tx_mode").
/// The file is not ours: every field is checked, anything odd is left out.
/// </summary>
public static partial class HistoryLine
{
    private const int Fields = 13;

    public static HistorySpot? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var f = line.Split(',');
        if (f.Length != Fields) return null;
        if (!Call().IsMatch(f[0]) || !Call().IsMatch(f[5])) return null;
        if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var khz) || Bands.Of(khz) is not { } band || band != f[4]) return null;
        if (!int.TryParse(f[9], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var db)) return null;
        if (!DateTime.TryParseExact(f[10], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) return null;
        if (!Word().IsMatch(f[8]) || !Word().IsMatch(f[12])) return null;
        return new HistorySpot(f[0].ToUpperInvariant(), khz, band, f[5].ToUpperInvariant(), f[8], db, new DateTimeOffset(at, TimeSpan.Zero), f[12]);
    }

    [GeneratedRegex("^[A-Za-z0-9/]{1,20}(-[0-9]{1,2})?$")]
    private static partial Regex Call();

    [GeneratedRegex("^[A-Z0-9 ]{1,10}$")]
    private static partial Regex Word();
}
