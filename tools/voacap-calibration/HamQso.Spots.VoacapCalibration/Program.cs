using System.Collections.Concurrent;
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

foreach (var day in options.Days) await Download.DayAsync(http, cache, day);
var catalog = new RbnNodeCatalog();
catalog.Set(RbnNodeListParser.Parse(await Download.NodesAsync(http, cache), DateTimeOffset.UtcNow).Nodes, DateTimeOffset.UtcNow);
var locate = new LocateSkimmer(catalog);
var ssnMonthly = SsnByMonth.From(
    await Download.CachedAsync(http, cache, "observed-solar-cycle-indices.json", "https://services.swpc.noaa.gov/json/solar-cycle/observed-solar-cycle-indices.json"),
    await Download.CachedAsync(http, cache, "predicted-solar-cycle.json", "https://services.swpc.noaa.gov/json/solar-cycle/predicted-solar-cycle.json"));
// --ssn daily: the SSN of the day from the observed F10.7 (SsnByDay); default the month's (SsnByMonth).
var ssnDaily = options.DailySsn
    ? SsnByDay.FromGfz(await Download.CachedAsync(http, cache, "Kp_ap_Ap_SN_F107_since_1932.txt", "https://kp.gfz.de/app/files/Kp_ap_Ap_SN_F107_since_1932.txt"))
    : null;
double SsnOf(DateTimeOffset at) => ssnDaily is not null
    ? SsnByDay.On(ssnDaily, DateOnly.FromDateTime(at.UtcDateTime)) ?? throw new InvalidDataException($"no F10.7 within 3 days of {at:yyyy-MM-dd}")
    : ssnMonthly[(at.Year, at.Month)];

var events = new List<CalibrationEvent>();
foreach (var day in options.Days)
{
    var spots = Download.ReadDay(cache, day);
    var before = events.Count;
    events.AddRange(CalibrationEvents.Build(spots, locate).Where(e => options.Bands.Contains(e.Band)));
    Console.WriteLine($"{day}: {spots.Count} spots, {events.Count - before} tests");
}
var sample = Sample.Stratified(events, options.PerStratum, options.Seed);
Console.WriteLine($"{events.Count} tests of {events.Select(e => e.Station).Distinct().Count()} stations; {sample.Count} used of {sample.Select(e => e.Station).Distinct().Count()} stations (seed {options.Seed})");
foreach (var g in sample.GroupBy(e => (e.Band, Day: CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon))).OrderBy(g => g.Key.Band)) Console.WriteLine($"  {g.Key.Band} {(g.Key.Day ? "day" : "night")}: {g.Count()}");

var voacap = new VoacapClient(http, options.VoacapUrl, options.Token);
var combos = options.Noise.SelectMany(n => options.Snr.Select(s => (Noise: n, Snr: s))).ToArray();
var rels = new Dictionary<(double Noise, double Snr), IReadOnlyDictionary<string, double>[]>();
foreach (var combo in combos)
{
    var results = new IReadOnlyDictionary<string, double>[sample.Count];
    await Parallel.ForEachAsync(Enumerable.Range(0, sample.Count), new ParallelOptions { MaxDegreeOfParallelism = options.Parallel }, async (i, ct) =>
    {
        var e = sample[i];
        results[i] = await voacap.PredictAsync(e, SsnOf(e.Start), combo.Noise, combo.Snr, ct);
    });
    rels[combo] = results;
    Console.Write('.');
}
Console.WriteLine();

var groups = new List<(string Name, Func<CalibrationEvent, bool> In)> { ("all", _ => true) };
groups.AddRange(options.Bands.Select(b => (b, (Func<CalibrationEvent, bool>)(e => e.Band == b))));
groups.Add(("day", e => CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon)));
groups.Add(("night", e => !CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon)));
Score ScoreOf((double Noise, double Snr) combo, Func<CalibrationEvent, bool> include) => Score.Of(
    [.. sample.Select((e, i) => (e, i)).Where(x => include(x.e)).Select(x => Outcome(x.e, rels[combo][x.i]))]);

var report = new StringBuilder("group,noise_dbw,required_snr_dbhz,tests,with_margin,expected,heard,brier,p10_margin,median_margin,p90_margin,above,near,below\n");
var best = new Dictionary<string, ((double Noise, double Snr) Combo, Score Score)>();
foreach (var (name, include) in groups)
{
    foreach (var combo in combos)
    {
        var s = ScoreOf(combo, include);
        report.AppendLine(Csv.Line(name, combo.Noise, combo.Snr, s.Events, s.WithMargin, Math.Round(s.Expected, 1), s.Heard, Math.Round(s.Brier, 4), Math.Round(s.P10Margin, 3), Math.Round(s.MedianMargin, 3), Math.Round(s.P90Margin, 3), s.Above, s.Near, s.Below));
    }
    // Best: the lowest Brier among those whose median margin is within ±10%; if none, the median margin nearest 0.
    best[name] = Score.Best(combos.Select(c => (c, ScoreOf(c, include))).ToArray());
}
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var gridFile = Path.Combine(cache, $"calibration-{stamp}.csv");
await File.WriteAllTextAsync(gridFile, report.ToString());

