using HamQso.Voacap.Core;

namespace HamQso.Voacap.Tests;

/// <summary>The input deck of voacapl (method 30): fixed-width cards, antennas once, one EXECUTE per circuit.</summary>
public class VoacapDeckTests
{
    private static readonly ModelSettings Settings = new(30, -150, 3, 24);

    [Fact]
    public void A_deck_with_two_circuits_and_a_sunspot_change()
    {
        var deck = VoacapDeck.Build(new DeckSpec(
            2026, 11, 14, 14.045, Settings, new GeoPoint(45.6, 9.2), "hq/0123456789abcdef", 100, "default/isotrope",
            [new DeckCircuit(new GeoPoint(49.98, 14.46), 118), new DeckCircuit(new GeoPoint(-34.55, -58.38), 131)]));

        Assert.Equal(
            """
            LINEMAX      55       number of lines-per-page
            COEFFS    CCIR
            TIME         14   14    1    1
            MONTH      2026 11.00
            SUNSPOT    118.
            LABEL     TX                  RX
            CIRCUIT   45.60N     9.20E    49.98N    14.46E  S     0
            SYSTEM       1. 150. 3.00  90. 24.0 3.00 0.10
            FPROB      1.00 1.00 1.00 0.00
            ANTENNA       1    1    2   30     0.000[hq/0123456789abcdef  ]  0.0    0.1000
            ANTENNA       2    2    2   30     0.000[default/isotrope     ]  0.0    0.0000
            FREQUENCY 14.05 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00
            METHOD       30    0
            EXECUTE
            SUNSPOT    131.
            CIRCUIT   45.60N     9.20E    34.55S    58.38W  S     0
            EXECUTE
            QUIT

            """.ReplaceLineEndings("\n"),
            deck);
    }

    [Fact]
    public void Midnight_is_hour_24_for_voacapl() =>
        Assert.Contains("TIME         24   24    1    1", Deck(utcHour: 0), StringComparison.Ordinal);

    [Fact]
    public void A_low_power_and_a_transmitter_south_west()
    {
        var deck = Deck(powerW: 5, tx: new GeoPoint(-5.5, -70.25));

        Assert.Contains($"     0.000[{"hq/x",-21}]  0.0    0.0050", deck, StringComparison.Ordinal);
        Assert.Contains("CIRCUIT    5.50S    70.25W", deck, StringComparison.Ordinal);
    }

    [Fact]
    public void Noise_minimum_angle_and_required_snr_go_in_the_system_card() =>
        Assert.Contains("SYSTEM       1. 140. 5.00  90. 30.0 3.00 0.10", Deck(settings: new ModelSettings(30, -140, 5, 30)), StringComparison.Ordinal);

    [Fact]
    public void The_160_m_band_has_its_frequency() =>
        Assert.Contains("FREQUENCY  1.83 0.00", Deck(mhz: 1.83), StringComparison.Ordinal);

    private static string Deck(int utcHour = 14, double powerW = 100, GeoPoint? tx = null, ModelSettings? settings = null, double mhz = 14.045) =>
        VoacapDeck.Build(new DeckSpec(2026, 11, utcHour, mhz, settings ?? Settings, tx ?? new GeoPoint(45.6, 9.2), "hq/x", powerW, "default/isotrope",
            [new DeckCircuit(new GeoPoint(49.98, 14.46), 118)]));
}
