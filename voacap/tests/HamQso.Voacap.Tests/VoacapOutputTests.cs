using HamQso.Voacap.Core;

namespace HamQso.Voacap.Tests;

/// <summary>The output of voacapl (method 30), recorded from voacapl 0.7.6: three circuits, one with no path at the MUF.</summary>
public class VoacapOutputTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void One_result_per_circuit_for_the_frequency_asked()
    {
        var results = VoacapOutput.Parse(Fixture("voacapl_3_circuits.out"));

        Assert.Equal(
            [
                new CircuitResult(0.97, 64, 14.4, "1F2"),
                new CircuitResult(0.39, 22, 37.6, "3F2"),
                new CircuitResult(0.93, 54, 11.8, "1F2"),
            ],
            results);
    }

    [Fact]
    public void A_finished_run_says_so_a_failed_one_does_not()
    {
        Assert.True(VoacapOutput.Completed(Fixture("voacapl_3_circuits.out")));
        Assert.False(VoacapOutput.Completed(""));
    }

    [Fact]
    public void Columns_are_read_by_position_a_mode_with_a_space_and_no_value_included()
    {
        const string page = """
                 CCIR Coefficients        ~METHOD 30   VOACAP L 16.1207W  PAGE   1
              24.0  4.8 14.1  0.0  0.0  0.0  0.0  0.0  0.0  0.0  0.0  0.0  0.0 FREQ
                    1F2  1 E   -    -    -    -    -    -    -    -    -    -  MODE  
                     42 -999   -    -    -    -    -    -    -    -    -    -  SNR   
                   0.81 0.01   -    -    -    -    -    -    -    -    -    -  REL   
             *****END OF RUN*****     VOACAP 16.1207W
            """;

        Assert.Equal(new CircuitResult(0.01, null, 4.8, "1E"), Assert.Single(VoacapOutput.Parse(page)));
    }

    [Fact]
    public void The_message_of_a_failed_run_is_kept_short_and_on_one_line()
    {
        var message = VoacapOutput.ErrorMessage(Fixture("voacapl_missing_antenna.stdout"));

        Assert.StartsWith("PAUSE error in readant", message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', message);
        Assert.True(message.Length <= 300);
    }
}
