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
    /// <summary>Sent with every HTTP request: SOTA asks for the app name and a callsign (B26).</summary>
    public const string UserAgent = "HamQSO/1.0 (IU4VRB; +https://hamqso.app)";

    /// <summary>
    /// Every HTTP feed with its default interval and the shortest one the
    /// configuration may set (HamQTH asks for at least 15 s; SOTA for at most
    /// one request every 60 s).
    /// </summary>
    public static readonly IReadOnlyList<(IPolledFeed Feed, TimeSpan Interval, TimeSpan MinInterval)> Feeds =
    [
        (new HamQthFeed(), TimeSpan.FromSeconds(15), TimeSpan.Zero),
        (new DxSummitFeed(), TimeSpan.FromSeconds(30), TimeSpan.Zero),
        (new PotaFeed(), TimeSpan.FromSeconds(30), TimeSpan.Zero),
        (new SotaFeed(), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)),
        (new WwffFeed(), TimeSpan.FromMinutes(1), TimeSpan.Zero),
    ];

    /// <summary>How every feed's <see cref="HttpClient"/> asks.</summary>
    public static void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = TimeSpan.FromSeconds(10);
    }

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
        foreach (var (feed, interval, minInterval) in Feeds)
        {
            var options = feeds.TryGetValue(feed.Name, out var o) ? o : new FeedOptions();
            if (!options.Enabled) continue;
            var every = options.Interval ?? interval;
            if (every < minInterval)
            {
                throw new InvalidOperationException($"Feeds:{feed.Name}:Interval is {every}: {feed.Name} must be asked at most once every {minInterval}");
            }
            sources.Add(new PolledSpotSource(feed, http(feed.Name), every, time));
        }
        return sources;
    }
}
