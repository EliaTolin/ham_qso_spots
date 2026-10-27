using System.Text.RegularExpressions;

namespace HamQso.Spots.Domain.Radio;

/// <summary>
/// Mode of a DX cluster spot. The cluster has no mode field: the mode written in
/// the comment, or decoded by an RBN skimmer, then the frequency (official band
/// plan per IARU region, FT8/FT4/JS8 calling windows, no mode where a segment is
/// shared by several modes).
/// Port of ham_qso_supabase/supabase/functions/_shared/band-plan.ts and of the
/// app's band_plan.dart: keep them in sync. All test band_plan_cases.json.
/// </summary>
public static partial class BandPlan
{
    private static readonly HashSet<string> Phone =
        ["SSB", "USB", "LSB", "PHONE", "AM", "FM", "DV", "DMR", "C4FM", "DSTAR", "D-STAR", "FUSION"];

    private static readonly HashSet<string> Digital =
    [
        "FT8", "FT4", "DATA", "DIGI", "DIGITAL", "RTTY", "PSK", "PSK31", "PSK63", "JS8",
        "MFSK", "JT65", "JT9", "SSTV", "OLIVIA", "VARAC", "Q65", "MSK144", "FST4",
        "WSPR", "VARA", "PACTOR", "CONTESTIA", "HELL", "THOR", "DOMINO", "MT63", "FREEDV",
    ];

    private static readonly string[] DeclaredDigital =
    [
        "FT8", "FT4", "RTTY", "PSK31", "PSK63", "PSK", "SSTV", "MFSK", "OLIVIA", "CONTESTIA", "JT65", "JT9",
        "WSPR", "HELL", "THOR", "DOMINO", "MT63", "PACTOR", "MSK144", "Q65", "JS8", "FST4", "FREEDV", "VARA", "VARAC",
    ];

    private static readonly string[] DeclaredPhone = ["SSB", "USB", "LSB", "AM", "FM", "PHONE", "DMR"];

    /// <summary>DXCC entities under FCC rules (narrower phone sub-bands on 80/40/20/15m).</summary>
    private static readonly HashSet<int> FccDxcc = [291, 6, 110, 202, 285, 103, 166, 9, 105, 182, 197, 297, 515, 43, 20, 123, 138, 174];

    /// <summary>Asian DXCC entities in ITU Region 1 (Middle East, Caucasus, former USSR, Mongolia).</summary>
    private static readonly HashSet<int> Region1Asia =
    [
        14, 15, 18, 75, 130, 135, 215, 262, 280, 283, 292, 304, 333, 336, 342, 348, 354, 363, 370, 376, 378, 384, 390, 391, 492, 510,
    ];

    /// <summary>Same grouping as the app's ModeCategory. Unknown modes: null.</summary>
    public static ModeCategory? CategoryOf(string? mode)
    {
        var m = (mode ?? "").Trim().ToUpperInvariant();
        if (m == "CW") return ModeCategory.Cw;
        if (Phone.Contains(m)) return ModeCategory.Phone;
        if (Digital.Contains(m)) return ModeCategory.Digital;
        return null;
    }

    /// <summary>The mode written in a comment, as a whole word, in Wavelog's order: CW, digital, phone.</summary>
    public static string? DeclaredMode(string? comment)
    {
        if (string.IsNullOrEmpty(comment)) return null;
        var normalized = Ft4Pattern().Replace(Ft8Pattern().Replace(comment.ToUpperInvariant(), "FT8"), "FT4");
        var words = new HashSet<string>(NonWordPattern().Split(normalized));
        if (words.Contains("CW")) return "CW";
        return DeclaredDigital.FirstOrDefault(words.Contains) ?? DeclaredPhone.FirstOrDefault(words.Contains);
    }

