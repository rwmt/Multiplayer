namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_Desynced)]
public record struct ClientDesyncedPacket(int tick, int diffAt) : IPacket
{
    public int tick = tick;
    public int diffAt = diffAt;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref tick);
        buf.Bind(ref diffAt);
    }
}
