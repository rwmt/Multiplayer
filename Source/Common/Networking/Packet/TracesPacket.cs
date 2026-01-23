namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_Traces, allowFragmented: true)]
public record struct ClientTracesPacket : IPacket
{
    public int playerId;
    public byte[] rawTraces;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.BindRemaining(ref rawTraces);
    }
}

[PacketDefinition(Packets.Server_Traces, allowFragmented: true)]
public record struct ServerTracesPacket : IPacket
{
    public enum Mode : byte
    {
        Request, Transfer
    }

    public Mode mode;

    public int tick;
    public int diffAt;
    public int playerId;

    // Used in transfer only
    public byte[] rawTraces;

    public static ServerTracesPacket Request(int tick, int diffAt, int playerId) => new()
    {
        mode = Mode.Request,
        tick = tick,
        diffAt = diffAt,
        playerId = playerId
    };

    public static ServerTracesPacket Transfer(byte[] rawTraces) =>
        new() { mode = Mode.Transfer, rawTraces = rawTraces };

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref mode);
        if (mode == Mode.Request)
        {
            buf.Bind(ref tick);
            buf.Bind(ref diffAt);
            buf.Bind(ref playerId);
        }
        else
        {
            buf.BindRemaining(ref rawTraces);
        }
    }
}
