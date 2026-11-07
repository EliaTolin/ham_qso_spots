using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HamQso.Voacap.Core;

/// <summary>
/// The antenna models of the service as voacapl antenna files, with the ITSA-1
/// types of the voacapl samples (poor ground: dielectric 4, conductivity 0.001):
/// <list type="bullet">
/// <item>isotropic: voacapl's isotrope; with a gain, a flat gain table (type 11);</item>
/// <item>dipole: horizontal half-wave dipole (type 23) at its height, 10 m by default;</item>
/// <item>vertical: quarter-wave monopole on the ground (type 22);</item>
/// <item>yagi: horizontal yagi (type 24) at its height, 12 m by default, 8.15 dBi by default;
/// its pattern has no azimuth, so it is as if it pointed at every receiver;</item>
/// <item>loop: a dipole pattern (type 23) with 3.15 dBi by default.</item>
/// </list>
/// A gain in dBi is given to voacapl over a dipole (−2.15 dB). The file name is
/// the hash of its text, so the same antenna is the same file.
/// </summary>
public static class AntennaFiles
{
    public const string Isotrope = "default/isotrope";
    private const double DipoleDbi = 2.15;

    private static readonly string[] Models = ["isotropic", "dipole", "vertical", "yagi", "loop"];

    public static bool IsKnown(string? model) => model is not null && Models.Contains(model, StringComparer.Ordinal);

    public static AntennaFile For(AntennaSpec antenna, double frequencyMhz)
    {
        ArgumentNullException.ThrowIfNull(antenna);
        var content = antenna.Model switch
        {
            "isotropic" when (antenna.GainDbi ?? 0) == 0 => null,
            "isotropic" => Flat(antenna.GainDbi!.Value),
            "dipole" => Itsa("HamQSO dipole", 23, "ITSA-1 Horizontal Dipole", frequencyMhz, -0.5, antenna.HeightM ?? 10, Over(antenna.GainDbi, DipoleDbi)),
            "loop" => Itsa("HamQSO loop", 23, "ITSA-1 Horizontal Dipole", frequencyMhz, -0.5, antenna.HeightM ?? 10, Over(antenna.GainDbi, 3.15)),
            "yagi" => Itsa("HamQSO yagi", 24, "ITSA-1 Horizontal Yagi", frequencyMhz, -0.5, antenna.HeightM ?? 12, Over(antenna.GainDbi, 8.15)),
            "vertical" => Vertical(frequencyMhz, Over(antenna.GainDbi, DipoleDbi)),
            _ => throw new ArgumentException($"unknown antenna model {antenna.Model}", nameof(antenna)),
        };
        return content is null ? new AntennaFile(Isotrope, null) : new AntennaFile($"hq/{Hash(content)}", content);
    }

    private static double Over(double? gainDbi, double fallbackDbi) => (gainDbi ?? fallbackDbi) - DipoleDbi;

    private static string Itsa(string title, int type, string kind, double mhz, double length, double height, double gainOverDipole) => Lines(
        $"{title,-20} :{kind}",
        " 8     8 parameters",
        $"{N(0, 6)}  [ 1] Max Gain dBi..:",
        $"  {type}    [ 2] Antenna Type..:",
        "   4    [ 3] Dielectric....:",
        ".00100  [ 4] Conductivity..:",
        $"{N(mhz, 6, "0.000")}  [ 5] Operating Freq:",
        $"{N(length, 6)}  [ 6] Antenna Length:",
        $"{N(height, 6)}  [ 7] Antenna Height:",
        $"{N(gainOverDipole, 6)}  [ 8] Gain ab dipole:");

    private static string Vertical(double mhz, double gainOverDipole) => Lines(
        $"{"HamQSO vertical",-20} :ITSA-1 Vertical Monopole",
        " 7     7 parameters",
        $"{N(0, 6)}  [ 1] Max Gain dBi..:",
        "  22    [ 2] Antenna Type..:",
        "   4    [ 3] Dielectric....:",
        ".00100  [ 4] Conductivity..:",
        $"{N(mhz, 6, "0.000")}  [ 5] Operating Freq:",
        $"{N(-0.25, 6)}  [ 6] Antenna Height:",
        $"{N(gainOverDipole, 6)}  [ 7] Gain ab dipole:");

    private static string Flat(double gainDbi)
    {
        var rows = Enumerable.Range(0, 10).Select(r => string.Concat(Enumerable.Repeat("     0.0", r == 9 ? 1 : 10)));
        return Lines([
            $"{"HamQSO isotropic",-20} :Gain table, flat",
            " 3     3 parameters",
            $"{N(gainDbi, 6)}  [ 1] Max Gain dBi..:",
            "  11    [ 2] Antenna Type..: 91 values gain in elevation angle follows",
            "  0.0   [ 3] Efficiency (for IONCAP)",
            .. rows]);
    }

    private static string N(double value, int width, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture).PadLeft(width);

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";

    private static string Hash(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16];
}
