using HamQso.Voacap.Core;

namespace HamQso.Voacap.Tests;

/// <summary>The antenna models of the service as voacapl antenna files (ITSA-1 types of the voacapl samples).</summary>
public class AntennaFilesTests
{
    [Fact]
    public void An_isotropic_antenna_without_gain_is_the_one_of_voacapl()
    {
        var file = AntennaFiles.For(new AntennaSpec("isotropic", 0, null), 14.045);

        Assert.Equal(("default/isotrope", (string?)null), (file.Name, file.Content));
    }

    [Fact]
    public void A_dipole_is_a_horizontal_half_wave_dipole_at_its_height_10_m_when_not_given()
    {
        var file = AntennaFiles.For(new AntennaSpec("dipole", null, null), 14.045);

        Assert.Equal(
            """
            HamQSO dipole        :ITSA-1 Horizontal Dipole
             8     8 parameters
              0.00  [ 1] Max Gain dBi..:
              23    [ 2] Antenna Type..:
               4    [ 3] Dielectric....:
            .00100  [ 4] Conductivity..:
            14.045  [ 5] Operating Freq:
             -0.50  [ 6] Antenna Length:
             10.00  [ 7] Antenna Height:
              0.00  [ 8] Gain ab dipole:

            """.ReplaceLineEndings("\n"),
            file.Content);
    }

    [Fact]
    public void A_yagi_has_its_gain_over_a_dipole_and_its_height()
    {
        var content = AntennaFiles.For(new AntennaSpec("yagi", 8.5, 12), 14.045).Content!;

        Assert.Contains("  24    [ 2] Antenna Type..:", content, StringComparison.Ordinal);
        Assert.Contains(" 12.00  [ 7] Antenna Height:", content, StringComparison.Ordinal);
        Assert.Contains("  6.35  [ 8] Gain ab dipole:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vertical_is_a_quarter_wave_monopole_on_the_ground()
    {
        var content = AntennaFiles.For(new AntennaSpec("vertical", null, 8), 7.03).Content!;

        Assert.Contains("  22    [ 2] Antenna Type..:", content, StringComparison.Ordinal);
        Assert.Contains(" -0.25  [ 6] Antenna Height:", content, StringComparison.Ordinal);
        Assert.Contains("  0.00  [ 7] Gain ab dipole:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loop_is_a_dipole_pattern_with_its_gain_1_dB_over_a_dipole_when_not_given()
    {
        var content = AntennaFiles.For(new AntennaSpec("loop", null, null), 14.045).Content!;

        Assert.Contains("  23    [ 2] Antenna Type..:", content, StringComparison.Ordinal);
        Assert.Contains("  1.00  [ 8] Gain ab dipole:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void An_isotropic_antenna_with_gain_is_a_flat_gain_table()
    {
        var content = AntennaFiles.For(new AntennaSpec("isotropic", 3, null), 14.045).Content!;

        Assert.StartsWith("HamQSO isotropic", content, StringComparison.Ordinal);
        Assert.Contains("  3.00  [ 1] Max Gain dBi..:", content, StringComparison.Ordinal);
        Assert.Contains("  11    [ 2] Antenna Type..:", content, StringComparison.Ordinal);
        Assert.Equal(91, content.Split('\n').Skip(5).SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Count());
    }

    [Fact]
    public void The_name_fits_the_21_characters_of_the_deck_and_depends_on_the_content_only()
    {
        var a = AntennaFiles.For(new AntennaSpec("dipole", null, 10), 14.045);
        var b = AntennaFiles.For(new AntennaSpec("dipole", null, null), 14.045);
        var c = AntennaFiles.For(new AntennaSpec("dipole", null, 11), 14.045);

        Assert.Equal(a.Name, b.Name);
        Assert.NotEqual(a.Name, c.Name);
        Assert.StartsWith("hq/", a.Name, StringComparison.Ordinal);
        Assert.True(a.Name.Length <= 21);
    }

    [Theory]
    [InlineData("isotropic")]
    [InlineData("dipole")]
    [InlineData("vertical")]
    [InlineData("yagi")]
    [InlineData("loop")]
    public void Every_model_of_the_contract_is_known(string model) => Assert.True(AntennaFiles.IsKnown(model));

    [Fact]
    public void Another_model_is_not_known() => Assert.False(AntennaFiles.IsKnown("hexBeam"));
}
