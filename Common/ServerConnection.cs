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
            ServerPlayer player = connection.serverPlayer;

            if (username.Length < 3 || username.Length > 15)
            {
                player.Disconnect("Invalid username length.");
                return;
            }

            if (!UsernamePattern.IsMatch(username))
            {
                player.Disconnect("Invalid username characters.");
                return;
            }

            if (MultiplayerServer.instance.GetPlayer(username) != null)
            {
                player.Disconnect("Username already online.");
                return;
            }

            connection.username = username;

            MultiplayerServer.instance.SendToAll(Packets.Server_Notification, new object[] { "Player " + connection.username + " has joined the game." });
            MultiplayerServer.instance.UpdatePlayerList();
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

            List<byte[]> globalCmds = MultiplayerServer.instance.globalCmds;
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(factionId);
            writer.WriteInt32(MultiplayerServer.instance.timer);
            writer.WriteByteArrayList(globalCmds);
            writer.WritePrefixedBytes(MultiplayerServer.instance.savedGame);

            writer.WriteInt32(MultiplayerServer.instance.mapCmds.Count);

            foreach (var kv in MultiplayerServer.instance.mapCmds)
            {
                int mapId = kv.Key;
                List<byte[]> mapCmds = kv.Value;

                MultiplayerServer.instance.SendCommand(CommandType.CreateMapFactionData, ScheduledCommand.NoFaction, mapId, ByteWriter.GetBytes(factionId));

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

            byte[] packetData = writer.GetArray();

            connection.State = ConnectionStateEnum.ServerPlaying;
            connection.Send(Packets.Server_WorldData, packetData);

            MpLog.Log("World response sent: " + packetData.Length + " " + globalCmds.Count);
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
            byte[] extra = data.ReadPrefixedBytes();

            // todo check if map id is valid for the player

            int factionId = MultiplayerServer.instance.playerFactions[connection.username];
            MultiplayerServer.instance.SendCommand(cmd, factionId, mapId, extra, connection.username);
        }

        [PacketHandler(Packets.Client_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            msg = msg.Trim();

            if (msg.Length == 0) return;

            MultiplayerServer.instance.SendToAll(Packets.Server_Chat, new object[] { connection.username, msg });
        }

        [PacketHandler(Packets.Client_AutosavedData)]
        public void HandleAutosavedData(ByteReader data)
        {
            int type = data.ReadInt32();
            byte[] compressedData = data.ReadPrefixedBytes();

            if (type == 0) // World data
            {
                MultiplayerServer.instance.savedGame = compressedData;
            }
            else if (type == 1) // Map data
            {
                int mapId = data.ReadInt32();

                // todo test map ownership
                MultiplayerServer.instance.mapData[mapId] = compressedData;
            }
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

        public static Dictionary<string, int[]> debugHashes = new Dictionary<string, int[]>();

        [PacketHandler(Packets.Client_Debug)]
        public void HandleDebug(ByteReader data)
        {
            int[] hashes = data.ReadPrefixedInts();
            debugHashes[connection.username] = hashes;

            if (debugHashes.Count == 2)
            {
                var first = debugHashes.ElementAt(0);
                var second = debugHashes.ElementAt(1);
                int index = int.MinValue;

                for (int i = 0; i < Math.Min(first.Value.Length, second.Value.Length); i++)
                {
                    if (first.Value[i] != second.Value[i])
                        index = i;
                }

                if (index == int.MinValue && first.Value.Length != second.Value.Length)
                    index = -1;

                MultiplayerServer.instance.SendToAll(Packets.Server_Debug, new object[] { index });

                debugHashes.Clear();
            }
        }

        public static string GetPlayerMapsPath(string username)
        {
            string worldfolder = Path.Combine(Path.Combine(MultiplayerServer.instance.saveFolder, "MpSaves"), MultiplayerServer.instance.worldId);
            DirectoryInfo directoryInfo = new DirectoryInfo(worldfolder);
            if (!directoryInfo.Exists)
                directoryInfo.Create();
            return Path.Combine(worldfolder, username + ".maps");
        }
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
