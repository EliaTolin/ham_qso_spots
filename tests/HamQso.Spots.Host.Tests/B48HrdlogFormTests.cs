using System.Net;
using System.Text;

namespace HamQso.Spots.Host.Tests;

/// <summary>
/// B48: HRDLog on the web refused the upload code ("Invalid token"). The
/// HRDLog form (Callsign, Code, App, ADIFData) has to reach HRDLog through the
/// proxy byte for byte, with a code holding the characters a form encodes.
/// </summary>
public sealed class B48HrdlogFormTests : IAsyncLifetime
{
    private readonly TestHost host = new();
    private FakeUpstream upstream = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        upstream = await FakeUpstream.StartAsync();
        host.Upstreams["hrdlog"] = upstream.BaseUri;
    }

    public async ValueTask DisposeAsync()
    {
        await host.DisposeAsync();
        await upstream.DisposeAsync();
    }

    [Fact]
    public async Task Scrivo_nominativo_e_codice_giusti_sul_web_tocco_Collega_il_codice_arriva_a_HRDLog_come_l_ho_scritto()
    {
        const string code = "Ab+1/2=3&x y%é";
        var form = "Callsign=IU2ABC&Code=" + Uri.EscapeDataString(code) + "&App=HamQSO+3.1&ADIFData=";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/proxy/hrdlog/echo/NewEntry.aspx")
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        using var http = host.Client(token: null);
        http.DefaultRequestHeaders.Add("X-HamQSO-Auth", $"Bearer {TestHost.ValidToken}");

        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seen = Assert.Single(upstream.Requests);
        Assert.Equal(("POST", "/echo/NewEntry.aspx"), (seen.Method, seen.Target));
        Assert.StartsWith("application/x-www-form-urlencoded", seen.Headers["Content-Type"], StringComparison.Ordinal);
        Assert.Equal(form, Encoding.UTF8.GetString(seen.Body));
        var fields = System.Web.HttpUtility.ParseQueryString(Encoding.UTF8.GetString(seen.Body));
        Assert.Equal(("IU2ABC", code), (fields["Callsign"], fields["Code"]));
    }
}
