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
    public async Task A_HamQSO_user_gets_the_saved_spot_lists_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new MqttClientFactory().CreateMqttClient();
        var lists = new Dictionary<string, string>();
        client.ApplicationMessageReceivedAsync += e =>
        {
            lock (lists) lists[e.ApplicationMessage.Topic] = e.ApplicationMessage.ConvertPayloadToString();
            return Task.CompletedTask;
        };

        var connected = await client.ConnectAsync(Options(user.Id, user.AccessToken), ct);
        Assert.Equal(MqttClientConnectResultCode.Success, connected.ResultCode);
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("spots/dx/#").WithTopicFilter("spots/pota").Build(), ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        Dictionary<string, string> got;
        lock (lists) got = new(lists);
        Assert.Contains("spots/pota", got.Keys);
        var dx = got.Where(l => l.Key.StartsWith("spots/dx/", StringComparison.Ordinal)).Sum(l => SpotJson.Deserialize(l.Value).Length);
        Assert.InRange(dx, 1, 50);
        Assert.NotEmpty(SpotJson.Deserialize(got["spots/pota"]));
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
