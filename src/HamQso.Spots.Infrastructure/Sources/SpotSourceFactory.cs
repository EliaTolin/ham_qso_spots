using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure.Sources.Http;
using HamQso.Spots.Infrastructure.Sources.Telnet;

namespace HamQso.Spots.Infrastructure.Sources;

/// <summary>
/// The active sources, from configuration: every enabled telnet node and HTTP
/// feed, all behind <see cref="ISpotSource"/>. A new feed (RBN, PSKReporter) is
/// one more entry in <see cref="Feeds"/>.
/// </summary>
public static class SpotSourceFactory
{
    /// <summary>Every HTTP feed with its default interval (HamQTH asks for at least 15 s).</summary>
    public static readonly IReadOnlyList<(IPolledFeed Feed, TimeSpan Interval)> Feeds =
    [
        (new HamQthFeed(), TimeSpan.FromSeconds(15)),
        (new DxSummitFeed(), TimeSpan.FromSeconds(30)),
        (new PotaFeed(), TimeSpan.FromSeconds(30)),
        (new SotaFeed(), TimeSpan.FromMinutes(1)),
        (new WwffFeed(), TimeSpan.FromMinutes(1)),
    ];

    public static IReadOnlyList<ISpotSource> Create(
        TelnetOptions telnet,
        IReadOnlyDictionary<string, FeedOptions> feeds,
        Func<string, HttpClient> http,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(telnet);
        ArgumentNullException.ThrowIfNull(feeds);
        ArgumentNullException.ThrowIfNull(http);
        var sources = new List<ISpotSource>();
        sources.AddRange(telnet.Nodes.Where(n => n.Enabled).Select(n => new TelnetNodeSource(n, telnet, time)));
        foreach (var (feed, interval) in Feeds)
        {
            var options = feeds.TryGetValue(feed.Name, out var o) ? o : new FeedOptions();
            if (!options.Enabled) continue;
            sources.Add(new PolledSpotSource(feed, http(feed.Name), options.Interval ?? interval, time));
        }
        return sources;
    }
}
