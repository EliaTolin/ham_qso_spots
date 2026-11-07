using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Domain.Spots;
using Microsoft.Extensions.Time.Testing;

namespace HamQso.Spots.Application.Tests.Reading;

public class GetSpotOnFrequencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static Spot Spot(string call, double khz, string mode, int minutesAgo) => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = call,
        Callsign = call,
        FrequencyKhz = khz,
        Mode = mode,
        SpotTime = Now.AddMinutes(-minutesAgo),
        Feeds = ["DXFun"],
    };

    private static Task<Spot?> Get(FakeReader reader, double khz) =>
        new GetSpotOnFrequency(reader, new FakeTimeProvider(Now)).ExecuteAsync(khz, TestContext.Current.CancellationToken);

    [Fact]
    public async Task It_asks_the_archive_only_for_the_last_10_minutes_and_the_next_5_around_the_frequency()
    {
        var reader = new FakeReader([]);

        await Get(reader, 14074.0);

        Assert.Equal(Now.AddMinutes(-10), reader.Since);
        Assert.Equal(Now.AddMinutes(5), reader.Until);
        Assert.Equal(14074.0 - SpotOnFrequency.CandidateRadiusKhz, reader.FromKhz);
        Assert.Equal(14074.0 + SpotOnFrequency.CandidateRadiusKhz, reader.ToKhz);
    }

    [Fact]
    public async Task The_rule_chooses_among_what_the_archive_gives()
    {
        var reader = new FakeReader([Spot("OLD", 14285, "USB", 5), Spot("NEW", 14286, "USB", 1), Spot("FAR", 14288, "USB", 0)]);

        var spot = await Get(reader, 14285);

        Assert.Equal("NEW", spot?.Callsign);
    }

    [Fact]
    public async Task An_FT8_spot_off_the_dial_arrives_from_the_archive_and_is_chosen()
    {
        var reader = new FakeReader([Spot("JA1FT8", 14075.8, "FT8", 2)]);

        var spot = await Get(reader, 14074.0);

        Assert.Equal("JA1FT8", spot?.Callsign);
    }

    [Fact]
    public async Task Nothing_on_the_frequency_is_no_spot()
    {
        Assert.Null(await Get(new FakeReader([]), 14285));
    }

    private sealed class FakeReader(IReadOnlyList<Spot> spots) : ISpotReader
    {
        public double FromKhz { get; private set; }

        public double ToKhz { get; private set; }

        public DateTimeOffset Since { get; private set; }

        public DateTimeOffset Until { get; private set; }

        public Task<IReadOnlyList<Spot>> RecentNearAsync(double fromKhz, double toKhz, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
        {
            (FromKhz, ToKhz, Since, Until) = (fromKhz, toKhz, since, until);
            return Task.FromResult<IReadOnlyList<Spot>>([.. spots.Where(s => s.FrequencyKhz >= fromKhz && s.FrequencyKhz <= toKhz && s.SpotTime >= since && s.SpotTime <= until)]);
        }

        public Task<SpotPage> LatestAsync(SpotFilter filter, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SpotPage> ChangedSinceAsync(SpotFilter filter, long sinceSeq, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
