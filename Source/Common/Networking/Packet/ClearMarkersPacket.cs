using System;

namespace Multiplayer.Common.Networking.Packet;

public enum PingMarkerClearMode : byte
{
    /// Sender's markers, all maps.
    Mine = 0,
    /// Sender's markers on one map.
    OnMap = 1,
    /// Every marker placed by a named user, all maps. Any player can do this to deal with griefers.
    FromPlayer = 2,
    /// Host-only: every marker, every player.
    AllMarkers = 3,
    /// Host-only: every ephemeral ping currently in flight.
    AllPings = 4,
}

public static class PingMarkerClearWire
{
    public static readonly int Count = Enum.GetValues(typeof(PingMarkerClearMode)).Length;
    public static bool IsValid(byte raw) => raw < Count;
}

// playerId / username stamped by the server, not trusted from the client (PlayerInfo may evict mid-relay).
[PacketDefinition(Packets.Server_ClearMarkers)]
public record struct ServerClearMarkersPacket(int playerId, string username, bool senderIsHost, ClientClearMarkersPacket data) : IPacket
{
    public int playerId = playerId;
    public string username = username;
    public bool senderIsHost = senderIsHost;
    public ClientClearMarkersPacket data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref username, maxLength: MultiplayerServer.MaxUsernameLength);
        buf.Bind(ref senderIsHost);
        buf.Bind(ref data);
    }
}

[PacketDefinition(Packets.Client_ClearMarkers)]
public record struct ClientClearMarkersPacket(byte mode, int mapId, string targetUsername) : IPacket
{
    public byte mode = mode;
    public int mapId = mapId;
    // FromPlayer only; empty otherwise.
    public string targetUsername = targetUsername;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref mode);
        buf.Bind(ref mapId);
        buf.Bind(ref targetUsername, maxLength: MultiplayerServer.MaxUsernameLength);
    }
}
