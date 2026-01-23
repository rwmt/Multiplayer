namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Debug)]
public record struct ServerDebugPacket : IPacket
{
    public void Bind(PacketBuffer buf)
    {
    }
}

[PacketDefinition(Packets.Client_Debug)]
public record struct ClientDebugPacket : IPacket
{
    public void Bind(PacketBuffer buf)
    {
    }
}
