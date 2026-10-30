using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace HamQso.Spots.Domain.References;

/// <summary>A POTA park, SOTA summit or WWFF area of the catalog the apps keep offline (F53.3).</summary>
public sealed record CatalogReference(
    string Ref,
    string Program,
    string Name,
    string? Location,
    int? Dxcc,
    string? Grid,
    double? Latitude,
    double? Longitude,
    int? AltitudeM,
    int? Points);

/// <summary>
/// The catalog file: gzip of UTF-8 text, a header line then one reference per
/// line in code order, fields separated by tabs (empty = unknown):
/// <c>HAMQSO-REFERENCES 1 &lt;generated at, ISO 8601&gt; &lt;count&gt;</c>, then
/// <c>ref program name location dxcc grid lat lon altitude_m points</c>.
/// <see cref="ETag"/> depends on the references only: the same catalog a week
/// later keeps it and the apps download nothing.
/// </summary>
public sealed record ReferenceCatalogFile(byte[] Gzip, string ETag, DateTimeOffset GeneratedAt, int Count);

/// <summary>What <see cref="ReferenceCatalogFormat.Read"/> finds in a file.</summary>
public sealed record ReferenceCatalogContent(DateTimeOffset GeneratedAt, IReadOnlyList<CatalogReference> References);

public static class ReferenceCatalogFormat
{
    public const string Magic = "HAMQSO-REFERENCES";
    public const int Version = 1;

    public static ReferenceCatalogFile Write(IEnumerable<CatalogReference> references, DateTimeOffset generatedAt)
    {
        var sorted = references.OrderBy(r => r.Ref, StringComparer.Ordinal).ToList();
        var rows = new StringBuilder();
        foreach (var r in sorted) rows.Append(Row(r)).Append('\n');
        var body = rows.ToString();
        var header = string.Join('\t', Magic, Version.ToString(CultureInfo.InvariantCulture),
            generatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            sorted.Count.ToString(CultureInfo.InvariantCulture)) + "\n";
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(header + body));
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return new ReferenceCatalogFile(output.ToArray(), '"' + Convert.ToHexStringLower(hash)[..32] + '"', generatedAt, sorted.Count);
    }

    /// <summary>The same file read back (the tests and the tools).</summary>
    public static ReferenceCatalogContent Read(byte[] gzip)
    {
        using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        var header = (reader.ReadLine() ?? "").Split('\t');
        if (header.Length < 4 || header[0] != Magic) throw new FormatException("Not a HamQSO reference catalog");
        var at = DateTimeOffset.Parse(header[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var list = new List<CatalogReference>();
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length < 10) continue;
            list.Add(new CatalogReference(f[0], f[1], f[2], Text(f[3]), Int(f[4]), Text(f[5]), Num(f[6]), Num(f[7]), Int(f[8]), Int(f[9])));
        }
        return new ReferenceCatalogContent(at, list);
    }

    private static string Row(CatalogReference r) => string.Join('\t',
        Clean(r.Ref), Clean(r.Program), Clean(r.Name), Clean(r.Location),
        r.Dxcc?.ToString(CultureInfo.InvariantCulture) ?? "", Clean(r.Grid),
        r.Latitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "",
        r.Longitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "",
        r.AltitudeM?.ToString(CultureInfo.InvariantCulture) ?? "",
        r.Points?.ToString(CultureInfo.InvariantCulture) ?? "");

    /// <summary>A tab or a line break would split the row: one space instead.</summary>
    private static string Clean(string? s) =>
        s is null ? "" : string.Join(' ', s.Split(['\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string? Text(string s) => s.Length == 0 ? null : s;

    private static int? Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
