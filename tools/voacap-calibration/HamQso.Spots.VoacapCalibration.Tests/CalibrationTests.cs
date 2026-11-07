using HamQso.Spots.Application.Rbn;
using HamQso.Spots.Domain.Rbn;
using HamQso.Spots.VoacapCalibration;

namespace HamQso.Spots.VoacapCalibration.Tests;

public class HistoryLineTests
{
    [Fact]
    public void A_line_of_the_RBN_history_with_every_field()
    {
        var s = HistoryLine.Parse("K1HTV-4,K,NA,7044,40m,AI5SM,K,NA,CQ,35,2026-11-05 00:00:01,16,CW");

        Assert.Equal(new HistorySpot("K1HTV-4", 7044, "40m", "AI5SM", "CQ", 35, new DateTimeOffset(2026, 11, 5, 0, 0, 1, TimeSpan.Zero), "CW"), s);
    }

    [Theory]
    [InlineData("callsign,de_pfx,de_cont,freq,band,dx,dx_pfx,dx_cont,mode,db,date,speed,tx_mode")]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("K1HTV-4,K,NA,7044,40m,AI5SM,K,NA,CQ,35,2026-11-05 00:00:01,16")]
    [InlineData("K1HTV-4,K,NA,lots,40m,AI5SM,K,NA,CQ,35,2026-11-05 00:00:01,16,CW")]
    [InlineData("K1HTV-4,K,NA,7044,40m,AI5SM,K,NA,CQ,35,yesterday,16,CW")]
    [InlineData("K1HTV-4,K,NA,7044,40m,=cmd|' /C calc'!A0,K,NA,CQ,35,2026-11-05 00:00:01,16,CW")]
    [InlineData("K1HTV-4,K,NA,7044,11m,AI5SM,K,NA,CQ,35,2026-11-05 00:00:01,16,CW")]
    public void Anything_that_is_not_such_a_line_is_left_out(string line) => Assert.Null(HistoryLine.Parse(line));
}

public class CalibrationEventsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 14, 0, 0, TimeSpan.Zero);

    private static HistorySpot Spot(string skimmer, string dx, double minute, string band = "20m", string kind = "CQ") =>
        new(skimmer, band == "20m" ? 14025 : 7025, band, dx, kind, 10, T0.AddMinutes(minute), "CW");

    private static LocateSkimmer Locate()
    {
        var catalog = new RbnNodeCatalog();
        catalog.Set(
        [
            new RbnNode("DL1TX", "JO62QM", [], true, null),
            new RbnNode("OK4YL", "JN79FX", [], true, null),
            new RbnNode("EA5WU", "IM98WN", [], true, null),
            new RbnNode("W3LPL", "FM19LG", [], true, null),
        ], T0);
        return new LocateSkimmer(catalog);
    }

    [Fact]
    public void A_test_is_the_first_CQ_after_15_minutes_of_silence_heard_in_3_minutes_listened_to_from_10_minutes_before()
    {
        HistorySpot[] spots =
        [
            Spot("EA5WU", "K1ABC", -9),          // EA5WU listening on 20 m, 9 minutes before
            Spot("W3LPL", "K1ABC", -11),         // W3LPL 11 minutes before: not listening
            Spot("OK4YL", "DL1TX/P", 0),         // the start: DL1TX (portable) heard by OK4YL
            Spot("EA5WU", "DL1TX", 2.5),         // heard by EA5WU within the 3 minutes
            Spot("W3LPL", "DL1TX", 4),           // after the 3 minutes: neither heard nor listening (the window ends with the test)
            Spot("DL1TX", "K1ABC", 2),           // the station itself is a skimmer: never its own receiver
            Spot("EA5WU", "K1ABC", 1, "40m"),    // another band
        ];

        var e = Assert.Single(CalibrationEvents.Build(spots, Locate()));

        Assert.Equal(("DL1TX", "20m", T0, "JO62QM"), (e.Station, e.Band, e.Start, e.StationLocator));
        Assert.Equal(["EA5WU", "OK4YL"], e.Listening.Select(l => l.Call).Order());
        Assert.Equal(["EA5WU", "OK4YL"], e.Heard);
        Assert.Equal([new HeardSpot("EA5WU", 10, 150), new HeardSpot("OK4YL", 10, 0)], e.HeardSpots);
        Assert.Equal(14.025, e.FrequencyMhz, 3);
    }

    [Fact]
    public void A_station_already_on_the_air_is_no_new_test_until_15_minutes_of_silence()
    {
        HistorySpot[] spots =
        [
            Spot("OK4YL", "DL1TX", -14),  // a start
            Spot("OK4YL", "DL1TX", 0),    // 14 minutes later: still on the air, not a start
            Spot("EA5WU", "DL1TX", 16),   // 16 minutes after the last: a new start
        ];

        var events = CalibrationEvents.Build(spots, Locate());

        Assert.Equal([T0.AddMinutes(-14), T0.AddMinutes(16)], events.Select(e => e.Start));
    }

    [Fact]
    public void The_first_15_minutes_of_a_day_start_no_test_the_silence_before_is_not_known()
    {
        var midnight = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        HistorySpot[] spots = [Spot("OK4YL", "DL1TX", 0) with { At = midnight.AddMinutes(10) }, Spot("OK4YL", "DL1TX", 0) with { At = midnight.AddMinutes(40) }];

        Assert.Equal([midnight.AddMinutes(40)], CalibrationEvents.Build(spots, Locate()).Select(e => e.Start));
    }

    [Fact]
    public void A_station_without_a_node_locator_or_a_beacon_is_no_test()
    {
        HistorySpot[] spots = [Spot("OK4YL", "K1ABC", 1), Spot("OK4YL", "DL1TX", 2, kind: "BEACON")];

        Assert.Empty(CalibrationEvents.Build(spots, Locate()));
    }

    [Theory]
    [InlineData(14, 12.5, true)]  // Rome, 14 UTC: day
    [InlineData(2, 12.5, false)]
    [InlineData(13, -120, false)] // California, 13 UTC: 5 local
    [InlineData(22, -120, true)]
    public void Day_is_6_to_18_local_solar_time_of_the_station(int utcHour, double lon, bool day) =>
        Assert.Equal(day, CalibrationEvents.IsDay(new DateTimeOffset(2026, 11, 5, utcHour, 0, 0, TimeSpan.Zero), lon));
}

