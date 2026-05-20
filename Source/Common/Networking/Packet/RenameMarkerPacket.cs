namespace Multiplayer.Common.Networking.Packet
{
    // playerId / factionId / username stamped by the server. Per-marker ownership enforced on the receiver.
    [PacketDefinition(Packets.Server_RenameMarker)]
    public record struct ServerRenameMarkerPacket(int playerId, int factionId, string username, bool senderIsHost, ClientRenameMarkerPacket data) : IPacket
    {
        public int playerId = playerId;
        public int factionId = factionId;
        public string username = username;
        public bool senderIsHost = senderIsHost;
        public ClientRenameMarkerPacket data = data;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref playerId);
            buf.Bind(ref factionId);
            buf.Bind(ref username, maxLength: MultiplayerServer.MaxUsernameLength);
            buf.Bind(ref senderIsHost);
            buf.Bind(ref data);
        }
    }

    [PacketDefinition(Packets.Client_RenameMarker)]
    public record struct ClientRenameMarkerPacket(int markerId, string label) : IPacket
    {
        public int markerId = markerId;
        public string label = label;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref markerId);
            buf.Bind(ref label, maxLength: PingCategoryWire.MaxLabelBytes);
        }
    }
}
