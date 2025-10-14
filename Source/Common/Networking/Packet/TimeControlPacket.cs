namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_TimeControl)]
public record struct ServerTimeControlPacket(int tickUntil, int sentCmds, float serverTimePerTick) : IPacket
{
    public int tickUntil = tickUntil;
    public int sentCmds = sentCmds;
    public float serverTimePerTick = serverTimePerTick;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref tickUntil);
        buf.Bind(ref sentCmds);
        buf.Bind(ref serverTimePerTick);
    }
}
