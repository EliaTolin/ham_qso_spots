using System.ComponentModel.DataAnnotations;

namespace HamQso.Spots.Infrastructure.Alerts;

/// <summary>
/// Configuration section "Supabase". The secret key comes from Coolify (prod) or
/// user secrets (dev); without it the server still runs (tokens are checked with
/// the public keys) but alerts and spot sending are off.
/// </summary>
public sealed class SupabaseOptions
{
    public const string Section = "Supabase";

    [Required]
    public Uri Url { get; set; } = new("https://mpyvdejoqfgsupiqtkcp.supabase.co");

    /// <summary>Empty: alerts and spot sending off.</summary>
    public string SecretKey { get; set; } = "";

    public bool HasSecretKey => !string.IsNullOrWhiteSpace(SecretKey);

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
