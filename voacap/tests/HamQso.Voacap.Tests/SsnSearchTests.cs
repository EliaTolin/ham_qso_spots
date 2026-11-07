using HamQso.Voacap.Core;

namespace HamQso.Voacap.Tests;

public class SsnSearchTests
{
    [Fact]
    public void The_coarse_grid_is_every_10_from_0_to_300() =>
        Assert.Equal([0, 10, 20, 290, 300], SsnSearch.Coarse().Where(s => s < 30 || s > 280));

    [Fact]
    public void The_fine_grid_is_every_1_around_the_best_coarse_value_within_0_and_300()
    {
        Assert.Equal(Enumerable.Range(120, 21), SsnSearch.Fine(130));
        Assert.Equal(Enumerable.Range(0, 11), SsnSearch.Fine(0));
        Assert.Equal(Enumerable.Range(290, 11), SsnSearch.Fine(300));
    }

    [Fact]
    public void The_best_is_the_ssn_whose_muf_is_nearest_the_measured_one()
    {
        (int, double)[] mufs = [(120, 16.8), (130, 17.6), (140, 18.4)];

        Assert.Equal((140, 18.4), SsnSearch.Best(mufs, 18.2, 118));
    }

    [Fact]
    public void On_a_tie_the_one_nearest_the_monthly_ssn()
    {
        (int, double)[] mufs = [(100, 18.0), (110, 18.0), (120, 18.0)];

        Assert.Equal((120, 18.0), SsnSearch.Best(mufs, 18.0, 125));
    }

    [Fact]
    public void A_measured_muf_above_every_prediction_gives_the_highest_ssn()
    {
        (int, double)[] mufs = [(0, 9.0), (300, 25.0)];

        Assert.Equal((300, 25.0), SsnSearch.Best(mufs, 40, 118));
    }

    [Fact]
    public void The_circuit_is_3000_km_east_west_centred_on_the_ionosonde()
    {
        var (west, east) = SsnSearch.Circuit(new GeoPoint(41.9, 12.5));

        Assert.Equal(3000, GreatCircle.DistanceKm(west, east), 0);
        Assert.Equal(1500, GreatCircle.DistanceKm(new GeoPoint(41.9, 12.5), east), 0);
        Assert.True(east.Lon > 12.5 && west.Lon < 12.5);
    }
}
