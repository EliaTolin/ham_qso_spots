using System.Net.Http.Headers;
using HamQso.Spots.Application.Ports;
using HamQso.Spots.Infrastructure;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>GET /v1/spots as an app calls it.</summary>
public static class Api
{
    public static async Task<SpotPage> GetAsync(string token, string query, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = E2eSettings.Server };
        using var response = await SendAsync(http, token, query, ct);
        response.EnsureSuccessStatusCode();
        return SpotJson.Deserialize(await response.Content.ReadAsStringAsync(ct));
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient http, string token, string query, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("v1/spots" + query, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http.SendAsync(request, ct);
    }
}
