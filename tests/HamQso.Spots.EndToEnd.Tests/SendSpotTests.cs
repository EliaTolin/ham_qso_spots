using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HamQso.Spots.Infrastructure.Tests.Sending;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>
/// POST /v1/dx/spots as the web app calls it: a real signed-in HamQSO user and
/// its real profile in Supabase, the real token check, profile read, rate
/// limit and telnet sender of the server, run here (the deployed server would
/// spot on the real cluster) with the node set to a <see cref="FakeDxNode"/>
/// on loopback. Never a spot on the public cluster.
/// </summary>
public sealed class SendSpotTests : IAsyncLifetime
{
    private const string Callsign = "Q0E2E";
    private const string Line = "DX 14025.0 Q0E2F e2e test";

    private readonly FakeDxNode node = new();
    private ServerWithFakeNode server = null!;
    private TestUser user = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        server = new ServerWithFakeNode(node.Port);
        user = await TestUser.CreateWithAccountAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await user.DisposeAsync();
        await server.DisposeAsync();
        await node.DisposeAsync();
    }

    private async Task<HttpResponseMessage> Post(string? token, string line = Line, int ssid = 7)
    {
        var http = server.CreateClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.PostAsJsonAsync(new Uri("/v1/dx/spots", UriKind.Relative), new { line, ssid }, Ct);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonElement.Parse(await response.Content.ReadAsStringAsync(Ct));

    [Fact]
    public async Task A_user_spots_logged_in_with_the_callsign_of_the_profile_and_gets_the_node_reply()
    {
        await user.SetCallsignAsync(Callsign, Ct);

        using var first = await Post(user.AccessToken);
        using var again = await Post(user.AccessToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, again.StatusCode));
        var connection = Assert.Single(node.Connections);
        Assert.Equal([$"{Callsign}-7", Line, Line], connection.Lines);
        // The first is broadcast (the echo is a spot, it reaches the app with the others); the node refuses the second.
        Assert.True((await Body(first)).GetProperty("sent").GetBoolean());
        Assert.Equal(0, (await Body(first)).GetProperty("replies").GetArrayLength());
        Assert.Equal("Duplicate spot", (await Body(again)).GetProperty("replies")[0].GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not.a.token")]
    public async Task Without_a_valid_token_it_is_a_401_and_the_node_sees_nothing(string? token)
    {
        using var response = await Post(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(node.Connections);
    }

    [Fact]
    public async Task An_anonymous_user_needs_an_account_and_the_node_sees_nothing()
    {
        await using var anonymous = await TestUser.CreateAsync(Ct);
        await anonymous.SetCallsignAsync(Callsign, Ct);

        using var response = await Post(anonymous.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_required", (await Body(response)).GetProperty("error").GetString());
        Assert.Empty(node.Connections);
    }

    [Fact]
    public async Task A_profile_without_callsign_gets_no_callsign_and_the_node_sees_nothing()
    {
        await user.SetCallsignAsync(null, Ct);

        using var response = await Post(user.AccessToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("no_callsign", (await Body(response)).GetProperty("error").GetString());
        Assert.Empty(node.Connections);
    }

    [Fact]
    public async Task The_fourth_spot_in_a_minute_is_rate_limited()
    {
        await user.SetCallsignAsync(Callsign, Ct);
        for (var i = 0; i < 3; i++)
        {
            using var ok = await Post(user.AccessToken, $"DX 1402{i}.0 Q0E2F e2e test");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var response = await Post(user.AccessToken, "DX 14029.0 Q0E2F e2e test");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("rate_limited", (await Body(response)).GetProperty("error").GetString());
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(4, Assert.Single(node.Connections).Lines.Count);
    }

    /// <summary>The server with the production Supabase project and the DX node on loopback; no sources, archive or broker.</summary>
    private sealed class ServerWithFakeNode(int nodePort) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Spots", "Host=localhost;Database=none");
            builder.UseSetting("Supabase:Url", E2eSettings.Supabase.ToString());
            builder.UseSetting("Supabase:SecretKey", E2eSettings.SecretKey);
            builder.UseSetting("DxSend:Host", "127.0.0.1");
            builder.UseSetting("DxSend:Port", nodePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(s => s.RemoveAll<IHostedService>());
        }
    }
}
