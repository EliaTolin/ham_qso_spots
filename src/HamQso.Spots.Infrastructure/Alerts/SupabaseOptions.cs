using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Alerts;

/// <summary>Configuration section "Supabase". The secret key comes from user secrets (dev) or Coolify (prod).</summary>
public sealed class SupabaseOptions
{
    public const string Section = "Supabase";

    [Required]
    public Uri Url { get; set; } = new("https://mpyvdejoqfgsupiqtkcp.supabase.co");

    [Required]
    public string SecretKey { get; set; } = "";

    /// <summary>Edge function that matches the alerts and sends the pushes.</summary>
    [Required]
    public string AlertsFunction { get; set; } = "alert_spots";
}
