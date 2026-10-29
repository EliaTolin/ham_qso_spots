using System.Net;
using HamQso.Spots.Infrastructure.Tests.Rbn;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>
/// GET /v1/rbn as the web app reads it (fetch, Authorization header): a real
/// signed-in HamQSO user, the real token check and RBN feed of the
/// server run here, and a <see cref="FakeRbnNode"/> on loopback instead of
/// telnet.reversebeacon.net.
/// </summary>
public sealed class RbnTests : IAsyncLifetime
{
    private const string Mine = "DX de OK4YL-#:   7025.10  Q0E2E/P        CW     7 dB  23 WPM  CQ      2055Z";
    private const string Other = "DX de OK4YL-#:   7025.10  Q0E2F          CW     9 dB  23 WPM  CQ      2055Z";

    private readonly FakeRbnNode rbn = new();
    private LocalServer server = null!;
    private TestUser user = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        server = new LocalServer { RbnPort = rbn.Port };
        user = await TestUser.CreateWithAccountAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await user.DisposeAsync();
        await server.DisposeAsync();
        await rbn.DisposeAsync();
    }

    [Fact]
    public async Task A_signed_in_user_hears_their_station_on_the_shared_RBN_connection()
    {
        var http = server.CreateClient();
        http.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.AccessToken);
        using var response = await http.GetAsync(new Uri("/v1/rbn/Q0E2E", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        Assert.Equal(": connected", await reader.ReadLineAsync(Ct));

        await rbn.WaitForLoginAsync();
        await rbn.SendAsync(Other);
        await rbn.SendAsync(Mine);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        string? line;
        do line = await reader.ReadLineAsync(timeout.Token);
        while (line is not null && (line.Length == 0 || line.StartsWith(':')));

        Assert.Equal($"data: {Mine}", line);
        Assert.Equal("N0CALL-87", Assert.Single(rbn.Connections).Login);
    }

    [Fact]
    public async Task An_anonymous_user_needs_an_account_and_no_connection_is_opened()
    {
        await using var anonymous = await TestUser.CreateAsync(Ct);

        var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", anonymous.AccessToken);
        using var response = await http.GetAsync(new Uri("/v1/rbn/Q0E2E", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("account_required", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Empty(rbn.Connections);
    }

    [Fact]
    public async Task Without_a_token_it_is_a_401()
    {
        using var response = await server.CreateClient().GetAsync(new Uri("/v1/rbn/Q0E2E", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
