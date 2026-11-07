using System.Globalization;
using System.IO.Compression;
using System.Text;
using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Infrastructure.Rbn;
using HamQso.Spots.VoacapCalibration;

// F71 phase 5: VOACAP against what the RBN really heard. See README.md.
var cache = Environment.GetEnvironmentVariable("HAMQSO_CALIBRATION_CACHE")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "hamqso-f71-calibration");
var options = Args.Parse(args);
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
// Generic, never an email or a callsign (Elia's rule for the calls made while developing).
http.DefaultRequestHeaders.UserAgent.ParseAdd("HamQSO-dev");

var days = options.Days;
foreach (var day in days) await Download.DayAsync(http, cache, day);
var nodes = await Download.NodesAsync(http, cache);
var catalog = new RbnNodeCatalog();
catalog.Set(RbnNodeListParser.Parse(nodes, DateTimeOffset.UtcNow).Nodes, DateTimeOffset.UtcNow);
var locate = new LocateSkimmer(catalog);

var events = new List<CalibrationEvent>();
foreach (var day in days)
{
    var spots = Download.ReadDay(cache, day);
    events.AddRange(CalibrationEvents.Build(spots, locate));
    Console.WriteLine($"{day}: {spots.Count} spots, {events.Count} events so far");
}
var sample = events.OrderBy(e => (e.Station, e.Band, e.Slot).GetHashCode() ^ options.Seed).Take(options.Events).ToArray();
Console.WriteLine($"{events.Count} events of {events.Select(e => e.Station).Distinct().Count()} stations; {sample.Length} used (seed {options.Seed}), SSN {options.Ssn}");

var voacap = new VoacapClient(http, options.VoacapUrl, options.Token);
var report = new StringBuilder("noise_dbw,required_snr_dbhz,events,expected,heard,brier,median_margin,above,near,below\n");
foreach (var noise in options.Noise)
{
    foreach (var snr in options.Snr)
    {
        var outcomes = new List<CalibrationOutcome>();
        foreach (var e in sample)
        {
            var rel = await voacap.PredictAsync(e, options.Ssn, noise, snr, CancellationToken.None);
            outcomes.Add(new CalibrationOutcome([.. e.Listening.Where(l => rel.ContainsKey(l.Call)).Select(l => (rel[l.Call], e.Heard.Contains(l.Call)))]));
        }
        var s = Score.Of(outcomes);
        var line = string.Create(CultureInfo.InvariantCulture, $"{noise},{snr},{s.Events},{s.Expected:0.0},{s.Heard},{s.Brier:0.0000},{s.MedianMargin:0.000},{s.Above},{s.Near},{s.Below}");
        Console.WriteLine(line);
        report.AppendLine(line);
    }
}
var output = Path.Combine(cache, $"calibration-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
await File.WriteAllTextAsync(output, report.ToString());
Console.WriteLine($"Report: {output} (lowest brier = best probabilities; median_margin near 0 = a dipole at 100 W is on average where the forecast says)");

internal sealed record Args(IReadOnlyList<string> Days, int Events, int Seed, double Ssn, IReadOnlyList<double> Noise, IReadOnlyList<double> Snr, Uri VoacapUrl, string Token)
{
    public static Args Parse(string[] args)
    {
        string? Get(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        static double[] Numbers(string? text, double[] fallback) =>
            text is null ? fallback : [.. text.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))];
        var days = (Get("--days") ?? DateTime.UtcNow.AddDays(-2).ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Split(',');
        if (days.Any(d => !DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) throw new ArgumentException("--days yyyyMMdd[,yyyyMMdd…]");
        var token = Environment.GetEnvironmentVariable("VOACAP_TOKEN") ?? "";
        if (token.Length == 0 && File.Exists("voacap/.local-token")) token = File.ReadAllText("voacap/.local-token").Trim();
        return new Args(
            days,
            int.Parse(Get("--events") ?? "200", CultureInfo.InvariantCulture),
            int.Parse(Get("--seed") ?? "71", CultureInfo.InvariantCulture),
            double.Parse(Get("--ssn") ?? throw new ArgumentException("--ssn <the predicted monthly SSN of NOAA for the days>"), CultureInfo.InvariantCulture),
            Numbers(Get("--noise"), [-155, -150, -145, -140]),
            Numbers(Get("--snr"), [14, 19, 24, 29, 34]),
            new Uri(Environment.GetEnvironmentVariable("VOACAP_URL") ?? "http://127.0.0.1:18090"),
            token);
    }
}

/// <summary>The RBN history and the node list, downloaded once into the cache folder (outside the repo).</summary>
internal static class Download
{
    private const long MaxCsvBytes = 200L * 1024 * 1024;

    public static async Task DayAsync(HttpClient http, string cache, string day)
    {
        var path = Path.Combine(cache, "history", $"{day}.zip");
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = await http.GetByteArrayAsync(new Uri($"https://data.reversebeacon.net/rbn_history/{day}.zip"));
        await File.WriteAllBytesAsync(path, bytes);
    }

    public static async Task<string> NodesAsync(HttpClient http, string cache)
    {
        var path = Path.Combine(cache, "nodes", $"skt-{DateTime.UtcNow:yyyyMMdd}.html");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, await http.GetStringAsync(new Uri("https://www.reversebeacon.net/cont_includes/status.php?t=skt")));
        }
        return await File.ReadAllTextAsync(path);
    }

    /// <summary>The spots of a day, read from the zip without extracting it: one entry named &lt;day&gt;.csv, at most 200 MB.</summary>
    public static IReadOnlyList<HistorySpot> ReadDay(string cache, string day)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(cache, "history", $"{day}.zip"));
        var entry = zip.Entries.SingleOrDefault(e => e.FullName == $"{day}.csv") ?? throw new InvalidDataException($"{day}.zip has no {day}.csv");
        if (entry.Length > MaxCsvBytes) throw new InvalidDataException($"{day}.csv is too large");
        using var reader = new StreamReader(entry.Open());
        var spots = new List<HistorySpot>();
        while (reader.ReadLine() is { } line)
        {
            if (HistoryLine.Parse(line) is { } s) spots.Add(s);
        }
        return spots;
    }
}
