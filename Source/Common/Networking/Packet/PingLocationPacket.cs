namespace Multiplayer.Common.Networking.Packet
{
    // playerId / factionId / username / color stamped by the server, not trusted from the client.
    [PacketDefinition(Packets.Server_PingLocation)]
    public record struct ServerPingLocPacket(int playerId, int factionId, string username, byte r, byte g, byte b, ClientPingLocPacket data) : IPacket
    {
        public int playerId = playerId;
        public int factionId = factionId;
        public string username = username;
        public byte r = r;
        public byte g = g;
        public byte b = b;
        public ClientPingLocPacket data = data;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref playerId);
            buf.Bind(ref factionId);
            buf.Bind(ref username, maxLength: MultiplayerServer.MaxUsernameLength);
            buf.Bind(ref r);
            buf.Bind(ref g);
            buf.Bind(ref b);
            buf.Bind(ref data);
        }
    }

    [PacketDefinition(Packets.Client_PingLocation)]
    public record struct ClientPingLocPacket(
        int mapId,
        int planetTileId,
        int planetTileLayer,
        float x, float y, float z,
        ushort category,
        bool isMarker,
        string label,
        int placedAtTick
    ) : IPacket
    {
        public int mapId = mapId;
        public int planetTileId = planetTileId;
        public int planetTileLayer = planetTileLayer;
        public float x = x, y = y, z = z;
        // Short-hash of the placer's MultiplayerPingDef. Receivers without that def map back to
        // Default (PingCategoryExtensions.ResolveFromWire); zero is the explicit "unknown" sentinel.
        public ushort category = category;
        public bool isMarker = isMarker;
        public string label = label;
        // Stamped by sender; relayed verbatim so receivers agree on "placed at".
        public int placedAtTick = placedAtTick;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref mapId);
            buf.Bind(ref planetTileId);
            buf.Bind(ref planetTileLayer);
            buf.Bind(ref x);
            buf.Bind(ref y);
            buf.Bind(ref z);
            buf.Bind(ref category);
            buf.Bind(ref isMarker);
            buf.Bind(ref label, maxLength: PingCategoryWire.MaxLabelBytes);
            buf.Bind(ref placedAtTick);
        }
    }
}
