using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Mqtt;

/// <summary>Configuration section "Mqtt": how the server reaches Mosquitto (the only client allowed to publish).</summary>
public sealed class MqttOptions
{
    public const string Section = "Mqtt";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    [Required]
    public string ClientId { get; set; } = "hamqso-spots-ingestor";

    /// <summary>The publisher account: the broker lets only this user write.</summary>
    [Required]
    public string Username { get; set; } = "ingestor";

    [Required]
    public string Password { get; set; } = "";
}
