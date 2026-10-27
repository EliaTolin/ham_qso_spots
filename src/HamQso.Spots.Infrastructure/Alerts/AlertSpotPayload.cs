using System.Globalization;
using HamQso.Spots.Domain.Radio;
using HamQso.Spots.Domain.Spots;

namespace HamQso.Spots.Infrastructure.Alerts;

/// <summary>
/// A spot as the alerts edge function reads it: the NormalizedSpot of
/// match_alerts (ham_qso_supabase/supabase/functions/match_alerts/types.ts)
/// without us_states, which Supabase resolves, plus the POTA location.
/// </summary>
public static class AlertSpotPayload
{
    public static Dictionary<string, object?> From(Spot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);
        return new()
        {
            ["source"] = spot.Program.ToString().ToUpperInvariant(),
            ["spot_key"] = spot.SpotKey,
            ["callsign"] = spot.Callsign,
            ["frequency_khz"] = spot.FrequencyKhz,
            ["band"] = spot.Band,
            // As in match_alerts: only a reported mode is pushed; the band plan one feeds the filters.
            ["mode"] = spot.ModeSource == ModeSource.Reported && spot.Mode.Length > 0 ? spot.Mode : null,
            ["mode_category"] = spot.ModeCategory?.ToString().ToUpperInvariant(),
            ["ref"] = spot.Reference,
            ["location"] = spot.Location,
            ["dxcc"] = spot.Dxcc,
            ["latitude"] = spot.Latitude,
            ["longitude"] = spot.Longitude,
            ["spotter"] = spot.Spotters.Count > 0 ? spot.Spotters[0] : null,
            ["comment"] = spot.Comment,
            ["spot_time"] = spot.SpotTime.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        };
    }
}
