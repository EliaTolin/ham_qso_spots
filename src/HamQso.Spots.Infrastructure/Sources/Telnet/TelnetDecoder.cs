namespace HamQso.Spots.Infrastructure.Sources.Telnet;

/// <summary>
/// Strips telnet negotiation (RFC 854) from the bytes of a node and refuses
/// every option (DO is answered WONT, WILL is answered DONT), keeping the line
/// in plain NVT mode. Same state machine as the app's telnet datasource.
/// </summary>
public sealed class TelnetDecoder
{
    private const byte Iac = 255;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;

    private State state = State.Data;
    private byte pendingCommand;

    private enum State
    {
        Data,
        AfterIac,
        Option,
        Subnegotiation,
        IacInSubnegotiation,
    }

    /// <summary>The data bytes of <paramref name="input"/>; refusals to send back go in <paramref name="replies"/>.</summary>
    public byte[] Decode(ReadOnlySpan<byte> input, List<byte> replies)
    {
        ArgumentNullException.ThrowIfNull(replies);
        var data = new List<byte>(input.Length);
        foreach (var b in input)
        {
            switch (state)
            {
                case State.Data:
                    if (b == Iac) state = State.AfterIac;
                    else data.Add(b);
                    break;
                case State.AfterIac:
                    if (b == Iac)
                    {
                        data.Add(b);
                        state = State.Data;
                    }
                    else if (b == Sb)
                    {
                        state = State.Subnegotiation;
                    }
                    else if (b is >= Will and <= Dont)
                    {
                        pendingCommand = b;
                        state = State.Option;
                    }
                    else
                    {
                        state = State.Data;
                    }
                    break;
                case State.Option:
                    if (pendingCommand == Do) replies.AddRange([Iac, Wont, b]);
                    if (pendingCommand == Will) replies.AddRange([Iac, Dont, b]);
                    state = State.Data;
                    break;
                case State.Subnegotiation:
                    if (b == Iac) state = State.IacInSubnegotiation;
                    break;
                case State.IacInSubnegotiation:
                    state = b == Se ? State.Data : State.Subnegotiation;
                    break;
            }
        }
        return [.. data];
    }
}
