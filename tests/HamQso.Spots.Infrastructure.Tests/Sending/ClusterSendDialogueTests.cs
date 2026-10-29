using HamQso.Spots.Infrastructure.Sending;

namespace HamQso.Spots.Infrastructure.Tests.Sending;

public class ClusterSendDialogueTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    private readonly ClusterSendDialogue dialogue = new("IU4VRB-7", Start);

    [Theory]
    [InlineData("login: ")]
    [InlineData("Please enter your call: ")]
    [InlineData("Callsign:")]
    public void The_login_prompt_is_answered_with_the_user_login(string prompt)
    {
        var (lines, command) = dialogue.OnText("Welcome to FAKE-1\r\n" + prompt, Start);

        Assert.Equal("IU4VRB-7", command);
        Assert.Equal(["Welcome to FAKE-1"], lines);
        Assert.True(dialogue.LoginSent);
    }

    [Fact]
    public void Without_a_prompt_the_login_goes_after_the_fallback()
    {
        Assert.Null(dialogue.OnTick(Start.AddSeconds(3), TimeSpan.FromSeconds(4)));
        Assert.Equal("IU4VRB-7", dialogue.OnTick(Start.AddSeconds(4), TimeSpan.FromSeconds(4)));
        Assert.Null(dialogue.OnTick(Start.AddSeconds(9), TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void A_password_prompt_after_the_login_gets_an_empty_password_once()
    {
        dialogue.OnText("login: ", Start);

        Assert.Equal("", dialogue.OnText("password: ", Start).Command);
        Assert.Null(dialogue.OnText("password: ", Start).Command);
    }

    [Fact]
    public void A_password_prompt_before_the_login_is_not_answered() =>
        Assert.Null(dialogue.OnText("password: ", Start).Command);

    [Fact]
    public void Ready_after_the_login_once_the_node_is_quiet()
    {
        Assert.False(dialogue.IsReady(Start.AddMinutes(1), Silence));
        dialogue.OnText("login: ", Start);
        dialogue.OnText("Hello IU4VRB-7\r\n", Start.AddSeconds(1));

        Assert.False(dialogue.IsReady(Start.AddSeconds(2.5), Silence));
        Assert.True(dialogue.IsReady(Start.AddSeconds(3), Silence));
    }

    [Fact]
    public void Spots_control_characters_and_empty_lines_are_not_replies()
    {
        var (lines, _) = dialogue.OnText(
            "DX de IU4VRB-7:   14025.0  K1ABC        CQ CW                          1200Z\a\a\r\n\r\n\a\r\nDuplicate spot\a\r\nIU4VRB-7 de FAKE-1 >",
            Start);

        Assert.Equal(["Duplicate spot"], lines);
    }
}
