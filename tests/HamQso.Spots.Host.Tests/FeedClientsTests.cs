using HamQso.Spots.Infrastructure.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace HamQso.Spots.Host.Tests;

/// <summary>The HTTP clients of the feeds, as the real Host registers them (B26).</summary>
public sealed class FeedClientsTests : IDisposable
{
    private readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    [Fact]
    public void Every_feed_asks_with_a_User_Agent_that_has_the_app_name_and_the_callsign()
    {
        var factory = host.Services.GetRequiredService<IHttpClientFactory>();

        foreach (var (feed, _, _) in SpotSourceFactory.Feeds)
        {
            using var http = factory.CreateClient(feed.Name);
            var agent = http.DefaultRequestHeaders.UserAgent.ToString();

            Assert.StartsWith("HamQSO/", agent, StringComparison.Ordinal);
            Assert.Contains("IU4VRB", agent, StringComparison.Ordinal);
        }
    }
}
