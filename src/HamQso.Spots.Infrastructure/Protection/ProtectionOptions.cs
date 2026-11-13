using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Protection;

/// <summary>Configuration section "Protection" (F107, callsign protection). Needs Supabase:SecretKey.</summary>
public sealed class ProtectionOptions
{
    public const string Section = "Protection";

    /// <summary>Off: nothing is read for the protection (a local run).</summary>
    public bool Enabled { get; set; }

    /// <summary>The edge function that takes the uses.</summary>
    [Required]
    public string Function { get; set; } = "callsign_protection";

    /// <summary>How often the protected callsigns are read again.</summary>
    public TimeSpan CallsignsInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the dispatcher looks at its queue (the limits are in <c>ProtectionLimits</c>).</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The PSKReporter MQTT feed (a flag of its own: it is the only one that opens a new connection).</summary>
    public bool PskReporterEnabled { get; set; }

    [Required]
    public string PskReporterHost { get; set; } = "mqtt.pskreporter.info";

    /// <summary>1884 is MQTT over TLS; 1883 plain.</summary>
    [Range(1, 65535)]
    public int PskReporterPort { get; set; } = 1884;

    public bool PskReporterTls { get; set; } = true;

    /// <summary>More callsigns than this are not subscribed (the filter stays narrow).</summary>
    [Range(1, 100_000)]
    public int PskReporterMaxCallsigns { get; set; } = 5000;
}
