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

        public ServerJoiningState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.CLIENT_USERNAME)]
        public void HandleClientUsername(ByteReader data)
        {
            string username = data.ReadString();

            if (username.Length < 3 || username.Length > 15)
            {
                Connection.Close("Invalid username length.");
                return;
            }

            if (!UsernamePattern.IsMatch(username))
            {
                Connection.Close("Invalid username characters.");
                return;
            }

            if (MultiplayerServer.instance.GetPlayer(username) != null)
            {
                Connection.Close("Username already online.");
                return;
            }

            Connection.Username = username;

            MultiplayerServer.instance.SendToAll(Packets.SERVER_NOTIFICATION, new object[] { "Player " + Connection.Username + " has joined the game." });
            MultiplayerServer.instance.UpdatePlayerList();
        }

        [PacketHandler(Packets.CLIENT_REQUEST_WORLD)]
        public void HandleWorldRequest(ByteReader data)
        {
            if (!MultiplayerServer.instance.playerFactions.TryGetValue(Connection.Username, out int factionId))
            {
                factionId = MultiplayerServer.instance.nextUniqueId++;
                MultiplayerServer.instance.playerFactions[Connection.Username] = factionId;

                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.SETUP_FACTION, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }

            if (MultiplayerServer.instance.players.Count(p => p.FactionId == factionId) == 1)
            {
                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.FACTION_ONLINE, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }

            List<byte[]> globalCmds = MultiplayerServer.instance.globalCmds;
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(factionId);
            writer.WriteInt32(MultiplayerServer.instance.timer);
            writer.Write(globalCmds);
            writer.WritePrefixedBytes(MultiplayerServer.instance.savedGame);

            writer.WriteInt32(1); // maps count

            foreach (int mapId in new[] { 0 })
            {
                MultiplayerServer.instance.SendCommand(CommandType.CREATE_MAP_FACTION_DATA, ScheduledCommand.NoFaction, mapId, ByteWriter.GetBytes(factionId));

                writer.WriteInt32(mapId);
                writer.Write(MultiplayerServer.instance.mapCmds[mapId]);
                writer.WritePrefixedBytes(MultiplayerServer.instance.mapData[mapId]);
            }

            byte[] packetData = writer.GetArray();

            Connection.State = new ServerPlayingState(Connection);
            Connection.Send(Packets.SERVER_WORLD_DATA, packetData);

            MpLog.Log("World response sent: " + packetData.Length + " " + globalCmds.Count);
        }

        public override void Disconnected(string reason)
        {
        }
    }

    public class ServerPlayingState : MpConnectionState
    {
        public ServerPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.CLIENT_WORLD_LOADED)]
        public void HandleWorldLoaded(ByteReader data)
        {
        }

        [PacketHandler(Packets.CLIENT_COMMAND)]
        public void HandleClientCommand(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extra = data.ReadPrefixedBytes();

            // todo check if map id is valid for the player

            int factionId = MultiplayerServer.instance.playerFactions[Connection.Username];
            MultiplayerServer.instance.SendCommand(cmd, factionId, mapId, extra);
        }

        [PacketHandler(Packets.CLIENT_CHAT)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            msg = msg.Trim();

            if (msg.Length == 0) return;

            MultiplayerServer.instance.SendToAll(Packets.SERVER_CHAT, new object[] { Connection.Username, msg });
        }

        [PacketHandler(Packets.CLIENT_AUTOSAVED_DATA)]
        public void HandleAutosavedData(ByteReader data)
        {
            int type = data.ReadInt32();
            byte[] compressedData = data.ReadPrefixedBytes();

            if (type == 0) // Faction data
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

        [PacketHandler(Packets.CLIENT_ENCOUNTER_REQUEST)]
        public void HandleEncounterRequest(ByteReader data)
        {
            int tile = data.ReadInt32();
            if (!MultiplayerServer.instance.mapTiles.TryGetValue(tile, out int mapId))
                return;

            byte[] extra = ByteWriter.GetBytes(Connection.Username); // todo faction id
            MultiplayerServer.instance.SendCommand(CommandType.CREATE_MAP_FACTION_DATA, ScheduledCommand.NoFaction, mapId, extra);

            byte[] mapData = MultiplayerServer.instance.mapData[mapId];
            List<byte[]> mapCmds = MultiplayerServer.instance.mapCmds.AddOrGet(mapId, new List<byte[]>());

            byte[] packetData = ByteWriter.GetBytes(mapId, mapCmds, mapData);
            Connection.Send(Packets.SERVER_MAP_RESPONSE, packetData);
        }

        [PacketHandler(Packets.CLIENT_ID_BLOCK_REQUEST)]
        public void HandleIdBlockRequest(ByteReader data)
        {
            int mapId = data.ReadInt32();

            if (mapId == ScheduledCommand.Global)
            {
                IdBlock nextBlock = MultiplayerServer.instance.NextIdBlock();
                MultiplayerServer.instance.SendCommand(CommandType.GLOBAL_ID_BLOCK, ScheduledCommand.NoFaction, ScheduledCommand.Global, nextBlock.Serialize());
            }
            else
            {
                // todo
            }
        }

        [PacketHandler(Packets.CLIENT_KEEP_ALIVE)]
        public void HandleClientKeepAlive(ByteReader data)
        {
            // Ping already handled by LiteNetLib
            if (Connection is MpNetConnection) return;

            int id = data.ReadInt32();
            if (MultiplayerServer.instance.keepAliveId == id)
                Connection.Latency = (int)MultiplayerServer.instance.lastKeepAlive.ElapsedMilliseconds / 2;
            else
                Connection.Latency = 2000;
        }

        public void OnMessage(Packets packet, ByteReader data)
        {
            /* if (packet == Packets.CLIENT_MAP_STATE_DEBUG)
            {
                OnMainThread.Enqueue(() => { Log.Message("Got map state " + Connection.username + " " + data.GetBytes().Length); });

                ThreadPool.QueueUserWorkItem(s =>
                {
                    using (MemoryStream stream = new MemoryStream(data.GetBytes()))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        XmlDocument xmlDocument = new XmlDocument();
                        xmlDocument.Load(xml);
                        xmlDocument.DocumentElement["map"].RemoveChildIfPresent("rememberedCameraPos");
                        xmlDocument.Save(GetPlayerMapsPath(Connection.username + "_replay"));
                        OnMainThread.Enqueue(() => { Log.Message("Writing done for " + Connection.username); });
                    }
                });
            }*/
        }

        public override void Disconnected(string reason)
        {
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
        public ServerSteamState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.CLIENT_STEAM_REQUEST)]
        public void HandleSteamRequest(ByteReader data)
        {
            Connection.State = new ServerJoiningState(Connection);
            Connection.Send(Packets.SERVER_STEAM_ACCEPT);
        }

        public override void Disconnected(string reason)
        {
        }
    }
}
