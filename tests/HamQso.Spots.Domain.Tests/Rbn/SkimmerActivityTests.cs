using HamQso.Spots.Domain.Rbn;

namespace HamQso.Spots.Domain.Tests.Rbn;

/// <summary>Plan F71, section 4: a skimmer listens on a band if it sent at least one spot there from 10 minutes before the test to its end.</summary>
public class SkimmerActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 40, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 14, 32, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 6, 14, 33, 17, TimeSpan.Zero);

    [Fact]
    public void Without_from_and_to_the_last_10_minutes()
    {
        var w = SkimmerActivity.Window(null, null, Now);

        Assert.Equal(new ActivityWindow(Now.AddMinutes(-10), Now), w);
    }

    [Fact]
    public void With_from_and_to_from_10_minutes_before_the_start_to_the_end()
    {
        var w = SkimmerActivity.Window(Start, End, Now);

        Assert.Equal(new ActivityWindow(new DateTimeOffset(2026, 11, 6, 14, 22, 5, TimeSpan.Zero), End), w);
    }

    [Fact]
    public void A_window_may_reach_48_hours_back_and_no_further()
    {
        Assert.NotNull(SkimmerActivity.Window(Now.AddHours(-48).AddMinutes(10), Now.AddHours(-47), Now));
        Assert.Null(SkimmerActivity.Window(Now.AddHours(-48).AddMinutes(9), Now.AddHours(-47), Now));
    }

    [Fact]
    public void Only_from_or_only_to_from_after_to_or_a_window_in_the_future_is_not_valid()
    {
        Assert.Null(SkimmerActivity.Window(Start, null, Now));
        Assert.Null(SkimmerActivity.Window(null, End, Now));
        Assert.Null(SkimmerActivity.Window(End, Start, Now));
        Assert.Null(SkimmerActivity.Window(Now, Now.AddMinutes(5), Now));
    }

    [Fact]
    public void A_small_clock_difference_of_the_client_is_accepted() =>
        Assert.NotNull(SkimmerActivity.Window(Now.AddMinutes(-2), Now.AddSeconds(30), Now));

    [Fact]
    public void The_minutes_of_the_window_count_on_the_band_asked_only()
    {
        var w = SkimmerActivity.Window(Start, End, Now)!;
        ActivityMinute[] minutes =
        [
            new("OK4YL", "20m", At(14, 22), 10, At(14, 22, 40)), // the minute of the window's start counts
            new("OK4YL", "20m", At(14, 31), 32, At(14, 31, 40)),
            new("OK4YL", "40m", At(14, 31), 99, At(14, 31, 50)), // another band
            new("Z61DX", "20m", At(14, 29), 3, At(14, 29, 2)),
            new("W3LPL", "20m", At(14, 21), 7, At(14, 21, 59)), // before the window
            new("EA5WU", "20m", At(14, 34), 5, At(14, 34, 1)), // after the end
        ];

        var listening = SkimmerActivity.Listening(minutes, "20m", w);

        Assert.Equal(
            [new ListeningSkimmer("OK4YL", 42, At(14, 31, 40)), new ListeningSkimmer("Z61DX", 3, At(14, 29, 2))],
            listening);
    }

    [Fact]
    public void Most_spots_first_then_by_call()
    {
        var w = new ActivityWindow(At(14, 0), At(14, 59));
        ActivityMinute[] minutes = [new("B1B", "20m", At(14, 1), 1, At(14, 1)), new("A1A", "20m", At(14, 1), 1, At(14, 1)), new("C1C", "20m", At(14, 2), 9, At(14, 2))];

        Assert.Equal(["C1C", "A1A", "B1B"], SkimmerActivity.Listening(minutes, "20m", w).Select(s => s.Call));
    }

    [Fact]
    public void The_minute_of_a_moment() => Assert.Equal(At(14, 32), SkimmerActivity.MinuteOf(Start));

    private static DateTimeOffset At(int h, int m, int s = 0) => new(2026, 11, 6, h, m, s, TimeSpan.Zero);
}
