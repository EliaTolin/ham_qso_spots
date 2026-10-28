using System.Text;
using System.Text.Json;

namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>
/// An anonymous HamQSO user made for one test run, Pro (so its alerts can fire)
/// and without devices (so no push reaches a phone). Deleting it deletes its
/// subscription, alerts and alert events (on delete cascade).
/// </summary>
public sealed class TestUser : IAsyncDisposable
{
    private readonly HttpClient admin;

    private TestUser(HttpClient admin, string id, string accessToken)
    {
        this.admin = admin;
        Id = id;
        AccessToken = accessToken;
    }

    public string Id { get; }

    /// <summary>Supabase access token: the MQTT password of an app.</summary>
    public string AccessToken { get; }

    public static async Task<TestUser> CreateAsync(CancellationToken ct)
    {
        using var anon = new HttpClient { BaseAddress = E2eSettings.Supabase };
        anon.DefaultRequestHeaders.Add("apikey", E2eSettings.PublishableKey);
        using var signup = await anon.PostAsync("auth/v1/signup", Json(new { }), ct);
        signup.EnsureSuccessStatusCode();
        using var session = JsonDocument.Parse(await signup.Content.ReadAsStringAsync(ct));
        var token = session.RootElement.GetProperty("access_token").GetString()!;
        var id = session.RootElement.GetProperty("user").GetProperty("id").GetString()!;

        var admin = Admin();
        var user = new TestUser(admin, id, token);
        using var pro = await admin.PostAsync("rest/v1/subscriptions", Json(new { user_id = id, is_pro = true, store = "e2e" }), ct);
        pro.EnsureSuccessStatusCode();
        return user;
    }

    /// <summary>An alert on one callsign, as the app creates it.</summary>
    public async Task<string> AddAlertAsync(string name, string callsign, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rest/v1/alerts") { Content = Json(new { user_id = Id, name, callsigns = new[] { callsign } }) };
        request.Headers.Add("Prefer", "return=representation");
        using var response = await admin.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return rows.RootElement[0].GetProperty("id").GetString()!;
    }

    /// <summary>The callsign of the user's profile, as the app saves it (null: none).</summary>
    public async Task SetCallsignAsync(string? callsign, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rest/v1/profiles") { Content = Json(new { id = Id, callsign }) };
        request.Headers.Add("Prefer", "resolution=merge-duplicates");
        using var response = await admin.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement[]> AlertEventsAsync(CancellationToken ct)
    {
        var body = await admin.GetStringAsync($"rest/v1/alert_events?user_id=eq.{Id}&select=alert_id,source,spot_key,callsign,band,push_sent_at", ct);
        using var rows = JsonDocument.Parse(body);
        return [.. rows.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    public async ValueTask DisposeAsync()
    {
        using var deleted = await admin.DeleteAsync($"auth/v1/admin/users/{Id}");
        deleted.EnsureSuccessStatusCode();
        admin.Dispose();
    }

    /// <summary>PostgREST and Auth admin with the project's secret key.</summary>
    public static HttpClient Admin()
    {
        var http = new HttpClient { BaseAddress = E2eSettings.Supabase };
        http.DefaultRequestHeaders.Add("apikey", E2eSettings.SecretKey);
        return http;
    }

    public static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
