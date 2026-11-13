using HamQso.Spots.Application.Ports;
using HamQso.Spots.Application.Protection;
using HamQso.Spots.Domain.Protection;

namespace HamQso.Spots.Application.Tests.Protection;

internal sealed class FakeCallsignSource(params string[] callsigns) : IProtectedCallsignSource
{
    public bool Fails { get; set; }

    public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken) =>
        Fails ? throw new HttpRequestException("supabase down") : Task.FromResult<IReadOnlyList<string>>(callsigns);
}

/// <summary>Takes the batches; throws what <see cref="Next"/> says, one per call, then answers.</summary>
internal sealed class FakeUplink : IProtectionUplink
{
    public List<ProtectionBatch> Batches { get; } = [];

    public Queue<Exception> Next { get; } = new();

    public Task SendAsync(ProtectionBatch batch, CancellationToken cancellationToken)
    {
        Batches.Add(batch);
        if (Next.TryDequeue(out var e)) throw e;
        return Task.CompletedTask;
    }
}

internal sealed class FakeProbe(ProtectionSignal signal, string? problem = null) : IProtectionProbe
{
    public ProtectionSignal Signal => signal;

    public string? Problem { get; set; } = problem;

    string? IProtectionProbe.Problem() => Problem;
}

internal sealed class RecordingHealth : ISourceHealth
{
    public List<(string Source, bool Ok, string? Reason)> Calls { get; } = [];

    public void Succeeded(string source) => Calls.Add((source, true, null));

    public void Failed(string source, string reason) => Calls.Add((source, false, reason));
}

internal static class Protected
{
    public static async Task<ProtectionCollector> CollectorAsync(UseAggregator aggregator, params string[] callsigns)
    {
        var registry = new ProtectedCallsignRegistry(new FakeCallsignSource(callsigns), new RecordingHealth());
        await registry.RefreshAsync(CancellationToken.None);
        return new ProtectionCollector(registry, aggregator);
    }

    public static PskReport Psk(string sender = "IN3FOB", string receiver = "DL1ABC", string band = "20m", double khz = 14074.5) =>
        new(sender, receiver, "JO40", band, khz, "FT8", -12);
}
