using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Application.Health;

/// <summary>Health that nobody reads: for tools and tests that do not serve /health.</summary>
public sealed class NullSourceHealth : ISourceHealth
{
    public static readonly NullSourceHealth Instance = new();

    private NullSourceHealth()
    {
    }

    public void Succeeded(string source)
    {
    }

    public void Failed(string source, string reason)
    {
    }
}
