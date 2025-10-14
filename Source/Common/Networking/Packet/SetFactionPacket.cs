namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_SetFaction)]
public record struct ServerSetFactionPacket(int playerId, int factionId) : IPacket
{
    public int playerId = playerId;
    public int factionId = factionId;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref factionId);
    }
}

[PacketDefinition(Packets.Client_SetFaction)]
public record struct ClientSetFactionPacket(int playerId, int factionId) : IPacket
{
    public int playerId = playerId;
    public int factionId = factionId;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref factionId);
    }
}