var chosen = options.Export ?? best["all"].Combo;
// The settings of a test: those of its band (--export-band), else the chosen ones.
(double Noise, double Snr) For(CalibrationEvent e) =>
    options.ExportBand.TryGetValue(e.Band, out var c) ? c
    : options.ExportDaylight.TryGetValue(CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon) ? "day" : "night", out var d) ? d
    : chosen;
var chosenRels = sample.Select((e, i) => rels[For(e)][i]).ToArray();
Score ChosenScore(Func<CalibrationEvent, bool> include) => Score.Of(
    [.. sample.Select((e, i) => (e, i)).Where(x => include(x.e)).Select(x => Outcome(x.e, chosenRels[x.i]))]);
Console.WriteLine($"Best per group (lowest Brier with |median margin| ≤ 10%); chosen for the export: {chosen.Noise} dBW, {chosen.Snr} dB-Hz{string.Concat(options.ExportBand.Concat(options.ExportDaylight).Select(b => $", {b.Key} {b.Value.Noise}/{b.Value.Snr}"))}");
Console.WriteLine("group  best(noise,snr)  brier  p10/median/p90 margin  | with chosen: brier  p10/median/p90  tests(with margin)");
foreach (var (name, include) in groups)
{
    var (combo, s) = best[name];
    var c = ChosenScore(include);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{name,-6} {combo.Noise,5} {combo.Snr,4}   {s.Brier:0.000}  {s.P10Margin,6:P0} {s.MedianMargin,6:P0} {s.P90Margin,6:P0}  | {c.Brier:0.000}  {c.P10Margin,6:P0} {c.MedianMargin,6:P0} {c.P90Margin,6:P0}  {c.Events}({c.WithMargin})"));
}

var export = new StringBuilder("test,date_utc,utc_hour,month,band,daylight,ssn,listening,expected_theory,heard,margin,farthest_km,continents,surprises\n");
for (var i = 0; i < sample.Count; i++)
{
    var e = sample[i];
    var row = TestRow.Of(e, chosenRels[i]);
    export.AppendLine(Csv.Line(i + 1, e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.Start.Hour, e.Start.Month, e.Band,
        CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon) ? "day" : "night", Math.Round(SsnOf(e.Start), 1),
        row.Listening, Math.Round(row.ExpectedTheory, 2), row.Heard, row.Margin is { } m ? Math.Round(m, 3) : null, Math.Round(row.FarthestKm), row.Continents, row.Surprises));
}
await File.WriteAllTextAsync(options.ExportPath, export.ToString());

// One row per listening skimmer per test, for the app's own title rules (agent C).
var skimmersPath = Path.Combine(Path.GetDirectoryName(options.ExportPath)!, "calibration_skimmers.csv");
await File.WriteAllTextAsync(skimmersPath, SkimmerRows.Csv(sample, chosenRels, 1));

// The whole history of the stations with most tests (records and growth need it), test ids from 100001.
var historyPath = Path.Combine(Path.GetDirectoryName(options.ExportPath)!, "calibration_history_skimmers.csv");
var busiest = events.GroupBy(e => e.Station).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(options.HistoryStations).ToArray();
var history = busiest.SelectMany(g => g).OrderBy(e => e.Station, StringComparer.Ordinal).ThenBy(e => e.Start).ToArray();
var historyRels = new IReadOnlyDictionary<string, double>[history.Length];
await Parallel.ForEachAsync(Enumerable.Range(0, history.Length), new ParallelOptions { MaxDegreeOfParallelism = options.Parallel }, async (i, ct) =>
    historyRels[i] = await voacap.PredictAsync(history[i], SsnOf(history[i].Start), For(history[i]).Noise, For(history[i]).Snr, ct));
await File.WriteAllTextAsync(historyPath, SkimmerRows.Csv(history, historyRels, 100_001));
Console.WriteLine($"Grid: {gridFile}\nTests: {options.ExportPath}\nSkimmers: {skimmersPath}\nHistory of {busiest.Length} stations ({string.Join(", ", busiest.Select(g => $"{g.Key} {g.Count()}"))}): {historyPath}");

static CalibrationOutcome Outcome(CalibrationEvent e, IReadOnlyDictionary<string, double> rel) =>
    new([.. e.Listening.Where(l => rel.ContainsKey(l.Call)).Select(l => (rel[l.Call], e.Heard.Contains(l.Call)))]);

