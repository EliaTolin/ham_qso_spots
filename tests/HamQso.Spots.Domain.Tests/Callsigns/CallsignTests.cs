using System.Text.Json;
using HamQso.Spots.Domain.Callsigns;

namespace HamQso.Spots.Domain.Tests.Callsigns;

public class CallsignTests
{
    /// <summary>Copy of the shared contract (app and backend): keep identical.</summary>
    public static TheoryData<string, string> SharedCases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "base_callsign_cases.json")));
        var data = new TheoryData<string, string>();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(c.GetProperty("input").GetString()!, c.GetProperty("expected").GetString()!);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void The_base_callsign_is_the_same_as_in_the_app_and_the_backend(string input, string expected) =>
        Assert.Equal(expected, Callsign.Base(input));

    [Theory]
    [InlineData("IN3FOB", true)]
    [InlineData("ea8/in3fob/p", true)]
    [InlineData("3Y0J", true)]
    [InlineData(" IU4VRB ", true)]
    [InlineData("IU4VRB-7", false)]
    [InlineData("IU4VRB/", false)]
    [InlineData("/IU4VRB", false)]
    [InlineData("ABCDEF", false)]
    [InlineData("12345", false)]
    [InlineData("IU4 VRB", false)]
    [InlineData("IU4VRB\r\n", true)]
    [InlineData("IU4VRB%0A", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRS1", true)]
    [InlineData("ABCDEFGHIJKLMNOPQRST1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_plausible_callsign_has_letters_and_digits_in_slash_separated_parts(string? callsign, bool plausible) =>
        Assert.Equal(plausible, Callsign.IsPlausible(callsign));
}
