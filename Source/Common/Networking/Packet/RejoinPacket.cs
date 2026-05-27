namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_RequestRejoin)]
public record struct ServerRequestRejoinPacket : IPacket
{
    public void Bind(PacketBuffer buf)
    {
    }
}