/// <summary>One row per listening skimmer per test (calibration_skimmers.csv, README).</summary>
internal static class SkimmerRows
{
    public static string Csv(IReadOnlyList<CalibrationEvent> tests, IReadOnlyList<IReadOnlyDictionary<string, double>> rels, int firstId)
    {
        var rows = new StringBuilder("test_id,callsign,started_at,band,locator,power_w,skimmer,dxcc,continent,lat,lon,p_theory,p_sky,heard,snr_db,after_s\n");
        for (var i = 0; i < tests.Count; i++)
        {
            var e = tests[i];
            foreach (var l in e.Listening.Where(l => rels[i].ContainsKey(l.Call)))
            {
                var heard = e.HeardSpots?.FirstOrDefault(h => h.Call == l.Call);
                rows.AppendLine(global::Csv.Line(firstId + i, e.Station, e.Start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), e.Band, e.StationLocator, null,
                    l.Call, l.Dxcc, l.Continent, l.Position!.Lat, l.Position.Lon, rels[i][l.Call], null, heard is null ? 0 : 1, heard?.SnrDb, heard?.AfterS));
            }
        }
        return rows.ToString();
    }
}

internal static class Csv
{
    public static string Line(params object?[] values) => string.Join(',', values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
}

internal sealed record Args(
    IReadOnlyList<string> Days, IReadOnlyList<string> Bands, int PerStratum, int Seed, int Parallel,
    IReadOnlyList<double> Noise, IReadOnlyList<double> Snr, (double Noise, double Snr)? Export, string ExportPath, Uri VoacapUrl, string Token, int HistoryStations,
    IReadOnlyDictionary<string, (double Noise, double Snr)> ExportBand,
    IReadOnlyDictionary<string, (double Noise, double Snr)> ExportDaylight,
    bool DailySsn)
{
    private static Dictionary<string, (double Noise, double Snr)> Pairs(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Split('=', '/')).ToDictionary(
            b => b[0], b => (double.Parse(b[1], CultureInfo.InvariantCulture), double.Parse(b[2], CultureInfo.InvariantCulture)), StringComparer.Ordinal);

    public static Args Parse(string[] args)
    {
        string? Get(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        static double[] Numbers(string? text, double[] fallback) =>
            text is null ? fallback : [.. text.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))];
        var days = (Get("--days") ?? DateTime.UtcNow.AddDays(-2).ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Split(',');
        if (days.Any(d => !DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) throw new ArgumentException("--days yyyyMMdd[,yyyyMMdd…]");
        var token = Environment.GetEnvironmentVariable("VOACAP_TOKEN") ?? "";
        if (token.Length == 0 && File.Exists("voacap/.local-token")) token = File.ReadAllText("voacap/.local-token").Trim();
        var export = Numbers(Get("--export"), []);
        return new Args(
            days,
            (Get("--bands") ?? "40m,20m,15m,10m").Split(','),
            int.Parse(Get("--per-stratum") ?? "60", CultureInfo.InvariantCulture),
            int.Parse(Get("--seed") ?? "71", CultureInfo.InvariantCulture),
            int.Parse(Get("--parallel") ?? "6", CultureInfo.InvariantCulture),
            Numbers(Get("--noise"), [-155, -150, -145, -140, -135, -130]),
            Numbers(Get("--snr"), [24, 29, 34, 39, 44, 49]),
            export.Length == 2 ? (export[0], export[1]) : null,
            Get("--export-path") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "hamqso-f71-fixtures", "calibration_tests.csv"),
            new Uri(Environment.GetEnvironmentVariable("VOACAP_URL") ?? "http://127.0.0.1:18090"),
            token,
            int.Parse(Get("--history-stations") ?? "8", CultureInfo.InvariantCulture),
            // --export-band 40m=-135/54,15m=-135/39 and --export-daylight day=-135/41,night=-135/47; each pair must be in the grid.
            Pairs(Get("--export-band")),
            Pairs(Get("--export-daylight")),
            Get("--ssn") == "daily");
    }
}

/// <summary>The RBN history, the node list and NOAA's SSN, downloaded once into the cache folder (outside the repo).</summary>
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

    public static Task<string> NodesAsync(HttpClient http, string cache) =>
        CachedAsync(http, cache, Path.Combine("nodes", $"skt-{DateTime.UtcNow:yyyyMMdd}.html"), "https://www.reversebeacon.net/cont_includes/status.php?t=skt");

    public static async Task<string> CachedAsync(HttpClient http, string cache, string name, string url)
    {
        var path = Path.Combine(cache, "pages", name);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, await http.GetStringAsync(new Uri(url)));
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
