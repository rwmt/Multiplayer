namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_KeepAlive)]
public record struct ServerKeepAlivePacket(int id) : IPacket
{
    public int id = id;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref id);
    }
}

[PacketDefinition(Packets.Client_KeepAlive)]
public record struct ClientKeepAlivePacket(int id, int ticksBehind, bool simulating, int workTicks) : IPacket
{
    public int id = id;
    public int ticksBehind = ticksBehind;
    public bool simulating = simulating;
    public int workTicks = workTicks;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref id);
        buf.Bind(ref ticksBehind);
        buf.Bind(ref simulating);
        buf.Bind(ref workTicks);
    }
}
