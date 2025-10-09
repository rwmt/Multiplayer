using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace Multiplayer.Common.Networking.Packet;

public enum PlayerListAction : byte
{
    /// Replaces all existing players with new ones
    List,
    /// Adds new players
    Add,
    /// Removes existing players
    Remove,
    /// Updates latencies (and some other related data) of players
    Latencies,
    /// Update a player's status
    Status
}

[PacketDefinition(Packets.Server_PlayerList)]
public record struct ServerPlayerListPacket : IPacket
{
    public PlayerListAction action;

    /// Available when action is: Remove, Status
    public int playerId;

    /// Available when action is: List, Add
    public PlayerInfo[] players;

    /// Available when action is: Latencies
    public PlayerLatency[] latencies;
    /// Available when action is: Status
    public PlayerStatus status;

    public static ServerPlayerListPacket List(IEnumerable<PlayerInfo> players) =>
        new() { action = PlayerListAction.List, players = players.ToArray() };

    public static ServerPlayerListPacket Add(PlayerInfo info) =>
        new() { action = PlayerListAction.Add, players = [info] };

    public static ServerPlayerListPacket Remove(int playerId) =>
        new() { action = PlayerListAction.Remove, playerId = playerId };

    public static ServerPlayerListPacket Latencies(IEnumerable<PlayerLatency> latencies) =>
        new() { action = PlayerListAction.Latencies, latencies = latencies.ToArray() };

    public static ServerPlayerListPacket Status(int playerId, PlayerStatus status) =>
        new() { action = PlayerListAction.Status, playerId = playerId, status = status };


    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref action);
        if (action == PlayerListAction.Add)
        {
            buf.Bind(ref players, BinderOf.Identity<PlayerInfo>());
        }
        else if (action == PlayerListAction.Remove)
        {
            buf.Bind(ref playerId);
        }
        else if (action == PlayerListAction.Latencies)
        {
            buf.Bind(ref latencies, BinderOf.Identity<PlayerLatency>());
        }
        else if (action == PlayerListAction.Status)
        {
            buf.Bind(ref playerId);
            buf.BindEnum(ref status);
        }
        else if (action == PlayerListAction.List)
        {
            buf.Bind(ref players, BinderOf.Identity<PlayerInfo>());
        }
    }

    public record struct PlayerInfo : IPacketBufferable
    {
        public int id;
        public string username;
        public int latency;
        public PlayerType type;
        public PlayerStatus status;
        public ulong steamId;
        public string steamPersonaName;
        public int ticksBehind;
        public bool simulating;
        public byte r, g, b;
        public int factionId;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref id);
            buf.Bind(ref username, maxLength: MultiplayerServer.MaxUsernameLength);
            buf.Bind(ref latency);
            buf.BindEnum(ref type);
            buf.BindEnum(ref status);
            buf.Bind(ref steamId);
            buf.Bind(ref steamPersonaName, maxLength: Constants.k_cwchPersonaNameMax);
            buf.Bind(ref ticksBehind);
            buf.Bind(ref simulating);
            buf.Bind(ref r);
            buf.Bind(ref g);
            buf.Bind(ref b);
            buf.Bind(ref factionId);
        }
    }

    public record struct PlayerLatency : IPacketBufferable
    {
        public int playerId;
        public int latency;
        public int ticksBehind;
        public bool simulating;
        public float frameTime;

        public void Bind(PacketBuffer buf)
        {
            buf.Bind(ref playerId);
            buf.Bind(ref latency);
            buf.Bind(ref ticksBehind);
            buf.Bind(ref simulating);
            buf.Bind(ref frameTime);
        }
    }
}
