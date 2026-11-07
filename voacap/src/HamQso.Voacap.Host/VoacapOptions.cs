using System.ComponentModel.DataAnnotations;

namespace HamQso.Voacap.Host;

/// <summary>Configuration section "Voacap".</summary>
public sealed class VoacapOptions
{
    public const string Section = "Voacap";

    /// <summary>The Bearer token of the callers (Coolify and Supabase secret VOACAP_TOKEN). Required: no token, no start.</summary>
    [Required]
    [MinLength(16)]
    public string Token { get; set; } = "";

    /// <summary>voacapl's data folder (a writable copy of /usr/share/voacapl/itshfbc).</summary>
    [Required]
    public string Itshfbc { get; set; } = "/app/itshfbc";

    [Required]
    public string Executable { get; set; } = "voacapl";

    /// <summary>"voacapl 0.7.6": written by the Dockerfile from the package version.</summary>
    [Required]
    public string Engine { get; set; } = "voacapl";

    /// <summary>A run of voacapl longer than this is killed (the contract answers within 10 s).</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>voacapl processes at once; more wait their turn.</summary>
    [Range(1, 64)]
    public int MaxParallel { get; set; } = Math.Max(2, Environment.ProcessorCount);
}
