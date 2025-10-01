namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_PingLocation)]
public record struct ServerPingLocPacket(int playerId, ClientPingLocPacket data) : IPacket
{
    public int playerId = playerId;
    public ClientPingLocPacket data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref data);
    }
}

[PacketDefinition(Packets.Client_PingLocation)]
public record struct ClientPingLocPacket(int mapId, int planetTileId, int planetTileLayer, float x, float y, float z) : IPacket
{
    public int mapId = mapId;
    public int planetTileId = planetTileId;
    public int planetTileLayer = planetTileLayer;
    public float x = x, y = y, z = z;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref mapId);
        buf.Bind(ref planetTileId);
        buf.Bind(ref planetTileLayer);
        buf.Bind(ref x);
        buf.Bind(ref y);
        buf.Bind(ref z);
    }
}
