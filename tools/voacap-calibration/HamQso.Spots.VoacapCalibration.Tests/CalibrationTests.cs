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

    private static HistorySpot Spot(string skimmer, string dx, int minute, string band = "20m", string kind = "CQ") =>
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
    public void A_CQ_of_a_station_of_known_locator_with_who_heard_it_and_who_was_listening()
    {
        HistorySpot[] spots =
        [
            Spot("OK4YL", "K1ABC", 0),           // OK4YL listening on 20 m before the CQ
            Spot("EA5WU", "K1ABC", -9),          // EA5WU too, 9 minutes before
            Spot("W3LPL", "K1ABC", -11),         // W3LPL 11 minutes before: not listening
            Spot("OK4YL", "DL1TX/P", 3),         // the CQ of DL1TX (portable), heard by OK4YL
            Spot("OK4YL", "DL1TX", 4),
            Spot("DL1TX", "K1ABC", 2),           // the station itself is a skimmer: never its own receiver
            Spot("EA5WU", "K1ABC", 5, "40m"),    // another band
        ];

        var e = Assert.Single(CalibrationEvents.Build(spots, Locate()));

        Assert.Equal(("DL1TX", "20m", T0, "JO62QM"), (e.Station, e.Band, e.Slot, e.StationLocator));
        Assert.Equal(["EA5WU", "OK4YL"], e.Listening.Select(l => l.Call).Order());
        Assert.Equal(["OK4YL"], e.Heard);
        Assert.Equal(14.025, e.FrequencyMhz, 3);
    }

    [Fact]
    public void A_station_without_a_node_locator_a_beacon_or_a_CQ_nobody_listened_to_is_no_event()
    {
        HistorySpot[] spots = [Spot("OK4YL", "K1ABC", 1), Spot("OK4YL", "DL1TX", 2, kind: "BEACON")];

        Assert.Empty(CalibrationEvents.Build(spots, Locate()));
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
        Assert.Equal((0, 1, 1), (s.Above, s.Near, s.Below));
    }
}