    /// <summary>The decoded mode of an RBN skimmer spot ("-#" spotter), null for any other spot.</summary>
    public static string? SkimmerMode(string? spotter, string? comment)
    {
        if (spotter is null || !spotter.EndsWith("-#", StringComparison.Ordinal) || string.IsNullOrEmpty(comment)) return null;
        var match = SkimmerCommentPattern().Match(comment.Trim());
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>The region whose band plan a station follows; null when unknown.</summary>
    public static IaruRegion? RegionOf(int? dxcc, string? continent)
    {
        if (dxcc is { } d && FccDxcc.Contains(d)) return IaruRegion.Fcc;
        return continent switch
        {
            "EU" or "AF" => IaruRegion.R1,
            "NA" or "SA" => IaruRegion.R2,
            "AS" => dxcc is { } a && Region1Asia.Contains(a) ? IaruRegion.R1 : IaruRegion.R3,
            "OC" => IaruRegion.R3,
            _ => null,
        };
    }

    /// <summary>Mode from the frequency alone, or "" where the segment does not tell.</summary>
    public static string BandPlanMode(double khz, IaruRegion? region)
    {
        foreach (var (from, to, mode) in Windows)
        {
            if (khz >= from && khz < to) return mode;
        }
        var table = region switch
        {
            IaruRegion.R1 => Region1,
            IaruRegion.R2 => Region2,
            IaruRegion.R3 => Region3,
            IaruRegion.Fcc => Fcc,
            _ => AnyRegionCw,
        };
        foreach (var (from, to, use) in table.Concat(Common))
        {
            if (khz >= from && khz < to)
            {
                return use switch
                {
                    Use.Cw => "CW",
                    Use.Voice => VoiceMode(khz),
                    _ => "",
                };
            }
        }
        return "";
    }

    /// <summary>
    /// The mode of a spot without evidence from other spots: reported (comment or
    /// skimmer) or band plan.
    /// </summary>
    public static ResolvedMode Resolve(double khz, string? comment, string? spotter, IaruRegion? region)
    {
        var reported = SkimmerMode(spotter, comment) ?? DeclaredMode(comment);
        if (reported is not null) return new ResolvedMode(reported, ModeSource.Reported);
        var mode = BandPlanMode(khz, region);
        return new ResolvedMode(mode, mode.Length > 0 ? ModeSource.BandPlan : null);
    }

    private static string VoiceMode(double khz)
    {
        if ((khz >= 29520 && khz < 29700) || khz >= 145000) return "FM";
        if (khz >= 5250 && khz < 5450) return "USB";
        return khz < 10000 ? "LSB" : "USB";
    }

    private enum Use
    {
        Cw,
        Voice,
        Mixed,
    }

    private static (double, double, Use) Cw(double from, double to) => (from, to, Use.Cw);

    private static (double, double, Use) Voice(double from, double to) => (from, to, Use.Voice);

    private static (double, double, Use) Mixed(double from, double to) => (from, to, Use.Mixed);

    /// <summary>FT8/FT4/JS8 calling windows [from, to) kHz: dial -0.6 to +3.2 (60m: 5356-5359).</summary>
    private static readonly (double From, double To, string Mode)[] Windows =
    [
        (1839.4, 1843.2, "FT8"), (3572.4, 3576.2, "FT8"), (3574.9, 3578.7, "FT4"), (5356, 5359, "FT8"),
        (7046.9, 7050.7, "FT4"), (7073.4, 7077.2, "FT8"), (7077.4, 7081.2, "JS8"),
        (10135.4, 10139.2, "FT8"), (10139.4, 10143.2, "FT4"),
        (14073.4, 14077.2, "FT8"), (14077.4, 14079.4, "JS8"), (14079.4, 14083.2, "FT4"),
        (18099.4, 18103.2, "FT8"), (18103.4, 18107.2, "FT4"), (21073.4, 21077.2, "FT8"), (21139.4, 21143.2, "FT4"),
        (24914.4, 24918.2, "FT8"), (24918.4, 24922.2, "FT4"), (28073.4, 28077.2, "FT8"), (28179.4, 28183.2, "FT4"),
        (50312.4, 50316.2, "FT8"), (50317.4, 50321.2, "FT4"), (50322.4, 50326.2, "FT8"), (70153.4, 70157.2, "FT8"),
        (144169.4, 144173.2, "FT4"), (144173.4, 144177.2, "FT8"), (432173.4, 432177.2, "FT8"), (1296173.4, 1296177.2, "FT8"),
    ];

    private static readonly (double, double, Use)[] Region1 =
    [
        Mixed(1800, 1810), Cw(1810, 1838), Mixed(1838, 1850), Voice(1850, 2000),
        Cw(3500, 3570), Mixed(3570, 3620), Voice(3620, 3800), Mixed(3800, 4000),
        Cw(7000, 7040), Mixed(7040, 7060), Voice(7060, 7200), Mixed(7200, 7300),
    ];

    private static readonly (double, double, Use)[] Region2 =
    [
        Mixed(1800, 1810), Cw(1810, 1840), Mixed(1840, 1850), Voice(1850, 2000),
        Cw(3500, 3570), Mixed(3570, 3600), Voice(3600, 4000),
        Cw(7000, 7040), Mixed(7040, 7125), Voice(7125, 7300),
    ];

    private static readonly (double, double, Use)[] Region3 =
    [
        Cw(1800, 1830), Mixed(1830, 1850), Voice(1850, 2000),
        Cw(3500, 3535), Mixed(3535, 3600), Voice(3600, 3900),
        Cw(7000, 7030), Mixed(7030, 7060), Voice(7060, 7200), Mixed(7200, 7300),
    ];

    /// <summary>US stations: no phone below 3.600, 7.125, 14.150, 21.200 MHz.</summary>
    private static readonly (double, double, Use)[] Fcc =
    [
        .. Region2,
        Cw(14000, 14070), Mixed(14070, 14150), Voice(14150, 14350),
        Cw(21000, 21070), Mixed(21070, 21200), Voice(21200, 21450),
    ];

    /// <summary>Unknown region: only the CW segments every region agrees on.</summary>
    private static readonly (double, double, Use)[] AnyRegionCw =
    [
        Cw(1810, 1830), Mixed(1830, 2000), Cw(3500, 3535), Mixed(3535, 4000), Cw(7000, 7030), Mixed(7030, 7300),
        Mixed(14112, 14350), Mixed(18111, 18168), Mixed(21151, 21450), Mixed(24931, 24990), Mixed(28300, 29000),
        Mixed(5361, 5366.5), Mixed(145000, 148000), Mixed(433000, 435000), Mixed(438000, 440000), Mixed(1297000, 1300000),
    ];

    /// <summary>Same in every region (after the regional table, which wins).</summary>
    private static readonly (double, double, Use)[] Common =
    [
        Cw(10100, 10130), Mixed(10130, 10150),
        Cw(14000, 14070), Mixed(14070, 14112), Voice(14112, 14350),
        Mixed(5250, 5351.5), Cw(5351.5, 5356), Mixed(5356, 5361), Voice(5361, 5366.5), Mixed(5366.5, 5450),
        Cw(18068, 18095), Mixed(18095, 18111), Voice(18111, 18168),
        Cw(21000, 21070), Mixed(21070, 21151), Voice(21151, 21450),
        Cw(24890, 24915), Mixed(24915, 24931), Voice(24931, 24990),
        Cw(28000, 28070), Mixed(28070, 28300), Voice(28300, 29000), Mixed(29000, 29520), Voice(29520, 29700),
        Mixed(50000, 54000), Mixed(70000, 70500),
        Mixed(144000, 145000), Voice(145000, 148000),
        Mixed(432000, 433000), Voice(433000, 435000), Mixed(435000, 438000), Voice(438000, 440000),
        Mixed(1296000, 1297000), Voice(1297000, 1300000),
    ];

    [GeneratedRegex(@"\bFT[- ]8\b")]
    private static partial Regex Ft8Pattern();

    [GeneratedRegex(@"\bFT[- ]4\b")]
    private static partial Regex Ft4Pattern();

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonWordPattern();

    [GeneratedRegex(@"^(CW|RTTY|FT8|FT4|PSK31|PSK63|MSK144)\s+[+-]?\d+\s*dB\b", RegexOptions.IgnoreCase)]
    private static partial Regex SkimmerCommentPattern();
}
