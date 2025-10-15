namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_InitDataRequest)]
public record struct ServerInitDataRequestPacket(bool includeConfigs) : IPacket
{
    public bool includeConfigs = includeConfigs;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref includeConfigs);
    }
}
