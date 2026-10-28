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

    /// <summary>
    /// Other Supabase projects whose users may also read the spots (HamQRG):
    /// their access tokens are checked with their own public keys. Only
    /// <see cref="Url"/> receives the alerts.
    /// </summary>
    public Uri[] ReaderUrls { get; set; } = [];

    /// <summary>Edge function that matches the alerts and sends the pushes.</summary>
    [Required]
    public string AlertsFunction { get; set; } = "alert_spots";
}
