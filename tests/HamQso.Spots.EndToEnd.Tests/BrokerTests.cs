using System.Net;
using System.Threading.Channels;
using HamQso.Spots.Domain.Spots;
using HamQso.Spots.Infrastructure;
using MQTTnet;
using MQTTnet.Formatter;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>The broker as an app sees it: MQTT over WebSocket, Supabase token as password.</summary>
public sealed class BrokerTests : IAsyncLifetime
{
    private TestUser user = null!;

    public async ValueTask InitializeAsync() => user = await TestUser.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await user.DisposeAsync();

    private static MqttClientOptions Options(string? username, string? password) =>
        new MqttClientOptionsBuilder()
            .WithWebSocketServer(o => o.WithUri(E2eSettings.Broker.ToString()))
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCredentials(username, password)
            .WithTimeout(TimeSpan.FromSeconds(15))
            .Build();

    [Fact]
    public async Task An_app_gets_the_starting_list_by_REST_then_the_news_by_MQTT_and_since_seq_finds_them()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new MqttClientFactory().CreateMqttClient();
        var news = Channel.CreateUnbounded<(string Topic, string Json)>();
        client.ApplicationMessageReceivedAsync += e => news.Writer.WriteAsync((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString())).AsTask();

        // 1. Subscribe first, as the app does, then 2. ask the starting list.
        Assert.Equal(MqttClientConnectResultCode.Success, (await client.ConnectAsync(Options(user.Id, user.AccessToken), ct)).ResultCode);
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("spots/dx/#").WithTopicFilter("spots/pota").Build(), ct);
        var start = await Api.GetAsync(user.AccessToken, "?program=dx", ct);
        Assert.True(start.Full);
        Assert.InRange(start.Spots.Count, 1, 200);
        Assert.True(start.Seq > 0);

        // 3. A piece of news (DX ~5-8 a minute, POTA every 30 s).
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(TimeSpan.FromSeconds(120));
        var (topic, json) = await news.Reader.ReadAsync(wait.Token);
        var page = SpotJson.Deserialize(json);
        Assert.False(page.Full);
        Assert.NotEmpty(page.Spots);
        var spot = page.Spots[0];
        Assert.NotNull(spot.Seq);
        Assert.StartsWith(spot.Program == SpotProgram.Dx ? "spots/dx/" : "spots/pota", topic, StringComparison.Ordinal);

        // 4. After a disconnection the app asks since_seq: the news is there.
        var program = spot.Program == SpotProgram.Dx ? "dx" : "pota";
        var since = await Api.GetAsync(user.AccessToken, $"?program={program}&since_seq={spot.Seq - 1}", ct);
        Assert.Contains(since.Spots, s => s.SpotKey == spot.SpotKey);
    }

    [Fact]
    public async Task Without_a_valid_token_the_starting_list_is_refused()
    {
        using var http = new HttpClient { BaseAddress = E2eSettings.Server };

        using var none = await http.GetAsync(new Uri("v1/spots?program=dx", UriKind.Relative), TestContext.Current.CancellationToken);
        using var forged = await Api.SendAsync(http, "not.a.token", "?program=dx", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (none.StatusCode, forged.StatusCode));
    }

    [Fact]
    public async Task A_HamQSO_user_cannot_publish()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(Options(user.Id, user.AccessToken), ct);

        var result = await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic("spots/dx/e2e/forged")
            .WithPayload("[]")
            .WithRetainFlag()
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);

        Assert.Equal(MqttClientPublishReasonCode.NotAuthorized, result.ReasonCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("8f2c-user", "not.a.token")]
    [InlineData("ingestor", "wrong-password")]
    public async Task Without_a_valid_Supabase_token_the_connection_is_refused(string? username, string? password)
    {
        using var client = new MqttClientFactory().CreateMqttClient();

        var result = await client.ConnectAsync(Options(username, password), TestContext.Current.CancellationToken);

        Assert.NotEqual(MqttClientConnectResultCode.Success, result.ResultCode);
    }

    [Fact]
    public async Task An_expired_or_foreign_token_is_refused()
    {
        // A well-formed ES256 token signed by another key.
        const string Foreign = "eyJhbGciOiJFUzI1NiIsImtpZCI6ImZvcmVpZ24iLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL21weXZkZWpvcWZnc3VwaXF0a2NwLnN1cGFiYXNlLmNvL2F1dGgvdjEiLCJhdWQiOiJhdXRoZW50aWNhdGVkIiwiZXhwIjo0MTAyNDQ0ODAwLCJzdWIiOiJ4In0.c2lnbmF0dXJlLW5vdC12YWxpZC1zaWduYXR1cmUtbm90LXZhbGlkLXNpZ25hdHVyZQ";
        using var client = new MqttClientFactory().CreateMqttClient();

        var result = await client.ConnectAsync(Options(user.Id, Foreign), TestContext.Current.CancellationToken);

        Assert.NotEqual(MqttClientConnectResultCode.Success, result.ResultCode);
    }
}