public class TestRowTests
{
    [Fact]
    public void A_simulated_test_with_expected_heard_farthest_continents_and_surprises()
    {
        var at = new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero);
        SkimmerInfo Sk(string call, double lat, double lon, string cont) => new(call, new SkimmerPosition(null, lat, lon, "node"), 1, cont);
        var e = new CalibrationEvent("DL1TX", "JO62QM", new SkimmerPosition("JO62QM", 52.5, 13.4, "node"), "20m", 14.025, at,
            [Sk("A", 52.5, 14.4, "EU"), Sk("B", 40.7, -74.0, "NA"), Sk("C", -33.9, 151.2, "OC"), Sk("D", 48.8, 2.3, "EU")],
            ["A", "B", "C"]);
        var rel = new Dictionary<string, double> { ["A"] = 0.9, ["B"] = 0.9, ["C"] = 0.2, ["D"] = 1.0 };

        var row = TestRow.Of(e, rel);

        Assert.Equal((4, 3.0, 3, 3, 1), (row.Listening, Math.Round(row.ExpectedTheory, 6), row.Heard, row.Continents, row.Surprises));
        Assert.InRange(row.FarthestKm, 16_000, 16_200);
        Assert.Equal(0, row.Margin!.Value, 6);
        Assert.Null(TestRow.Of(e, new Dictionary<string, double> { ["A"] = 0.1 }).Margin);
    }
}

public class SsnByMonthTests
{
    [Fact]
    public void NOAA_observed_SWPC_number_where_there_is_one_else_the_prediction()
    {
        var map = SsnByMonth.From(
            """[{"time-tag":"2026-09","ssn":45.7,"observed_swpc_ssn":68.73},{"time-tag":"2026-10","observed_swpc_ssn":61.71}]""",
            """[{"time-tag":"2026-10","predicted_ssn":82.9},{"time-tag":"2026-11","predicted_ssn":83.2}]""");

        Assert.Equal((68.73, 61.71, 83.2), (map[(2026, 9)], map[(2026, 10)], map[(2026, 11)]));
    }
}

public class SampleTests
{
    [Fact]
    public void At_most_n_per_band_and_day_or_night_spread_over_the_stations_the_same_with_the_same_seed()
    {
        var at = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 40).Select(i => new CalibrationEvent($"S{i % 4}", "JN45ON", new SkimmerPosition("JN45ON", 45, 9, "node"), i % 2 == 0 ? "20m" : "40m", 14, at.AddMinutes(i * 20), [], [])).ToArray();

        var a = Sample.Stratified(events, 5, 71);
        var b = Sample.Stratified(events, 5, 71);

        Assert.Equal(a, b);
        Assert.All(a.GroupBy(e => (e.Band, CalibrationEvents.IsDay(e.Start, e.StationPosition.Lon))), g => Assert.True(g.Count() <= 5));
        Assert.All(a.GroupBy(e => e.Band), g => Assert.True(g.Select(e => e.Station).Distinct().Count() >= 2));
    }
}

public class ScoreTests
{
    [Fact]
    public void Brier_score_expected_heard_and_the_margin_of_the_events_with_at_least_3_expected()
    {
        CalibrationOutcome[] outcomes =
        [
            new([(0.9, true), (0.8, true), (0.7, false), (0.6, true)]),   // expected 3.0, heard 3: margin 0
            new([(0.5, true), (0.5, false)]),                            // expected 1.0: no margin
            new([(1.0, true), (1.0, true), (1.0, false), (1.0, false)]), // expected 4, heard 2: margin -50%
        ];

        var s = Score.Of(outcomes);

        Assert.Equal(8.0, s.Expected, 6);
        Assert.Equal(6, s.Heard);
        Assert.Equal((0.01 + 0.04 + 0.49 + 0.16 + 0.25 + 0.25 + 0 + 0 + 1 + 1) / 10, s.Brier, 6);
        Assert.Equal(-0.25, s.MedianMargin, 6);
        // Percentiles by linear interpolation between the margins (-0.5 and 0).
        Assert.Equal(-0.45, s.P10Margin, 6);
        Assert.Equal(-0.05, s.P90Margin, 6);
        Assert.Equal(2, s.WithMargin);
        Assert.Equal((0, 1, 1), (s.Above, s.Near, s.Below));
    }
}
