using System;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common
{
    public class ServerPlayer : IChatSource
    {
        public int id;
        public ConnectionBase conn;
        public PlayerType type = PlayerType.Normal;
        public PlayerStatus status = PlayerStatus.Simulating;
        public ColorRGB color;
        public bool hasJoined;
        public bool simulating;
        public float frameTime;

        public int ticksBehind;
        public int ticksBehindReceivedAt;
        public int ExtrapolatedTicksBehind => ticksBehind + (Server.gameTimer - ticksBehindReceivedAt);

        public ulong steamId;
        public string steamPersonaName = "";

        public int lastCursorTick = -1;

        public int keepAliveId;
        public int keepAliveAt;

        public bool frozen;
        public int unfrozenAt;

        // Track which map the player is currently on
        public int currentMapId = -1;

        public string Username => conn.username;
        public int Latency => conn.Latency;
        public int FactionId { get; set; }
        public bool HasJoined => conn.State is ConnectionStateEnum.ServerLoading or ConnectionStateEnum.ServerPlaying;
        public bool IsPlaying => conn.State == ConnectionStateEnum.ServerPlaying;
        public bool IsHost => Server.hostUsername == Username;
        public bool IsArbiter => type == PlayerType.Arbiter;

        public MultiplayerServer Server => MultiplayerServer.instance!;

        public ServerPlayer(int id, ConnectionBase connection)
        {
            this.id = id;
            conn = connection;
        }

        public void HandleReceive(ByteReader data, bool reliable)
        {
            try
            {
                conn.HandleReceiveRaw(data, reliable);
            }
            catch (Exception e)
            {
                ServerLog.Error($"Error handling packet by {conn}: {e}");
                Disconnect(MpDisconnectReason.ServerPacketRead);
            }
        }

        public void Disconnect(string reasonKey)
        {
            Disconnect(MpDisconnectReason.GenericKeyed, ByteWriter.GetBytes(reasonKey));
        }

        public void Disconnect(MpDisconnectReason reason, byte[]? data = null)
        {
            conn.Close(reason, data);
            Server.playerManager.SetDisconnected(conn, reason);
        }

        public void SendKeepAlivePacket() =>
            conn.Send(new ServerKeepAlivePacket(keepAliveId), false);

        public void SendPacket(Packets packet, byte[] data, bool reliable = true)
        {
            conn.Send(packet, data, reliable);
        }

        public void SendPacket(Packets packet, object[] data)
        {
            conn.Send(packet, data);
        }

        public void SendPlayerList() =>
            conn.Send(ServerPlayerListPacket.List(Server.JoinedPlayers.Select(p => p.PlayerInfoPacket())));

        public ServerPlayerListPacket.PlayerInfo PlayerInfoPacket() => new()
        {
            id = id,
            username = Username ?? "",
            latency = Latency,
            type = type,

            status = status,

            steamId = steamId,
            steamPersonaName = steamPersonaName,

            ticksBehind = ticksBehind,
            simulating = simulating,

            r = color.r,
            g = color.g,
            b = color.b,

            factionId = FactionId,
        };

        public ServerPlayerListPacket.PlayerLatency LatencyPacket() => new()
        {
            playerId = id, latency = Latency, ticksBehind = ticksBehind, simulating = simulating,frameTime = frameTime
        };

        public void UpdateStatus(PlayerStatus newStatus)
        {
            if (status == newStatus) return;
            status = newStatus;
            Server.SendToPlaying(ServerPlayerListPacket.Status(id, newStatus));
        }

        public void ResetTimeVotes()
        {
            Server.commands.Send(
                CommandType.TimeSpeedVote,
                ScheduledCommand.NoFaction,
                ScheduledCommand.Global,
                ByteWriter.GetBytes(TimeVote.PlayerResetGlobal, -1),
                fauxSource: this
            );
        }

        public void SendMsg(string msg)
        {
            SendPacket(Packets.Server_Chat, new object[] { msg });
        }
    }

    public enum PlayerStatus : byte
    {
        Simulating,
        Playing,
        Desynced
    }

    public enum PlayerType : byte
    {
        Normal,
        Steam,
        Arbiter
    }
}
