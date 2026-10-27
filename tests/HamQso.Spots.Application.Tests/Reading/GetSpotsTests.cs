using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Reading;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Application.Tests.Reading;

public class GetSpotsTests
{
    private static readonly SpotFilter Dx = new(SpotProgram.Dx, [], []);

    private static Spot Spot(long seq) => new()
    {
        Program = SpotProgram.Dx,
        SpotKey = $"K{seq}",
        Callsign = $"K{seq}AA",
        FrequencyKhz = 14025,
        SpotTime = DateTimeOffset.UnixEpoch.AddMinutes(seq),
        Feeds = ["DXFun"],
        Seq = seq,
    };

    private static Task<SpotPage> Get(FakeReader reader, int? limit = null, long? since = null) =>
        new GetSpots(reader).ExecuteAsync(Dx, limit, since, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Without_since_seq_the_app_gets_the_latest_200_as_a_full_list()
    {
        var reader = new FakeReader(Enumerable.Range(1, 500).Select(i => Spot(i)));

        var page = await Get(reader);

        Assert.True(page.Full);
        Assert.Equal(200, page.Spots.Count);
        Assert.Equal(500, page.Seq);
        Assert.Equal(GetSpots.MaxLimit, reader.LastLimit);
    }

    [Fact]
    public async Task With_since_seq_the_app_gets_only_what_changed()
    {
        var reader = new FakeReader(Enumerable.Range(1, 500).Select(i => Spot(i)));

        var page = await Get(reader, since: 490);

        Assert.False(page.Full);
        Assert.Equal(Enumerable.Range(491, 10).Select(i => (long)i), page.Spots.Select(s => s.Seq!.Value));
    }

    [Fact]
    public async Task With_more_than_200_changes_the_app_gets_the_current_list_instead()
    {
        var reader = new FakeReader(Enumerable.Range(1, 1000).Select(i => Spot(i)));

        var page = await Get(reader, since: 10);

        Assert.True(page.Full);
        Assert.Equal(200, page.Spots.Count);
        Assert.Equal(1000, page.Spots.Max(s => s.Seq));
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(50, 50)]
    [InlineData(1000, 200)]
    [InlineData(0, 1)]
    public async Task The_limit_is_at_most_200(int? asked, int used)
    {
        var reader = new FakeReader(Enumerable.Range(1, 500).Select(i => Spot(i)));

        var page = await Get(reader, limit: asked);

        Assert.Equal(used, page.Spots.Count);
    }

    private sealed class FakeReader(IEnumerable<Spot> spots) : ISpotReader
    {
        private readonly Spot[] all = [.. spots];

        public int LastLimit { get; private set; }

        private long Seq => all.Length == 0 ? 0 : all.Max(s => s.Seq!.Value);

        public Task<SpotPage> LatestAsync(SpotFilter filter, int limit, CancellationToken cancellationToken)
        {
            LastLimit = limit;
            return Task.FromResult(new SpotPage(Seq, true, [.. all.OrderByDescending(s => s.SpotTime).Take(limit)]));
        }

        public Task<SpotPage> ChangedSinceAsync(SpotFilter filter, long sinceSeq, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new SpotPage(Seq, false, [.. all.Where(s => s.Seq > sinceSeq).OrderBy(s => s.Seq).Take(limit)]));
    }
}
