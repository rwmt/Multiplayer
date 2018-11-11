using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Multiplayer.Common
{
    public class ServerJoiningState : MpConnectionState
    {
        private static Regex UsernamePattern = new Regex(@"^[a-zA-Z0-9_]+$");

        public ServerJoiningState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_Username)]
        public void HandleClientUsername(ByteReader data)
        {
            string username = data.ReadString();

            if (username.Length < 3 || username.Length > 15)
            {
                Player.Disconnect("MpInvalidUsernameLength");
                return;
            }

            if (!UsernamePattern.IsMatch(username))
            {
                Player.Disconnect("MpInvalidUsernameChars");
                return;
            }

            if (MultiplayerServer.instance.GetPlayer(username) != null)
            {
                Player.Disconnect("MpInvalidUsernameAlreadyPlaying");
                return;
            }

            connection.username = username;

            Server.SendNotification("MpPlayerConnected", connection.username);
            Server.SendToAll(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Add, Player.id, Player.Username, Player.Latency });
        }

        [PacketHandler(Packets.Client_RequestWorld)]
        public void HandleWorldRequest(ByteReader data)
        {
            int factionId = MultiplayerServer.instance.coopFactionId;
            MultiplayerServer.instance.playerFactions[connection.username] = factionId;

            /*if (!MultiplayerServer.instance.playerFactions.TryGetValue(connection.Username, out int factionId))
            {
                factionId = MultiplayerServer.instance.nextUniqueId++;
                MultiplayerServer.instance.playerFactions[connection.Username] = factionId;

                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.SETUP_FACTION, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }*/

            if (MultiplayerServer.instance.players.Count(p => p.FactionId == factionId) == 1)
            {
                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.FactionOnline, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }

            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(factionId);
            writer.WriteInt32(MultiplayerServer.instance.timer);
            writer.WritePrefixedBytes(MultiplayerServer.instance.savedGame);

            writer.WriteInt32(MultiplayerServer.instance.mapCmds.Count);

            foreach (var kv in MultiplayerServer.instance.mapCmds)
            {
                int mapId = kv.Key;

                //MultiplayerServer.instance.SendCommand(CommandType.CreateMapFactionData, ScheduledCommand.NoFaction, mapId, ByteWriter.GetBytes(factionId));

                List<byte[]> mapCmds = kv.Value;

                writer.WriteInt32(mapId);
                writer.WriteByteArrayList(mapCmds);
            }

            writer.WriteInt32(MultiplayerServer.instance.mapData.Count);

            foreach (var kv in MultiplayerServer.instance.mapData)
            {
                int mapId = kv.Key;
                byte[] mapData = kv.Value;

                writer.WriteInt32(mapId);
                writer.WritePrefixedBytes(mapData);
            }

            connection.State = ConnectionStateEnum.ServerPlaying;

            byte[] packetData = writer.GetArray();
            connection.SendFragmented(Packets.Server_WorldData, packetData);

            Player.SendPlayerList();

            MpLog.Log("World response sent: " + packetData.Length);
        }
    }

    public class ServerPlayingState : MpConnectionState
    {
        public ServerPlayingState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_WorldLoaded)]
        public void HandleWorldLoaded(ByteReader data)
        {
        }

        [PacketHandler(Packets.Client_Command)]
        public void HandleClientCommand(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extra = data.ReadPrefixedBytes(32767);

            // todo check if map id is valid for the player

            int factionId = MultiplayerServer.instance.playerFactions[connection.username];
            MultiplayerServer.instance.SendCommand(cmd, factionId, mapId, extra, connection.username);
        }

        public const int MaxChatMsgLength = 128;

        [PacketHandler(Packets.Client_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            msg = msg.Trim();

            // todo handle max length
            if (msg.Length == 0) return;

            if (msg[0] == '/')
            {
                var cmd = msg.Substring(1);
                var parts = cmd.Split(' ');
                var handler = MultiplayerServer.instance.GetCmdHandler(parts[0]);

                if (handler != null)
                    handler.Handle(Player, parts.SubArray(1));
                else
                    Player.SendChat("Invalid command");
            }
            else
            {
                Player.SendChat($"{connection.username}: {msg}");
            }
        }

        [PacketHandler(Packets.Client_AutosavedData)]
        [IsFragmented]
        public void HandleAutosavedData(ByteReader data)
        {
            if (Player.Username != Server.hostUsername) return;

            int type = data.ReadInt32();
            byte[] compressedData = data.ReadPrefixedBytes();

            if (type == 0) // World data
            {
                int mapId = data.ReadInt32();

                // todo test map ownership
                Server.mapData[mapId] = compressedData;
            }
            else if (type == 1) // Map data
            {
                Server.savedGame = compressedData;

                if (Server.tmpMapCmds != null)
                {
                    Server.mapCmds = Server.tmpMapCmds;
                    Server.tmpMapCmds = null;
                }
            }
        }

        [PacketHandler(Packets.Client_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            var writer = new ByteWriter();

            byte seq = data.ReadByte();
            byte map = data.ReadByte();

            writer.WriteInt32(Player.id);
            writer.WriteByte(seq);
            writer.WriteByte(map);

            if (map < byte.MaxValue)
            {
                byte icon = data.ReadByte();
                short x = data.ReadShort();
                short z = data.ReadShort();

                writer.WriteByte(icon);
                writer.WriteShort(x);
                writer.WriteShort(z);
            }

            Server.SendToAll(Packets.Server_Cursor, writer.GetArray());
        }

        [PacketHandler(Packets.Client_IdBlockRequest)]
        public void HandleIdBlockRequest(ByteReader data)
        {
            int mapId = data.ReadInt32();

            if (mapId == ScheduledCommand.Global)
            {
                //IdBlock nextBlock = MultiplayerServer.instance.NextIdBlock();
                //MultiplayerServer.instance.SendCommand(CommandType.GlobalIdBlock, ScheduledCommand.NoFaction, ScheduledCommand.Global, nextBlock.Serialize());
            }
            else
            {
                // todo
            }
        }

        [PacketHandler(Packets.Client_KeepAlive)]
        public void HandleClientKeepAlive(ByteReader data)
        {
            // Ping already handled by LiteNetLib
            if (connection is MpNetConnection) return;

            int id = data.ReadInt32();
            if (MultiplayerServer.instance.keepAliveId == id)
                connection.Latency = (int)MultiplayerServer.instance.lastKeepAlive.ElapsedMilliseconds / 2;
            else
                connection.Latency = 2000;
        }

        [PacketHandler(Packets.Client_Debug)]
        public void HandleDebug(ByteReader data)
        {
            if (Player.Username != Server.hostUsername) return;

            int tick = data.ReadInt32();

        }
    }

    public enum PlayerListAction : byte
    {
        List,
        Add,
        Remove,
        Latencies
    }

    public class ServerSteamState : MpConnectionState
    {
        public ServerSteamState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_SteamRequest)]
        public void HandleSteamRequest(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ServerJoining;
            connection.Send(Packets.Server_SteamAccept);
        }
    }
}
