using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Multiplayer.Common
{
    public class MultiplayerServer
    {
        static MultiplayerServer()
        {
            Connection.RegisterState(typeof(ServerWorldState));
            Connection.RegisterState(typeof(ServerPlayingState));
        }

        public static MultiplayerServer instance;

        public const int SCHEDULED_CMD_DELAY = 15; // in ticks
        public const int DEFAULT_PORT = 30502;
        public const int LOOP_RESOLUTION = 100; // in ms, 6 game ticks

        public byte[] savedGame; // Compressed game save

        // World tile to map id
        public Dictionary<int, int> mapTiles = new Dictionary<int, int>();

        // Map id to compressed map data
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>();

        // Map id to serialized cmds list
        public Dictionary<int, List<byte[]>> mapCmds = new Dictionary<int, List<byte[]>>();
        public List<byte[]> globalCmds = new List<byte[]>();

        public int timer;
        public ActionQueue queue = new ActionQueue();
        public Connection host;
        public NetworkServer server;
        public string saveFolder;
        public string worldId;
        public IPAddress addr;
        public int port;

        public int highestUniqueId = -1;

        public MultiplayerServer(IPAddress addr, int port = DEFAULT_PORT)
        {
            this.addr = addr;
            this.port = port;

            server = new NetworkServer(addr, port, newConn =>
            {
                newConn.onMainThread = Enqueue;
                newConn.State = new ServerWorldState(newConn);

                newConn.closedCallback += () =>
                {
                    if (newConn.username == null) return;

                    server.SendToAll(Packets.SERVER_NOTIFICATION, new object[] { "Player " + newConn.username + " disconnected." });
                    UpdatePlayerList();
                };
            });
        }

        public void Run()
        {
            while (true)
            {
                queue.RunQueue();

                server.SendToAll(Packets.SERVER_TIME_CONTROL, new object[] { timer + SCHEDULED_CMD_DELAY });

                timer += 6;

                Thread.Sleep(LOOP_RESOLUTION);
            }
        }

        public void DoAutosave()
        {
            Enqueue(() =>
            {
                SendCommand(CommandType.AUTOSAVE, -1, new byte[0]);

                globalCmds.Clear();
                foreach (int tile in mapCmds.Keys)
                    mapCmds[tile].Clear();
            });
        }

        public void UpdatePlayerList()
        {
            string[] players;
            lock (server.GetConnections())
                players = server.GetConnections().Select(conn => conn.username).ToArray();

            server.SendToAll(Packets.SERVER_PLAYER_LIST, new object[] { players });
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public IdBlock NextIdBlock()
        {
            int blockSize = 30000;
            int blockStart = highestUniqueId;
            highestUniqueId = highestUniqueId + blockSize;
            MpLog.Log("New id block " + blockStart + " of size " + blockSize);

            return new IdBlock(blockStart, blockSize);
        }

        public void SendCommand(CommandType cmd, int mapId, byte[] extra)
        {
            // todo send only to players playing the map if not global

            bool global = ScheduledCommand.IsCommandGlobal(cmd);
            byte[] toSend = NetworkServer.GetBytes(ServerPlayingState.GetServerCommandMsg(cmd, mapId, extra));

            if (global)
                globalCmds.Add(toSend);
            else
                mapCmds.AddOrGet(mapId, new List<byte[]>()).Add(toSend);

            server.SendToAll(Packets.SERVER_COMMAND, toSend);
        }
    }

    public class IdBlock
    {
        public int blockStart;
        public int blockSize;
        public int mapId = -1; // -1 means global

        public int current;
        public bool overflowHandled;

        public IdBlock(int blockStart, int blockSize, int mapId = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapId = mapId;
        }

        public int NextId()
        {
            // Overflows should be handled by the caller
            current++;
            return blockStart + current;
        }

        public byte[] Serialize()
        {
            return NetworkServer.GetBytes(blockSize, blockSize, mapId, current);
        }

        public static IdBlock Deserialize(ByteReader data)
        {
            IdBlock block = new IdBlock(data.ReadInt(), data.ReadInt(), data.ReadInt());
            block.current = data.ReadInt();
            return block;
        }
    }

    public class ActionQueue
    {
        private Queue<Action> queue = new Queue<Action>();
        private Queue<Action> tempQueue = new Queue<Action>();

        public void RunQueue()
        {
            lock (queue)
            {
                if (queue.Count > 0)
                {
                    foreach (Action a in queue)
                        tempQueue.Enqueue(a);
                    queue.Clear();
                }
            }

            try
            {
                while (tempQueue.Count > 0)
                    tempQueue.Dequeue().Invoke();
            }
            catch (Exception e)
            {
                MpLog.LogLines("Exception while executing action queue", e.ToString());
            }
        }

        public void Enqueue(Action action)
        {
            lock (queue)
                queue.Enqueue(action);
        }
    }

    public class PacketHandlerAttribute : Attribute
    {
        public readonly object packet;

        public PacketHandlerAttribute(object packet)
        {
            this.packet = packet;
        }
    }

    // i.e. not on the main thread
    public class HandleImmediatelyAttribute : Attribute
    {
    }

    public class ServerWorldState : ConnectionState
    {
        private static Regex UsernamePattern = new Regex(@"^[a-zA-Z0-9_]+$");

        public ServerWorldState(Connection connection) : base(connection)
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

            if (MultiplayerServer.instance.server.GetByUsername(username) != null)
            {
                Connection.Close("Username already online.");
                return;
            }

            Connection.username = username;

            MultiplayerServer.instance.server.SendToAll(Packets.SERVER_NOTIFICATION, new object[] { "Player " + Connection.username + " has joined the game." });
            MultiplayerServer.instance.UpdatePlayerList();
        }

        [PacketHandler(Packets.CLIENT_REQUEST_WORLD)]
        public void HandleWorldRequest(ByteReader data)
        {
            byte[] extra = NetworkServer.GetBytes(Connection.username);
            MultiplayerServer.instance.SendCommand(CommandType.SETUP_FACTION, -1, extra);

            byte[][] cmds = MultiplayerServer.instance.globalCmds.ToArray();
            byte[] packetData = NetworkServer.GetBytes(MultiplayerServer.instance.timer + MultiplayerServer.SCHEDULED_CMD_DELAY, cmds, MultiplayerServer.instance.savedGame);

            Connection.Send(Packets.SERVER_WORLD_DATA, packetData);

            MpLog.Log("World response sent: " + packetData.Length + " " + cmds.Length);
        }

        [PacketHandler(Packets.CLIENT_WORLD_LOADED)]
        [HandleImmediately]
        public void HandleWorldLoaded(ByteReader data)
        {
            Connection.State = new ServerPlayingState(Connection);
            MultiplayerServer.instance.UpdatePlayerList();
        }

        public override void Disconnected()
        {
        }
    }

    public class ServerPlayingState : ConnectionState
    {
        public ServerPlayingState(Connection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.CLIENT_COMMAND)]
        public void HandleClientCommand(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt();
            int mapId = data.ReadInt();
            byte[] extra = data.ReadPrefixedBytes();

            bool global = ScheduledCommand.IsCommandGlobal(cmd);
            if (global && mapId != -1)
            {
                MpLog.Log("Client {0} sent a global command {1} with map id specified.", Connection.username, cmd);
                mapId = -1;
            }
            else if (!global && mapId < 0)
            {
                MpLog.Log("Client {0} sent a map command {1} without a map id.", Connection.username, cmd);
                return;
            }

            // todo check if map id is valid for the player

            MultiplayerServer.instance.SendCommand(cmd, mapId, extra);
        }

        [PacketHandler(Packets.CLIENT_CHAT)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            msg = msg.Trim();

            if (msg.Length == 0) return;

            MultiplayerServer.instance.server.SendToAll(Packets.SERVER_CHAT, new object[] { Connection.username, msg });
        }

        [PacketHandler(Packets.CLIENT_AUTOSAVED_DATA)]
        public void HandleAutosavedData(ByteReader data)
        {
            bool isGame = data.ReadBool();

            if (isGame)
            {
                byte[] compressedData = data.ReadPrefixedBytes();
                MultiplayerServer.instance.savedGame = compressedData;
            }
            else
            {
                int mapId = data.ReadInt();
                byte[] compressedData = data.ReadPrefixedBytes();

                // todo test map ownership
                MultiplayerServer.instance.mapData[mapId] = compressedData;
            }
        }

        [PacketHandler(Packets.CLIENT_ENCOUNTER_REQUEST)]
        public void HandleEncounterRequest(ByteReader data)
        {
            int tile = data.ReadInt();
            if (!MultiplayerServer.instance.mapTiles.TryGetValue(tile, out int mapId))
                return;

            byte[] extra = NetworkServer.GetBytes(Connection.username);
            MultiplayerServer.instance.SendCommand(CommandType.MAP_FACTION_DATA, mapId, extra);

            byte[] mapData = MultiplayerServer.instance.mapData[mapId];
            byte[][] mapCmds = MultiplayerServer.instance.mapCmds.AddOrGet(mapId, new List<byte[]>()).ToArray();
            byte[] packetData = NetworkServer.GetBytes(mapCmds, mapData);

            Connection.Send(Packets.SERVER_MAP_RESPONSE, packetData);
        }

        [PacketHandler(Packets.CLIENT_ID_BLOCK_REQUEST)]
        public void HandleIdBlockRequest(ByteReader data)
        {
            int mapId = data.ReadInt();

            if (mapId == -1)
            {
                IdBlock nextBlock = MultiplayerServer.instance.NextIdBlock();
                MultiplayerServer.instance.SendCommand(CommandType.GLOBAL_ID_BLOCK, -1, nextBlock.Serialize());
            }
            else
            {
                // todo
            }
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

        public override void Disconnected()
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

        public static object[] GetServerCommandMsg(CommandType cmdType, int mapId, byte[] extra)
        {
            return new object[] { cmdType, MultiplayerServer.instance.timer + MultiplayerServer.SCHEDULED_CMD_DELAY, mapId, extra };
        }
    }
}
