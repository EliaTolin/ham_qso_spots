using System.Text;
using HamQso.Spots.Infrastructure.Sources.Telnet;

namespace HamQso.Spots.Infrastructure.Tests.Sources.Telnet;

/// <summary>RFC 854 negotiation: every option refused, only the text goes through.</summary>
public class TelnetDecoderTests
{
    private const byte Iac = 255, Will = 251, Wont = 252, Do = 253, Dont = 254, Sb = 250, Se = 240, Echo = 1, NoOp = 241;

    private static (string Text, byte[] Replies) Decode(TelnetDecoder decoder, params byte[] input)
    {
        var replies = new List<byte>();
        var data = decoder.Decode(input, replies);
        return (Encoding.Latin1.GetString(data), [.. replies]);
    }

    [Fact]
    public void Do_is_answered_wont_and_will_is_answered_dont()
    {
        var (text, replies) = Decode(new TelnetDecoder(), Iac, Do, Echo, Iac, Will, Echo, (byte)'O', (byte)'K');

        Assert.Equal("OK", text);
        Assert.Equal(new byte[] { Iac, Wont, Echo, Iac, Dont, Echo }, replies);
    }

    [Fact]
    public void Wont_and_dont_from_the_node_need_no_answer()
    {
        var (text, replies) = Decode(new TelnetDecoder(), Iac, Wont, Echo, Iac, Dont, Echo, (byte)'x');

        Assert.Equal(("x", 0), (text, replies.Length));
    }

    [Fact]
    public void A_doubled_255_is_one_data_byte()
    {
        var (text, _) = Decode(new TelnetDecoder(), (byte)'a', Iac, Iac, (byte)'b');

        Assert.Equal("aÿb", text);
    }

    [Fact]
    public void A_subnegotiation_is_skipped_whole_also_with_255_inside()
    {
        var (text, replies) = Decode(new TelnetDecoder(), (byte)'a', Iac, Sb, 24, 1, Iac, Iac, 7, Iac, Se, (byte)'b');

        Assert.Equal(("ab", 0), (text, replies.Length));
    }

    [Fact]
    public void Other_commands_are_dropped()
    {
        var (text, _) = Decode(new TelnetDecoder(), (byte)'a', Iac, NoOp, (byte)'b');

        Assert.Equal("ab", text);
    }

    [Fact]
    public void A_command_split_between_two_reads_is_still_understood()
    {
        var decoder = new TelnetDecoder();

        var (first, firstReplies) = Decode(decoder, (byte)'a', Iac);
        var (second, secondReplies) = Decode(decoder, Do, Echo, (byte)'b');

        Assert.Equal(("a", "b"), (first, second));
        Assert.Empty(firstReplies);
        Assert.Equal(new byte[] { Iac, Wont, Echo }, secondReplies);
    }
}
