using System;
using System.Diagnostics;
using System.Linq;

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
        public Stopwatch keepAliveTimer = Stopwatch.StartNew();
        public int keepAliveAt;

        public bool frozen;
        public int unfrozenAt;

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

        public void SendChat(string msg)
        {
            SendPacket(Packets.Server_Chat, new object[] { msg });
        }

        public void SendPacket(Packets packet, byte[] data, bool reliable = true)
        {
            conn.Send(packet, data, reliable);
        }

        public void SendPacket(Packets packet, object[] data)
        {
            conn.Send(packet, data);
        }

        public void SendPlayerList()
        {
            var writer = new ByteWriter();

            writer.WriteByte((byte)PlayerListAction.List);
            writer.WriteInt32(Server.JoinedPlayers.Count());

            foreach (var player in Server.JoinedPlayers)
                writer.WriteRaw(player.SerializePlayerInfo());

            conn.Send(Packets.Server_PlayerList, writer.ToArray());
        }

        public byte[] SerializePlayerInfo()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(id);
            writer.WriteString(Username);
            writer.WriteInt32(Latency);
            writer.WriteByte((byte)type);
            writer.WriteByte((byte)status);
            writer.WriteULong(steamId);
            writer.WriteString(steamPersonaName);
            writer.WriteInt32(ticksBehind);
            writer.WriteBool(simulating);
            writer.WriteByte(color.r);
            writer.WriteByte(color.g);
            writer.WriteByte(color.b);
            writer.WriteInt32(FactionId);

            return writer.ToArray();
        }

        public void WriteLatencyUpdate(ByteWriter writer)
        {
            writer.WriteInt32(Latency);
            writer.WriteInt32(ticksBehind);
            writer.WriteBool(simulating);
            writer.WriteFloat(frameTime);
        }

        public void UpdateStatus(PlayerStatus newStatus)
        {
            if (status == newStatus) return;
            status = newStatus;
            Server.SendToPlaying(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Status, id, (byte)newStatus });
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
            SendChat(msg);
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
