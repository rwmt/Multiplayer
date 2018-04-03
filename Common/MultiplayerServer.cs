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
                try
                {
                    queue.RunQueue();
                }
                catch (Exception e)
                {
                    MpLog.LogLines("Exception while executing the server action queue:", e.ToString());
                }

                server.SendToAll(Packets.SERVER_TIME_CONTROL, new object[] { timer + SCHEDULED_CMD_DELAY });

                timer += 6;

                Thread.Sleep(LOOP_RESOLUTION);
            }
        }

        public void DoAutosave()
        {
            Enqueue(() =>
            {
                globalCmds.Clear();
                foreach (int tile in mapCmds.Keys)
                    mapCmds[tile].Clear();

                server.SendToAll(Packets.SERVER_COMMAND, ServerPlayingState.GetServerCommandMsg(CommandType.AUTOSAVE, -1, new byte[0]));
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
    }

    public class IdBlock
    {
        public int blockStart;
        public int blockSize;
        public int mapTile = -1; // for encounters

        private int current;

        public IdBlock() { }

        public IdBlock(int blockStart, int blockSize, int mapTile = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapTile = mapTile;
        }

        public int NextId()
        {
            current++;
            if (current > blockSize * 0.95)
            {
                //Multiplayer.client.Send(Packets.CLIENT_ID_BLOCK_REQUEST, new object[] { mapTile });
                //Log.Message("Sent id block request at " + current);
            }

            return blockStart + current;
        }

        public byte[] Serialize()
        {
            return NetworkServer.GetBytes(blockSize, blockSize);
        }

        public static IdBlock Deserialize(ByteReader data)
        {
            return new IdBlock(data.ReadInt(), data.ReadInt());
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

            while (tempQueue.Count > 0)
                tempQueue.Dequeue().Invoke();
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
            MultiplayerServer.instance.host.Send(Packets.SERVER_NEW_FACTION_REQUEST, new object[] { Connection.username });
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

            byte[] toSend = NetworkServer.GetBytes(GetServerCommandMsg(cmd, mapId, extra));
            if (global)
                MultiplayerServer.instance.globalCmds.Add(toSend);
            else
                MultiplayerServer.instance.mapCmds.AddOrGet(mapId, new List<byte[]>()).Add(toSend);

            // todo send only to players playing the map if not global

            MultiplayerServer.instance.server.SendToAll(Packets.SERVER_COMMAND, toSend);
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

        [PacketHandler(Packets.CLIENT_NEW_FACTION_RESPONSE)]
        public void HandleNewFactionResponse(ByteReader data)
        {
            string username = data.ReadString();
            byte[] newFactionData = data.ReadPrefixedBytes();

            Connection conn = MultiplayerServer.instance.server.GetByUsername(username);
            if (conn == null) return;

            byte[][] cmds = MultiplayerServer.instance.globalCmds.ToArray();
            byte[] packetData = NetworkServer.GetBytes(MultiplayerServer.instance.timer + MultiplayerServer.SCHEDULED_CMD_DELAY, cmds, MultiplayerServer.instance.savedGame);

            if (newFactionData.Length > 0)
            {
                byte[] extra = NetworkServer.GetBytes(username, newFactionData);
                MultiplayerServer.instance.server.SendToAll(Packets.SERVER_COMMAND, GetServerCommandMsg(CommandType.NEW_FACTION, -1, extra));
            }

            conn.Send(Packets.SERVER_WORLD_DATA, packetData);

            MpLog.Log("World response sent: " + packetData.Length + " " + cmds.Length);
        }

        [PacketHandler(Packets.CLIENT_ENCOUNTER_REQUEST)]
        public void HandleEncounterRequest(ByteReader data)
        {
            int tile = data.ReadInt();
            if (!MultiplayerServer.instance.mapTiles.TryGetValue(tile, out int mapId))
                return;

            byte[] mapData = MultiplayerServer.instance.mapData[mapId];
            byte[][] mapCmds = MultiplayerServer.instance.mapCmds.AddOrGet(mapId, new List<byte[]>()).ToArray();
            byte[] packetData = NetworkServer.GetBytes(mapCmds, mapData);

            Connection.Send(Packets.SERVER_MAP_RESPONSE, packetData);
        }

        public void OnMessage(Packets packet, ByteReader data)
        {
            /*if (packet == Packets.CLIENT_ENCOUNTER_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    MpLog.Log("Encounter request");

                    int tile = data.ReadInt();
                    Settlement settlement = Find.WorldObjects.SettlementAt(tile);
                    if (settlement == null) return;
                    Faction faction = settlement.Faction;
                    string defender = Find.World.GetComponent<MultiplayerWorldComp>().GetUsernameByFaction(faction);
                    if (defender == null) return;
                    Connection conn = Multiplayer.server.GetByUsername(defender);
                    if (conn == null)
                    {
                        Connection.Send(Packets.SERVER_NOTIFICATION, new object[] { "The player is offline." });
                        return;
                    }

                    Encounter.Add(tile, defender, Connection.username);

                    // send the encounter map
                    // setup id blocks
                });
            }
            else if (packet == Packets.CLIENT_MAP_RESPONSE)
            {
                OnMainThread.Enqueue(() =>
                {
                    // setup the encounter

                    LongActionEncounter encounter = ((LongActionEncounter)OnMainThread.currentLongAction);

                    Connection attacker = Multiplayer.server.GetByUsername(encounter.attacker);
                    Connection defender = Multiplayer.server.GetByUsername(encounter.defender);

                    defender.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());
                    attacker.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());

                    encounter.waitingFor.Add(defender.username);
                    encounter.waitingFor.Add(attacker.username);

                    Faction attackerFaction = Find.World.GetComponent<MultiplayerWorldComp>().playerFactions[encounter.attacker];
                    FactionContext.Push(attackerFaction);

                    PawnGenerationRequest request = new PawnGenerationRequest(PawnKindDefOf.Colonist, attackerFaction, mustBeCapableOfViolence: true);
                    Pawn pawn = PawnGenerator.GeneratePawn(request);

                    ThingWithComps thingWithComps = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("Gun_IncendiaryLauncher"));
                    pawn.equipment.AddEquipment(thingWithComps);

                    byte[] pawnExtra = Server.GetBytes(encounter.tile, pawn.Faction.GetUniqueLoadID(), ScribeUtil.WriteSingle(pawn));

                    foreach (string player in new[] { encounter.attacker, encounter.defender })
                    {
                        Multiplayer.server.GetByUsername(player).Send(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(ServerAction.SPAWN_PAWN, pawnExtra));
                    }
                
                    FactionContext.Pop();
                });
            }
            else if (packet == Packets.CLIENT_MAP_LOADED)
            {
                OnMainThread.Enqueue(() =>
                {
                    // map loaded
                });
            }
            else if (packet == Packets.CLIENT_ID_BLOCK_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    int tile = data.ReadInt();

                    if (tile == -1)
                    {
                        IdBlock nextBlock = Multiplayer.NextIdBlock();
                        Connection.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteExposable(nextBlock));
                    }
                    else
                    {
                        Encounter encounter = Encounter.GetByTile(tile);
                        if (Connection.username != encounter.defender) return;

                        IdBlock nextBlock = Multiplayer.NextIdBlock();
                        nextBlock.mapTile = tile;

                        foreach (string player in encounter.GetPlayers())
                        {
                            byte[] extra = ScribeUtil.WriteExposable(nextBlock);
                            Multiplayer.server.GetByUsername(player).Send(Packets.SERVER_COMMAND, GetServerCommandMsg(CommandType.MAP_ID_BLOCK, extra));
                        }
                    }
                });
            }
            else if (packet == Packets.CLIENT_MAP_STATE_DEBUG)
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
            }
            else if (packet == Packets.CLIENT_AUTOSAVED_DATA)
            {
                OnMainThread.Enqueue(() =>
                {
                    bool isGame = data.ReadBool();
                    byte[] compressedData = data.ReadPrefixedBytes();
                    byte[] uncompressedData = GZipStream.UncompressBuffer(compressedData);

                    if (isGame)
                    {
                        Multiplayer.savedGame = uncompressedData;
                    }
                    else
                    {
                        // handle map data
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
