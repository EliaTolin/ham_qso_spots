using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HamQso.Spots.VoacapCalibration;

/// <summary>The VOACAP service (voacap/, contract plan F71 3.3): one scenario per call, the settings under test.</summary>
public sealed class VoacapClient(HttpClient http, Uri baseUrl, string token)
{
    public async Task<IReadOnlyDictionary<string, double>> PredictAsync(CalibrationEvent e, double ssn, double noiseDbw, double requiredSnr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        var body = new JsonObject
        {
            ["month"] = e.Start.Month,
            ["utc_hour"] = e.Start.Hour,
            ["frequency_mhz"] = Math.Round(e.FrequencyMhz, 3),
            ["settings"] = new JsonObject { ["method"] = 30, ["noise_dbw"] = noiseDbw, ["min_takeoff_deg"] = 3, ["required_snr_dbhz"] = requiredSnr },
            ["receive_antenna"] = new JsonObject { ["model"] = "isotropic", ["gain_dbi"] = 0 },
            ["transmitter"] = new JsonObject { ["lat"] = e.StationPosition.Lat, ["lon"] = e.StationPosition.Lon },
            // The reference station of the plan (decision 14): dipole at 10 m, 100 W. The real stations differ: that is what the margin measures.
            ["scenarios"] = new JsonArray(new JsonObject { ["id"] = "theory", ["ssn"] = ssn, ["power_w"] = 100, ["antenna"] = new JsonObject { ["model"] = "dipole", ["height_m"] = 10 } }),
            ["receivers"] = new JsonArray([.. e.Listening.Take(600).Select(l => (JsonNode)new JsonObject { ["id"] = l.Call, ["lat"] = l.Position!.Lat, ["lon"] = l.Position.Lon })]),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "/v1/predict")) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        return answer.GetProperty("results").GetProperty("theory").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("rel").GetDouble(), StringComparer.Ordinal);
    }
}
