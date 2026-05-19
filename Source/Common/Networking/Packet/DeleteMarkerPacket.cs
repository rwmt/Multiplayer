using System;

namespace Multiplayer.Common.Networking.Packet;

// playerId / factionId / username stamped by the server, not trusted from the client.
[PacketDefinition(Packets.Server_DeleteMarker)]
public record struct ServerDeleteMarkerPacket(int playerId, int factionId, string username, bool senderIsHost, ClientDeleteMarkerPacket data) : IPacket
{
    public int playerId = playerId;
    public int factionId = factionId;
    public string username = username;
    public bool senderIsHost = senderIsHost;
    public ClientDeleteMarkerPacket data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref factionId);
        buf.Bind(ref username, maxLength: MultiplayerServer.MaxUsernameLength);
        buf.Bind(ref senderIsHost);
        buf.Bind(ref data);
    }
}

[PacketDefinition(Packets.Client_DeleteMarker)]
public record struct ClientDeleteMarkerPacket(int[] markerIds) : IPacket
{
    public const int MaxBatchSize = 256;

    public int[] markerIds = markerIds;

    public void Bind(PacketBuffer buf)
    {
        markerIds ??= Array.Empty<int>();
        // Cap reader allocation against malformed input.
        buf.Bind(ref markerIds, BinderOf.Int(), maxLength: MaxBatchSize);
    }
}
