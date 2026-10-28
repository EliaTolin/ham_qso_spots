using HamQso.Spots.Application.Sending;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Sending;

public class SpotRateLimiterTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly SpotRateLimiter limiter;

    public SpotRateLimiterTests() => limiter = new SpotRateLimiter(time);

    [Fact]
    public void Three_spots_a_minute_pass_the_fourth_waits_until_the_first_leaves_the_minute()
    {
        Assert.Null(limiter.TryAcquire("u"));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(limiter.TryAcquire("u"));
        Assert.Null(limiter.TryAcquire("u"));

        Assert.Equal(TimeSpan.FromSeconds(50), limiter.TryAcquire("u"));

        time.Advance(TimeSpan.FromSeconds(50));
        Assert.Null(limiter.TryAcquire("u"));
    }

    [Fact]
    public void Twenty_spots_an_hour_then_the_user_waits_for_the_hour()
    {
        for (var i = 0; i < 20; i++)
        {
            Assert.Null(limiter.TryAcquire("u"));
            time.Advance(TimeSpan.FromMinutes(1));
        }

        // 20 minutes went by: the first spot leaves the hour in 40.
        Assert.Equal(TimeSpan.FromMinutes(40), limiter.TryAcquire("u"));
        time.Advance(TimeSpan.FromMinutes(40));
        Assert.Null(limiter.TryAcquire("u"));
    }

    [Fact]
    public void A_refused_attempt_does_not_count()
    {
        for (var i = 0; i < 3; i++) limiter.TryAcquire("u");
        for (var i = 0; i < 10; i++) Assert.NotNull(limiter.TryAcquire("u"));

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(limiter.TryAcquire("u"));
    }

    [Fact]
    public void Every_user_has_their_own_limit()
    {
        for (var i = 0; i < 3; i++) limiter.TryAcquire("u");

        Assert.NotNull(limiter.TryAcquire("u"));
        Assert.Null(limiter.TryAcquire("v"));
    }

    [Fact]
    public void Users_silent_for_an_hour_are_forgotten_and_start_again()
    {
        for (var i = 0; i < 3; i++) limiter.TryAcquire("u");
        time.Advance(TimeSpan.FromHours(2));

        for (var i = 0; i < 3; i++) Assert.Null(limiter.TryAcquire("u"));
    }

    [Fact]
    public void With_both_limits_exceeded_the_longer_wait_wins()
    {
        var now = time.GetUtcNow();
        // 17 spots 59 minutes ago, 3 in the last 30 seconds: the minute frees in 30 s, the hour in 1 min.
        var times = Enumerable.Repeat(now.AddMinutes(-59), 17).Concat([now.AddSeconds(-30), now.AddSeconds(-20), now.AddSeconds(-10)]).ToList();

        Assert.Equal(TimeSpan.FromMinutes(1), SpotRateLimiter.RetryAfter(times, now));
        Assert.Equal(TimeSpan.FromSeconds(30), SpotRateLimiter.RetryAfter(times[^3..], now));
    }
}
