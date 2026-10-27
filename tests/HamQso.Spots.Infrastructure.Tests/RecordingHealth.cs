using HamQso.Spots.Application.Ports;

namespace HamQso.Spots.Infrastructure.Tests;

/// <summary>Records what a component tells /health, in order; safe across threads.</summary>
public sealed class RecordingHealth : ISourceHealth
{
    private readonly List<(string Source, bool Ok, string? Reason)> calls = [];

    public IReadOnlyList<(string Source, bool Ok, string? Reason)> Calls
    {
        get
        {
            lock (calls) return [.. calls];
        }
    }

    /// <summary>The sources reported failing, in order.</summary>
    public IReadOnlyList<string> Failures => [.. Calls.Where(c => !c.Ok).Select(c => c.Source)];

    /// <summary>The reasons of the failures, in order.</summary>
    public IReadOnlyList<string> Reasons => [.. Calls.Where(c => !c.Ok).Select(c => c.Reason!)];

    /// <summary>The last state reported by <paramref name="source"/>: true ok, false failing, null nothing yet.</summary>
    public bool? LastOf(string source) => Calls.LastOrDefault(c => c.Source == source) is { Source: not null } c ? c.Ok : null;

    public void Succeeded(string source)
    {
        lock (calls) calls.Add((source, true, null));
    }

    public void Failed(string source, string reason)
    {
        lock (calls) calls.Add((source, false, reason));
    }
}
