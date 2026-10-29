using HamQso.Spots.Infrastructure.Proxy;

namespace HamQso.Spots.Infrastructure.Tests.Proxy;

public class UpstreamUriTests
{
    private static readonly Uri Qrz = new("https://logbook.qrz.com");

    [Theory]
    [InlineData("api", "", "https://logbook.qrz.com/api")]
    [InlineData("api", "?KEY=abc&ACTION=STATUS", "https://logbook.qrz.com/api?KEY=abc&ACTION=STATUS")]
    [InlineData("qslcard/ImportADIF.cfm", "?ADIFData=%3Ccall%3A5%3EK1ABC", "https://logbook.qrz.com/qslcard/ImportADIF.cfm?ADIFData=%3Ccall%3A5%3EK1ABC")]
    [InlineData("", "", "https://logbook.qrz.com/")]
    [InlineData("a b/c@d", "", "https://logbook.qrz.com/a%20b/c%40d")]
    public void The_path_and_query_go_on_the_service_host(string path, string query, string expected) =>
        Assert.Equal(expected, UpstreamUri.Build(Qrz, path, query)!.AbsoluteUri);

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("@evil.example/x")]
    [InlineData("http://evil.example/x")]
    [InlineData("\\\\evil.example\\x")]
    public void A_path_never_changes_the_host(string path) =>
        Assert.Equal("logbook.qrz.com", UpstreamUri.Build(Qrz, path, "")!.Host);

    [Theory]
    [InlineData("../admin", "")]
    [InlineData("a/./b", "")]
    [InlineData("api", "KEY=abc")]
    [InlineData("api", "?a=b#frag")]
    [InlineData("api", "?a=b\r\nHost: evil")]
    public void Dot_segments_and_odd_queries_are_refused(string path, string query) =>
        Assert.Null(UpstreamUri.Build(Qrz, path, query));

    [Theory]
    [InlineData("https://logbook.qrz.com/other", true)]
    [InlineData("https://LOGBOOK.qrz.com/", true)]
    [InlineData("http://logbook.qrz.com/", false)]
    [InlineData("https://logbook.qrz.com:8443/", false)]
    [InlineData("https://evil.example/", false)]
    [InlineData("https://qrz.com/", false)]
    public void Only_the_same_scheme_host_and_port_is_the_same_origin(string target, bool same) =>
        Assert.Equal(same, UpstreamUri.IsSameOrigin(new Uri(target), Qrz));

    [Fact]
    public void The_production_allowlist_is_the_eight_services_of_the_app()
    {
        Assert.Equal(
            ["country-files", "eqsl", "hamqsl", "hrdlog", "qrz-logbook", "sota-sso", "sotadata", "sotawatch"],
            ProxyAllowlist.Production.Keys.Order(StringComparer.Ordinal));
        Assert.All(ProxyAllowlist.Production.Keys, k => Assert.Equal("https", ProxyAllowlist.Production.BaseOf(k)!.Scheme));
        Assert.Null(ProxyAllowlist.Production.BaseOf("anything-else"));
    }
}
