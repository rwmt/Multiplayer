using Ionic.Zlib;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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

        public byte[] savedGame;
        public Dictionary<string, byte[]> playerMaps = new Dictionary<string, byte[]>();
        public List<byte[]> cmdsSinceLastAutosave = new List<byte[]>();

        public int timer;
        public ActionQueue queue = new ActionQueue();
        public Connection host;
        public NetworkServer server;
        public string saveFolder;
        public string worldId;
        public IPAddress addr;
        public int port;

        public MultiplayerServer(IPAddress addr, int port = DEFAULT_PORT)
        {
            this.addr = addr;
            this.port = port;

            server = new NetworkServer(addr, port, conn =>
            {
                conn.onMainThread = Enqueue;
                conn.State = new ServerWorldState(conn);
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
                cmdsSinceLastAutosave.Clear();
                server.SendToAll(Packets.SERVER_COMMAND, ServerPlayingState.GetServerCommandMsg(CommandType.AUTOSAVE, new byte[0]));
            });
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
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
                Connection.Send(Packets.SERVER_DISCONNECT_REASON, "Invalid username length.");
                Connection.Close();
                return;
            }

            if (!UsernamePattern.IsMatch(username))
            {
                Connection.Send(Packets.SERVER_DISCONNECT_REASON, "Invalid username characters.");
                Connection.Close();
                return;
            }

            Connection.username = username;
        }

        [PacketHandler(Packets.CLIENT_REQUEST_WORLD)]
        public void HandleWorldRequest(ByteReader data)
        {
            MultiplayerServer.instance.host.Send(Packets.SERVER_NEW_FACTION_REQUEST);
        }

        [PacketHandler(Packets.CLIENT_NEW_FACTION_RESPONSE)]
        public void HandleNewFaction(ByteReader data)
        {
            string username = data.ReadString();
            byte[] newFactionData = data.ReadPrefixedBytes();

            // todo compress
            byte[][] cmds = MultiplayerServer.instance.cmdsSinceLastAutosave.ToArray();
            byte[] packetData = NetworkServer.GetBytes(MultiplayerServer.instance.timer + MultiplayerServer.SCHEDULED_CMD_DELAY, cmds, MultiplayerServer.instance.savedGame);

            MultiplayerServer.instance.server.SendToAll(Packets.SERVER_COMMAND, ServerPlayingState.GetServerCommandMsg(CommandType.NEW_FACTION, newFactionData));

            /*MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

            if (!comp.playerFactions.TryGetValue(username, out Faction faction))
            {
                faction = FactionGenerator.NewGeneratedFaction(FactionDefOf.PlayerColony);
                faction.Name = username + "'s faction";
                faction.def = Multiplayer.factionDef;

                byte[] extra = Server.GetBytes(username, ScribeUtil.WriteExposable(faction));
                Multiplayer.server.SendToAll(Packets.SERVER_COMMAND, ServerPlayingState.GetServerCommandMsg(CommandType.NEW_FACTION, extra));

                MpLog.Log("New faction: " + faction.Name);
            }*/

            Connection.Send(Packets.SERVER_WORLD_DATA, packetData);

            MpLog.Log("World response sent: " + packetData.Length + " " + cmds.Length);
        }

        [PacketHandler(Packets.CLIENT_WORLD_LOADED)]
        [HandleImmediately]
        public void HandleWorldLoaded(ByteReader data)
        {
            Connection.State = new ServerPlayingState(Connection);
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
            byte[] extra = data.ReadPrefixedBytes();

            byte[] toSend = NetworkServer.GetBytes(GetServerCommandMsg(cmd, extra));
            MultiplayerServer.instance.cmdsSinceLastAutosave.Add(toSend);

            MpLog.Log("Server command " + cmd);

            MultiplayerServer.instance.server.SendToAll(Packets.SERVER_COMMAND, toSend);
        }

        [PacketHandler(Packets.CLIENT_AUTOSAVED_DATA)]
        public void HandleAutosavedData(ByteReader data)
        {
            bool isGame = data.ReadBool();
            byte[] compressedData = data.ReadPrefixedBytes();
            byte[] uncompressedData = GZipStream.UncompressBuffer(compressedData);

            if (isGame)
            {
                MultiplayerServer.instance.savedGame = uncompressedData;
            }
            else
            {
                // handle map data
            }
        }

        [PacketHandler(Packets.CLIENT_ENCOUNTER_REQUEST)]
        public void HandleEncounterRequest(ByteReader data)
        {
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

        public static object[] GetServerCommandMsg(CommandType cmdType, byte[] extra)
        {
            return new object[] { cmdType, MultiplayerServer.instance.timer + MultiplayerServer.SCHEDULED_CMD_DELAY, extra };
        }
    }
}
