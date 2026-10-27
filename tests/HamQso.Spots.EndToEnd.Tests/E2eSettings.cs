namespace HamQso.Spots.EndToEnd.Tests;

/// <summary>Where the deployed system is. Keys come from the environment, never from the repo.</summary>
public static class E2eSettings
{
    public static Uri Server { get; } = new(Env("SPOTS_E2E_SERVER", "https://spot.hamqso.app"));

    public static Uri Broker { get; } = new(Env("SPOTS_E2E_BROKER", "wss://spot.hamqso.app/mqtt"));

    public static Uri Supabase { get; } = new(Env("SUPABASE_URL", "https://mpyvdejoqfgsupiqtkcp.supabase.co"));

    public static string PublishableKey => Required("SUPABASE_PUBLISHABLE_KEY");

    public static string SecretKey => Required("SUPABASE_SECRET_KEY");

    private static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
        ? v
        : throw new InvalidOperationException($"{name} is not set: the end-to-end tests need it (see the .csproj).");
}
