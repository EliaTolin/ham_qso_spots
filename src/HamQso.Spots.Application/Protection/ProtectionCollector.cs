using HamQso.Spots.Application.Ports;
using HamQso.Spots.Domain.Protection;
using HamQso.Spots.Domain.Radio;

namespace HamQso.Spots.Application.Protection;

/// <summary>
/// The door of the protection (F107): what the RBN, the cluster and PSKReporter hear goes in; whatever is
/// not about a protected callsign is dropped at once, and a station's own spot of itself is no use.
/// What stays is gathered by <see cref="UseAggregator"/>. It decides nothing about who is on air.
/// </summary>
public sealed class ProtectionCollector(ProtectedCallsignRegistry registry, UseAggregator aggregator, TimeProvider? time = null) : IProtectionTap
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public void OfferRbn(RbnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var protectedCalls = registry.Current;
        if (protectedCalls.Match(line.Call) is not { } owner) return;
        if (ProtectedCallsigns.StationOf(line.Spot.Skimmer) == owner) return;
        if (Bands.Of(line.Spot.Khz) is not { } band) return;
        aggregator.Add(owner, new Hearing(line.Call, ProtectionSignal.Skimmer, line.ReceivedAt, band, line.Spot.Khz, line.Spot.Mode, Skimmer(line.Spot.Skimmer), null, line.Spot.SnrDb));
    }

    public void OfferSpots(IEnumerable<AlertSpot> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var protectedCalls = registry.Current;
        if (protectedCalls.Count == 0) return;
        foreach (var row in accepted)
        {
            var spot = row.Spot;
            if (protectedCalls.Match(spot.Callsign) is not { } owner || spot.Band is not { } band) continue;
            foreach (var spotter in spot.Spotters)
            {
                if (ProtectedCallsigns.StationOf(spotter) == owner) continue;
                var isSkimmer = spotter.EndsWith("-#", StringComparison.Ordinal);
                // A skimmer is stamped with our clock, as the RBN lines are (ReceivedAt): the cluster's own clock
                // could put the same skimmer in the next minute and count it twice.
                aggregator.Add(owner, new Hearing(
                    spot.Callsign,
                    isSkimmer ? ProtectionSignal.Skimmer : ProtectionSignal.Spot,
                    isSkimmer ? clock.GetUtcNow() : spot.SpotTime,
                    band,
                    spot.FrequencyKhz,
                    string.IsNullOrEmpty(spot.Mode) ? null : spot.Mode,
                    spotter.ToUpperInvariant(),
                    null,
                    null));
            }
        }
    }

    public void OfferPsk(PskReport report, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        var protectedCalls = registry.Current;
        if (protectedCalls.Match(report.Sender) is not { } owner) return;
        if (ProtectedCallsigns.StationOf(report.Receiver) == owner) return;
        aggregator.Add(owner, new Hearing(report.Sender, ProtectionSignal.PskReporter, now, report.Band, report.FrequencyKhz, report.Mode, report.Receiver, report.ReceiverLocator, report.SnrDb));
    }

    /// <summary>The RBN writes the skimmer without "-#"; the cluster keeps it: one form, so the same skimmer counts once.</summary>
    private static string Skimmer(string skimmer) => skimmer.ToUpperInvariant() + "-#";
}
