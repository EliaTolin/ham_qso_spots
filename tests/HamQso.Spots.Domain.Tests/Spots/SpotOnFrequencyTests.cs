using System.Text.Json;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Domain.Tests.Spots;

/// <summary>F88: the spot a frequency written in the log suggests.</summary>
public class SpotOnFrequencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static Spot Spot(
        string call = "K1ABC",
        double khz = 14285,
        string mode = "USB",
        int minutesAgo = 1,
        SpotProgram program = SpotProgram.Dx,
        long seq = 1,
        bool qrt = false,
        string? comment = null) => new()
        {
            Program = program,
            SpotKey = call,
            Callsign = call,
            FrequencyKhz = khz,
            Mode = mode,
            SpotTime = Now.AddMinutes(-minutesAgo),
            Feeds = ["DXFun"],
            Seq = seq,
            Qrt = qrt,
            Comment = comment,
        };

    private static string? Picked(double khz, params Spot[] spots) => SpotOnFrequency.Pick(khz, spots, Now)?.Callsign;

    private static JsonElement[] Cases(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "on_frequency_cases.json")));
        return [.. doc.RootElement.GetProperty(name).EnumerateArray().Select(c => c.Clone())];
    }

    public static TheoryData<string, double> SharedTolerances()
    {
        var data = new TheoryData<string, double>();
        foreach (var c in Cases("tolerances")) data.Add(c.GetProperty("mode").GetString()!, c.GetProperty("khz").GetDouble());
        return data;
    }

    public static TheoryData<double, string, double?> SharedDials()
    {
        var data = new TheoryData<double, string, double?>();
        foreach (var c in Cases("calling_dials"))
        {
            var dial = c.GetProperty("dial");
            data.Add(c.GetProperty("khz").GetDouble(), c.GetProperty("mode").GetString()!, dial.ValueKind == JsonValueKind.Null ? null : dial.GetDouble());
        }
        return data;
    }

    public static TheoryData<double, double, string, bool> SharedOnFrequency()
    {
        var data = new TheoryData<double, double, string, bool>();
        foreach (var c in Cases("on_frequency"))
        {
            data.Add(c.GetProperty("khz").GetDouble(), c.GetProperty("spot_khz").GetDouble(), c.GetProperty("mode").GetString()!, c.GetProperty("on_frequency").GetBoolean());
        }
        return data;
    }

    public static TheoryData<string?, bool> SharedQrtComments()
    {
        var data = new TheoryData<string?, bool>();
        foreach (var c in Cases("comment_says_qrt")) data.Add(c.GetProperty("comment").GetString(), c.GetProperty("qrt").GetBoolean());
        return data;
    }

    [Theory]
    [MemberData(nameof(SharedTolerances))]
    public void The_tolerance_of_a_mode_is_the_same_as_in_the_app(string mode, double khz) =>
        Assert.Equal(khz, SpotOnFrequency.ToleranceKhz(mode));

    [Theory]
    [MemberData(nameof(SharedDials))]
    public void The_calling_dial_is_the_same_as_in_the_app(double khz, string mode, double? dial) =>
        Assert.Equal(dial, BandPlan.CallingDialFor(khz, mode));

    [Theory]
    [MemberData(nameof(SharedOnFrequency))]
    public void A_spot_is_on_frequency_as_in_the_app(double khz, double spotKhz, string mode, bool onFrequency) =>
        Assert.Equal(onFrequency, Picked(khz, Spot(khz: spotKhz, mode: mode)) is not null);

    [Fact]
    public void The_most_recent_wins_over_the_nearest()
    {
        var nearButOlder = Spot("NEAR", khz: 14285.0, minutesAgo: 5);
        var fartherButNewer = Spot("NEWER", khz: 14286.0, minutesAgo: 1);

        Assert.Equal("NEWER", Picked(14285.0, nearButOlder, fartherButNewer));
        Assert.Equal("NEWER", Picked(14285.0, fartherButNewer, nearButOlder));
    }

    [Theory]
    [MemberData(nameof(SharedQrtComments))]
    public void A_comment_says_QRT_as_in_the_app_and_such_a_spot_is_left_out(string? comment, bool qrt)
    {
        Assert.Equal(qrt, QrtComment.Says(comment));
        Assert.Equal(!qrt, Picked(14285, Spot(comment: comment)) is not null);
    }

    [Theory]
    [InlineData(SpotProgram.Dx)]
    [InlineData(SpotProgram.Pota)]
    [InlineData(SpotProgram.Sota)]
    [InlineData(SpotProgram.Wwff)]
    public void QRT_written_in_the_comment_counts_in_every_program(SpotProgram program)
    {
        Assert.Null(Picked(14285, Spot(program: program, comment: "qrt off to next peak")));
    }

    [Fact]
    public void QRT_written_in_the_comment_leaves_room_for_the_station_still_on_air()
    {
        var qrt = Spot("QRT", minutesAgo: 1, comment: "now QRT, 73");
        var older = Spot("ON_AIR", minutesAgo: 5, comment: "CQ POTA");

        Assert.Equal("ON_AIR", Picked(14285, qrt, older));
    }

    [Fact]
    public void QRTX_in_the_comment_is_not_a_QRT()
    {
        Assert.Equal("K1ABC", Picked(14285, Spot(comment: "pse QRTX")));
    }

    [Fact]
    public void A_spot_of_exactly_10_minutes_ago_counts()
    {
        Assert.Equal("K1ABC", Picked(14285, Spot(minutesAgo: 10)));
    }

    [Fact]
    public void A_spot_of_one_second_more_than_10_minutes_ago_does_not()
    {
        var spot = Spot() with { SpotTime = Now.AddMinutes(-10).AddSeconds(-1) };

        Assert.Null(Picked(14285, spot));
    }

    [Fact]
    public void A_spot_with_a_time_slightly_ahead_of_the_server_counts()
    {
        Assert.Equal("K1ABC", Picked(14285, Spot(minutesAgo: -2)));
    }

    [Fact]
    public void A_spot_of_exactly_5_minutes_ahead_counts()
    {
        Assert.Equal("K1ABC", Picked(14285, Spot(minutesAgo: -5)));
    }

    [Fact]
    public void A_spot_of_one_second_more_than_5_minutes_ahead_does_not()
    {
        var spot = Spot() with { SpotTime = Now.AddMinutes(5).AddSeconds(1) };

        Assert.Null(Picked(14285, spot));
    }

    [Fact]
    public void A_spot_far_in_the_future_does_not_hide_the_one_on_air()
    {
        Assert.Equal("ON_AIR", Picked(14285, Spot("FUTURE", minutesAgo: -60), Spot("ON_AIR", minutesAgo: 1)));
    }

    [Theory]
    [InlineData("AM", 3, 3.1)]
    [InlineData("FM", 3, 3.1)]
    [InlineData("CW", 0.5, 0.6)]
    [InlineData("USB", 1.5, 1.6)]
    [InlineData("LSB", 1.5, 1.6)]
    [InlineData("RTTY", 1, 1.1)]
    [InlineData("", 1, 1.1)]
    [InlineData("XYZ", 1, 1.1)]
    public void Every_mode_has_its_tolerance_both_ways(string mode, double inside, double outside)
    {
        Assert.Equal("K1ABC", Picked(14285 + inside, Spot(mode: mode)));
        Assert.Equal("K1ABC", Picked(14285 - inside, Spot(mode: mode)));
        Assert.Null(Picked(14285 + outside, Spot(mode: mode)));
        Assert.Null(Picked(14285 - outside, Spot(mode: mode)));
    }

    [Fact]
    public void An_FT8_spot_is_found_on_its_calling_frequency()
    {
        var ft8 = Spot(khz: 14075.8, mode: "FT8");

        Assert.Equal("K1ABC", Picked(14074.0, ft8));
        Assert.Equal("K1ABC", Picked(14074.1, ft8));
        Assert.Equal("K1ABC", Picked(14073.9, ft8));
        Assert.Null(Picked(14074.2, ft8));
        Assert.Null(Picked(14073.8, ft8));
        Assert.Null(Picked(14075.8, ft8));
    }

    [Theory]
    [InlineData("FT4", 14081.5, 14080.0)]
    [InlineData("JS8", 14079.0, 14078.0)]
    public void FT4_and_JS8_spots_too(string mode, double spotKhz, double dial)
    {
        Assert.Equal("K1ABC", Picked(dial, Spot(khz: spotKhz, mode: mode)));
        Assert.Null(Picked(dial + 0.2, Spot(khz: spotKhz, mode: mode)));
    }

    [Fact]
    public void An_FT8_spot_outside_every_window_has_the_digital_tolerance()
    {
        Assert.Equal("K1ABC", Picked(14090.0, Spot(khz: 14091.0, mode: "FT8")));
        Assert.Null(Picked(14090.0, Spot(khz: 14091.1, mode: "FT8")));
    }

    [Fact]
    public void A_QRT_spot_is_left_out_even_when_it_is_the_most_recent()
    {
        var qrt = Spot("QRT", minutesAgo: 1, qrt: true);
        var older = Spot("ON_AIR", minutesAgo: 5);

        Assert.Equal("ON_AIR", Picked(14285, qrt, older));
        Assert.Null(Picked(14285, qrt));
    }

    [Fact]
    public void A_spot_without_a_frequency_is_left_out()
    {
        Assert.Null(Picked(0.5, Spot(khz: 0, mode: "CW")));
        Assert.Null(Picked(1, Spot(khz: 0, mode: "AM")));
    }

    [Theory]
    [InlineData(SpotProgram.Dx)]
    [InlineData(SpotProgram.Pota)]
    [InlineData(SpotProgram.Sota)]
    [InlineData(SpotProgram.Wwff)]
    public void Every_program_counts(SpotProgram program)
    {
        Assert.Equal("K1ABC", Picked(14285, Spot(program: program)));
    }

    [Fact]
    public void No_spot_means_no_suggestion()
    {
        Assert.Null(Picked(14285));
    }

    [Fact]
    public void At_the_same_time_the_nearest_wins()
    {
        var far = Spot("FAR", khz: 14286, seq: 9);
        var near = Spot("NEAR", khz: 14285, seq: 1);

        Assert.Equal("NEAR", Picked(14285, far, near));
        Assert.Equal("NEAR", Picked(14285, near, far));
    }

    [Fact]
    public void At_the_same_time_and_distance_the_last_saved_wins()
    {
        var first = Spot("FIRST", khz: 14284, seq: 1);
        var last = Spot("LAST", khz: 14286, seq: 2);

        Assert.Equal("LAST", Picked(14285, first, last));
        Assert.Equal("LAST", Picked(14285, last, first));
    }

    [Fact]
    public void With_everything_equal_program_and_key_decide_whatever_the_order()
    {
        var pota = Spot("POTA1", program: SpotProgram.Pota);
        var dxB = Spot("B", program: SpotProgram.Dx);
        var dxA = Spot("A", program: SpotProgram.Dx);

        Assert.Equal("A", Picked(14285, pota, dxB, dxA));
        Assert.Equal("A", Picked(14285, dxA, dxB, pota));
    }

    [Fact]
    public void The_radius_of_the_candidates_is_the_widest_reach_of_the_rule()
    {
        Assert.Equal(3.3, SpotOnFrequency.CandidateRadiusKhz, 6);
    }

    [Fact]
    public void Every_spot_of_every_window_is_inside_the_radius_of_the_candidates()
    {
        foreach (var c in Cases("calling_dials").Where(c => c.GetProperty("dial").ValueKind != JsonValueKind.Null))
        {
            var spotKhz = c.GetProperty("khz").GetDouble();
            var dial = c.GetProperty("dial").GetDouble();
            foreach (var khz in new[] { dial - 0.1, dial + 0.1 })
            {
                Assert.NotNull(Picked(khz, Spot(khz: spotKhz, mode: c.GetProperty("mode").GetString()!)));
                Assert.InRange(Math.Abs(spotKhz - khz), 0, SpotOnFrequency.CandidateRadiusKhz);
            }
        }
    }

    [Fact]
    public void Every_spot_on_frequency_in_the_shared_cases_is_inside_the_radius_of_the_candidates()
    {
        foreach (var c in Cases("on_frequency").Where(c => c.GetProperty("on_frequency").GetBoolean()))
        {
            Assert.InRange(Math.Abs(c.GetProperty("spot_khz").GetDouble() - c.GetProperty("khz").GetDouble()), 0, SpotOnFrequency.CandidateRadiusKhz);
        }
    }
}
