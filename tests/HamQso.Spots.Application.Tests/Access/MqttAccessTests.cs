using HamQso.Spots.Application.Access;
using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Application.Tests.Access;

public class MqttAccessTests
{
    private const string ValidToken = "valid.supabase.token";

    private readonly MqttAccess access = new(new FakeTokens(), "ingestor", "s3cret");

    private Task<bool> Login(string? username, string? password) =>
        access.AuthenticateAsync(username, password, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_HamQSO_user_with_a_valid_Supabase_token_can_read_the_spots()
    {
        Assert.True(await Login("8f2c-user-id", ValidToken));
        Assert.True(access.Authorize("8f2c-user-id", "spots/dx/20m/cw", MqttAccessKind.Subscribe));
        Assert.True(access.Authorize("8f2c-user-id", "spots/pota", MqttAccessKind.Read));
    }

    [Theory]
    [InlineData("8f2c-user-id", "expired.or.forged")]
    [InlineData("8f2c-user-id", "")]
    [InlineData("", ValidToken)]
    [InlineData(null, null)]
    public async Task Without_a_valid_Supabase_token_the_connection_is_refused(string? username, string? password) =>
        Assert.False(await Login(username, password));

    [Theory]
    [InlineData(MqttAccessKind.Write)]
    [InlineData(MqttAccessKind.ReadWrite)]
    public void No_app_can_publish(MqttAccessKind kind) =>
        Assert.False(access.Authorize("8f2c-user-id", "spots/dx/20m/cw", kind));

    [Fact]
    public async Task Only_the_server_account_publishes()
    {
        Assert.True(await Login("ingestor", "s3cret"));
        Assert.True(access.Authorize("ingestor", "spots/dx/20m/cw", MqttAccessKind.Write));
    }

    [Fact]
    public async Task The_server_account_cannot_be_taken_with_a_token_or_a_wrong_password()
    {
        Assert.False(await Login("ingestor", ValidToken));
        Assert.False(await Login("ingestor", "s3cre"));
    }

    [Fact]
    public async Task Without_a_publisher_password_configured_nobody_publishes() =>
        Assert.False(await new MqttAccess(new FakeTokens(), "ingestor", "").AuthenticateAsync("ingestor", "", TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("$SYS/broker/clients")]
    [InlineData("other/topic")]
    [InlineData("spotsx/dx")]
    [InlineData("")]
    public void Topics_outside_spots_are_closed_to_everyone(string topic)
    {
        Assert.False(access.Authorize("8f2c-user-id", topic, MqttAccessKind.Subscribe));
        Assert.False(access.Authorize("ingestor", topic, MqttAccessKind.Write));
    }

    private sealed class FakeTokens : ITokenValidator
    {
        public Task<bool> IsValidAsync(string token, CancellationToken cancellationToken) => Task.FromResult(token == ValidToken);
    }
}
