namespace HamQso.Voacap.Core;

/// <summary>POST /v1/effective-ssn: the coarse then fine search of <see cref="SsnSearch"/>, two runs of voacapl.</summary>
public sealed class EffectiveSsn(IVoacapEngine engine, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    // The MUF does not depend on these: an isotropic antenna, 100 W, 10 MHz, the usual settings.
    private static readonly ModelSettings Settings = new(30, -150, 3, 24);
    private const double Mhz = 10;

    public async Task<EffectiveSsnResult> ExecuteAsync(EffectiveSsnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var coarse = SsnSearch.Best(await MufsAsync(request, SsnSearch.Coarse(), cancellationToken).ConfigureAwait(false), request.MeasuredMufdMhz, request.SsnMonth);
        var fine = SsnSearch.Best(await MufsAsync(request, SsnSearch.Fine(coarse.Ssn), cancellationToken).ConfigureAwait(false), request.MeasuredMufdMhz, request.SsnMonth);
        return new EffectiveSsnResult(engine.Name, fine.Ssn, fine.Muf, 2);
    }

    private async Task<IReadOnlyList<(int Ssn, double Muf)>> MufsAsync(EffectiveSsnRequest r, IReadOnlyList<int> ssns, CancellationToken cancellationToken)
    {
        var (west, east) = SsnSearch.Circuit(r.Ionosonde);
        var deck = VoacapDeck.Build(new DeckSpec(clock.GetUtcNow().Year, r.Month, r.UtcHour, Mhz, Settings, west, AntennaFiles.Isotrope, 100, AntennaFiles.Isotrope,
            [.. ssns.Select(s => new DeckCircuit(east, s))]));
        var circuits = VoacapOutput.Parse(await engine.RunAsync(deck, [new AntennaFile(AntennaFiles.Isotrope, null)], cancellationToken).ConfigureAwait(false));
        if (circuits.Count != ssns.Count || circuits.Any(c => c.MufMhz is null)) throw new VoacapEngineException($"{circuits.Count} MUFs for {ssns.Count} circuits");
        return [.. ssns.Zip(circuits, (s, c) => (s, c.MufMhz!.Value))];
    }
}
