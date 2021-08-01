using System;
using System.Linq;
using System.Text;

namespace Multiplayer.Common
{
    public class ServerPlayer
    {
        public int id;
        public IConnection conn;
        public PlayerType type;
        public PlayerStatus status;
        public ColorRGB color;
        public int ticksBehind;

        public ulong steamId;
        public string steamPersonaName = "";

        public int lastCursorTick = -1;

        public string Username => conn.username;
        public int Latency => conn.Latency;
        public int FactionId => MultiplayerServer.instance.playerFactions[Username];
        public bool IsPlaying => conn.State == ConnectionStateEnum.ServerPlaying;
        public bool IsHost => MultiplayerServer.instance.hostUsername == Username;
        public bool IsArbiter => type == PlayerType.Arbiter;

        public MultiplayerServer Server => MultiplayerServer.instance;

        public ServerPlayer(int id, IConnection connection)
        {
            this.id = id;
            conn = connection;
        }

        public void HandleReceive(ByteReader data, bool reliable)
        {
            try
            {
                conn.HandleReceive(data, reliable);
            }
            catch (Exception e)
            {
                MpLog.Error($"Error handling packet by {conn}: {e}");
                Disconnect($"Receive error: {e.GetType().Name}: {e.Message}");
            }
        }

        public void Disconnect(string reasonKey)
        {
            Disconnect(MpDisconnectReason.GenericKeyed, Encoding.UTF8.GetBytes(reasonKey));
        }

        public void Disconnect(MpDisconnectReason reason, byte[] data = null)
        {
            conn.Close(reason, data);
            Server.OnDisconnected(conn, reason);
        }

        public void SendChat(string msg)
        {
            SendPacket(Packets.Server_Chat, new[] { msg });
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
            writer.WriteInt32(Server.PlayingPlayers.Count());

            foreach (var player in Server.PlayingPlayers)
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
            writer.WriteByte(color.r);
            writer.WriteByte(color.g);
            writer.WriteByte(color.b);

            return writer.ToArray();
        }

        public void UpdateStatus(PlayerStatus status)
        {
            if (this.status == status) return;
            this.status = status;
            Server.SendToAll(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Status, id, (byte)status });
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
