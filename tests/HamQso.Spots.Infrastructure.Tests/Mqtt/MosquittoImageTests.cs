using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using MQTTnet;

namespace HamQso.Spots.Infrastructure.Tests.Mqtt;

/// <summary>B21: on Coolify nothing from the repo is mounted, so the image must carry its configuration.</summary>
public sealed class MosquittoImageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.yaml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public async Task The_Mosquitto_image_starts_with_its_own_configuration_and_refuses_anonymous_clients()
    {
        var ct = TestContext.Current.CancellationToken;
        var image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Path.Combine(RepoRoot(), "deploy", "mosquitto"))
            .WithDockerfile("Dockerfile")
            .WithName($"hamqso-mosquitto-test:{Guid.NewGuid():N}")
            .WithCleanUp(true)
            .Build();
        await image.CreateAsync(ct);

        await using var mosquitto = new ContainerBuilder(image)
            .WithPortBinding(1883, true)
            .WithPortBinding(9001, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883).UntilInternalTcpPortIsAvailable(9001))
            .Build();
        await mosquitto.StartAsync(ct);

        using var client = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder().WithTcpServer(mosquitto.Hostname, mosquitto.GetMappedPublicPort(1883)).WithTimeout(TimeSpan.FromSeconds(10)).Build();
        // MQTTnet 5 returns the refusal as a result code, it does not throw.
        var result = await client.ConnectAsync(options, ct);

        Assert.Equal(MqttClientConnectResultCode.NotAuthorized, result.ResultCode);
    }
}
