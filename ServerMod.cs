using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;

namespace ServerMod
{
    [StaticConstructorOnStartup]
    public class ServerMod
    {
        public const int DEFAULT_PORT = 30502;

        public static String username;
        public static Server server;
        public static Connection client;
        public static Connection localServerConnection;

        public static byte[] savedWorld;
        public static byte[] mapsData;
        public static bool savingForEncounter;
        public static AutoResetEvent pause = new AutoResetEvent(false);

        public static IdBlock mainBlock;

        public static int highestUniqueId = -1;

        static ServerMod()
        {
            GenCommandLine.TryGetCommandLineArg("username", out username);
            if (username == null)
                username = SteamUtility.SteamPersonaName;
            if (username == "???")
                username = "Player" + Rand.Range(0, 9999);

            Log.Message("Player's username: " + username);

            var gameobject = new GameObject();
            gameobject.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(gameobject);

            var harmony = HarmonyInstance.Create("servermod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            if (GenCommandLine.CommandLineArgPassed("dev"))
            {
                // generate and load dummy dev map
                DebugSettings.noAnimals = true;
                LongEventHandler.QueueLongEvent(null, "Play", "LoadingLongEvent", true, null);
            }
            else if (GenCommandLine.TryGetCommandLineArg("connect", out string ip))
            {
                if (String.IsNullOrEmpty(ip))
                    ip = "127.0.0.1";

                DebugSettings.noAnimals = true;
                LongEventHandler.QueueLongEvent(() =>
                {
                    IPAddress.TryParse(ip, out IPAddress addr);
                    Client.TryConnect(addr, ServerMod.DEFAULT_PORT, (conn, e) =>
                    {
                        if (e != null)
                        {
                            ServerMod.client = null;
                            return;
                        }

                        ServerMod.client = conn;
                        conn.username = ServerMod.username;
                        conn.SetState(new ClientWorldState(conn));
                    });
                }, "Connecting", false, null);
            }
        }

        public static IdBlock NextIdBlock()
        {
            int blockSize = 25000;
            int blockStart = highestUniqueId;
            highestUniqueId = highestUniqueId + blockSize;
            Log.Message("New id block " + blockStart + " of " + blockSize);
            return new IdBlock(blockStart, blockSize);
        }
    }

    public class IdBlock : AttributedExposable
    {
        [ExposeValue]
        public int blockStart;
        [ExposeValue]
        public int blockSize;
        [ExposeValue]
        public int mapTile = -1; // for encounters

        private int current;

        public IdBlock() { }

        public IdBlock(int blockStart, int blockSize, int mapTile = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapTile = mapTile;
            current = blockStart;
        }

        public int NextId()
        {
            current++;
            if (current > blockStart + blockSize * 0.8)
            {
                ServerMod.client.Send(Packets.CLIENT_ID_BLOCK_REQUEST, ScribeUtil.WriteSingle(this));
                Log.Message("Sent id block request at " + current);
            }

            return current;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            current = blockStart;
        }
    }

    public class ServerModInstance : Mod
    {
        public ServerModInstance(ModContentPack pack) : base(pack)
        {
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
        }

        public override string SettingsCategory()
        {
            return "Multiplayer";
        }
    }

    public class Encounter
    {
        private static List<Encounter> encounters = new List<Encounter>();

        public readonly int tile;
        public readonly string defender;
        public List<string> attackers = new List<string>();

        private Encounter(int tile, string defender, string attacker)
        {
            this.tile = tile;
            this.defender = defender;
            attackers.Add(attacker);
        }

        public IEnumerable<string> GetAssociatedPlayers()
        {
            yield return defender;
            foreach (string attacker in attackers)
                yield return attacker;
        }

        public static void Add(int tile, string defender, string attacker)
        {
            encounters.Add(new Encounter(tile, defender, attacker));
        }

        public static Encounter GetByTile(int tile)
        {
            return encounters.FirstOrDefault(e => e.tile == tile);
        }
    }

    public static class Packets
    {
        public const int CLIENT_REQUEST_WORLD = 0;
        public const int CLIENT_WORLD_LOADED = 1;
        public const int CLIENT_ACTION_REQUEST = 2;
        public const int CLIENT_USERNAME = 3;
        public const int CLIENT_NEW_WORLD_OBJ = 4;
        public const int CLIENT_QUIT_MAPS = 5;
        public const int CLIENT_ENCOUNTER_REQUEST = 6;
        public const int CLIENT_MAP_RESPONSE = 7;
        public const int CLIENT_MAP_LOADED = 8;
        public const int CLIENT_ID_BLOCK_REQUEST = 9;

        public const int SERVER_WORLD_DATA = 0;
        public const int SERVER_ACTION_SCHEDULE = 1;
        public const int SERVER_NEW_FACTIONS = 2;
        public const int SERVER_NEW_WORLD_OBJ = 3;
        public const int SERVER_MAP_REQUEST = 4;
        public const int SERVER_MAP_RESPONSE = 5;
        public const int SERVER_NOTIFICATION = 6;
        public const int SERVER_NEW_ID_BLOCK = 7;
    }

    public enum ServerAction : int
    {
        PAUSE, UNPAUSE, PAWN_JOB, SPAWN_THING, LONG_ACTION_SCHEDULE, LONG_ACTION_END, MAP_ID_BLOCK, AREA
    }

    public struct ScheduledServerAction
    {
        public readonly int ticks;
        public readonly ServerAction action;
        public readonly byte[] data;

        public ScheduledServerAction(int ticks, ServerAction action, byte[] data)
        {
            this.ticks = ticks;
            this.action = action;
            this.data = data;
        }
    }

    public class ByteReader
    {
        private readonly byte[] array;

        private int index;

        public ByteReader(byte[] array)
        {
            this.array = array;
        }

        public int ReadInt()
        {
            return BitConverter.ToInt32(array, IncrementIndex(4));
        }

        public byte[] ReadPrefixedBytes()
        {
            int len = ReadInt();
            return array.SubArray(IncrementIndex(len), len);
        }

        public int[] ReadPrefixedInts()
        {
            int len = ReadInt();
            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt();
            return result;
        }

        public string ReadString()
        {
            return Encoding.UTF8.GetString(ReadPrefixedBytes());
        }

        public int IncrementIndex(int val)
        {
            int i = index;
            index += val;
            if (index > array.Length)
                throw new IndexOutOfRangeException();
            return i;
        }

        public byte[] GetBytes()
        {
            return array;
        }
    }

    public static class Extensions
    {
        public static T[] Append<T>(this T[] arr1, T[] arr2)
        {
            T[] result = new T[arr1.Length + arr2.Length];
            Array.Copy(arr1, 0, result, 0, arr1.Length);
            Array.Copy(arr2, 0, result, arr1.Length, arr2.Length);
            return result;
        }

        public static T[] SubArray<T>(this T[] data, int index, int length)
        {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public static void RemoveChildIfPresent(this XmlNode node, string child)
        {
            XmlNode childNode = node[child];
            if (childNode != null)
                node.RemoveChild(childNode);
        }

        public static void RemoveFromParent(this XmlNode node)
        {
            if (node == null) return;
            node.ParentNode.RemoveChild(node);
        }

        public static void Write(this MemoryStream stream, byte[] arr)
        {
            if (arr.Length > 0)
                stream.Write(arr, 0, arr.Length);
        }
    }

    public class CountdownLock<T>
    {
        public AutoResetEvent eventObj = new AutoResetEvent(false);
        private HashSet<T> ids = new HashSet<T>();

        public void Add(T id)
        {
            lock (ids)
            {
                ids.Add(id);
            }
        }

        public void Wait()
        {
            eventObj.WaitOne();
        }

        public bool Done(T id)
        {
            lock (ids)
            {
                if (!ids.Remove(id))
                    return false;

                if (ids.Count == 0)
                {
                    eventObj.Set();
                    return true;
                }

                return false;
            }
        }

        public HashSet<T> GetIds()
        {
            lock (ids)
                return ids;
        }
    }

    public class ServerWorldState : ConnectionState
    {
        public ServerWorldState(Connection connection) : base(connection)
        {
        }

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.CLIENT_REQUEST_WORLD)
            {
                OnMainThread.Enqueue(() =>
                {
                    byte[] extra = ScribeUtil.WriteSingle(new LongActionPlayerJoin() { username = Connection.username });
                    ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, extra), Connection);
                });
            }
            else if (id == Packets.CLIENT_WORLD_LOADED)
            {
                Connection.SetState(new ServerPlayingState(this.Connection));

                OnMainThread.Enqueue(() =>
                {
                    ServerMod.savedWorld = null;

                    byte[] extra = ScribeUtil.WriteSingle(OnMainThread.currentLongAction);
                    ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_END, extra));

                    Log.Message("world sending finished");
                });
            }
            else if (id == Packets.CLIENT_USERNAME)
            {
                OnMainThread.Enqueue(() => Connection.username = data.ReadString());
            }
        }

        public void SendData()
        {
            string mapsFile = ServerPlayingState.GetPlayerMapsPath(Connection.username);
            byte[] mapsData = new byte[0];
            if (File.Exists(mapsFile))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    XmlDocument maps = new XmlDocument();
                    maps.Load(mapsFile);
                    maps.Save(stream);
                    mapsData = stream.ToArray();
                }
            }

            Connection.Send(Packets.SERVER_WORLD_DATA, new object[] { ServerMod.savedWorld, mapsData });
        }

        public override void Disconnect()
        {
        }
    }

    public class ServerPlayingState : ConnectionState
    {
        public ServerPlayingState(Connection connection) : base(connection)
        {
        }

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.CLIENT_ACTION_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    ServerAction action = (ServerAction)data.ReadInt();
                    int ticks = Find.TickManager.TicksGame + 15;
                    byte[] extra = data.ReadPrefixedBytes();

                    ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(action, extra));

                    Log.Message("server got request from client at " + Find.TickManager.TicksGame + " for " + action + " " + ticks);
                });
            }
            else if (id == Packets.CLIENT_NEW_WORLD_OBJ)
            {
                ServerMod.server.SendToAll(Packets.SERVER_NEW_WORLD_OBJ, data.GetBytes(), Connection);
            }
            else if (id == Packets.CLIENT_QUIT_MAPS)
            {
                new Thread(() =>
                {
                    try
                    {
                        using (MemoryStream stream = new MemoryStream(data.GetBytes()))
                        using (XmlTextReader xml = new XmlTextReader(stream))
                        {
                            XmlDocument xmlDocument = new XmlDocument();
                            xmlDocument.Load(xml);
                            xmlDocument.Save(GetPlayerMapsPath(Connection.username));
                        }
                    }
                    catch (XmlException e)
                    {
                        Log.Error("Couldn't save " + Connection.username + "'s maps");
                        Log.Error(e.ToString());
                    }
                }).Start();
            }
            else if (id == Packets.CLIENT_ENCOUNTER_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    Log.Message("encounter request");

                    int tile = data.ReadInt();
                    Settlement settlement = Find.WorldObjects.SettlementAt(tile);
                    if (settlement == null) return;
                    Faction faction = settlement.Faction;
                    string defender = Find.World.GetComponent<ServerModWorldComp>().GetUsername(faction);
                    if (defender == null) return;
                    Connection conn = ServerMod.server.GetByUsername(defender);
                    if (conn == null)
                    {
                        Connection.Send(Packets.SERVER_NOTIFICATION, new object[] { "The player is offline." });
                        return;
                    }

                    Encounter.Add(tile, defender, Connection.username);

                    byte[] extra = ScribeUtil.WriteSingle(new LongActionEncounter() { defender = defender, attacker = Connection.username, tile = tile });
                    conn.Send(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, extra));
                });
            }
            else if (id == Packets.CLIENT_MAP_RESPONSE)
            {
                OnMainThread.Enqueue(() =>
                {
                    Connection conn = ServerMod.server.GetByUsername(((LongActionEncounter)OnMainThread.currentLongAction).attacker);
                    conn.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());
                });
            }
            else if (id == Packets.CLIENT_MAP_LOADED)
            {
                OnMainThread.Enqueue(() =>
                {
                    byte[] extra = ScribeUtil.WriteSingle(OnMainThread.currentLongAction);
                    ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(ServerAction.LONG_ACTION_END, extra));
                });
            }
            else if (id == Packets.CLIENT_ID_BLOCK_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    IdBlock request = ScribeUtil.ReadSingle<IdBlock>(data.GetBytes());

                    if (request.mapTile == -1)
                    {
                        IdBlock nextBlock = ServerMod.NextIdBlock();
                        Connection.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteSingle(nextBlock));
                    }
                    else
                    {
                        Encounter encounter = Encounter.GetByTile(request.mapTile);
                        if (Connection.username != encounter.defender) return;

                        IdBlock nextBlock = ServerMod.NextIdBlock();
                        nextBlock.mapTile = request.mapTile;

                        foreach (string player in encounter.GetAssociatedPlayers())
                        {
                            byte[] extra = ScribeUtil.WriteSingle(nextBlock);
                            ServerMod.server.GetByUsername(player).Send(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(ServerAction.MAP_ID_BLOCK, extra));
                        }
                    }
                });
            }
        }

        public override void Disconnect()
        {
        }

        public static string GetPlayerMapsPath(string username)
        {
            string worldfolder = Path.Combine(Path.Combine(GenFilePaths.SaveDataFolderPath, "MpSaves"), Find.World.GetComponent<ServerModWorldComp>().worldId);
            DirectoryInfo directoryInfo = new DirectoryInfo(worldfolder);
            if (!directoryInfo.Exists)
                directoryInfo.Create();
            return Path.Combine(worldfolder, username + ".maps");
        }

        public static object[] GetServerActionMsg(ServerAction action, byte[] extra)
        {
            return new object[] { action, Find.TickManager.TicksGame + 15, extra };
        }
    }

    public class ClientWorldState : ConnectionState
    {
        public ClientWorldState(Connection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, new object[] { ServerMod.username });
            connection.Send(Packets.CLIENT_REQUEST_WORLD);
        }

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.SERVER_ACTION_SCHEDULE)
            {
                ClientPlayingState.HandleActionSchedule(data);
            }
            else if (id == Packets.SERVER_WORLD_DATA)
            {
                OnMainThread.Enqueue(() =>
                {
                    ServerMod.savedWorld = data.ReadPrefixedBytes();
                    ServerMod.mapsData = data.ReadPrefixedBytes();

                    Log.Message("World size: " + ServerMod.savedWorld.Length + ", Maps size: " + ServerMod.mapsData.Length);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        MemoryUtility.ClearAllMapsAndWorld();
                        Current.Game = new Game();
                        Current.Game.InitData = new GameInitData();
                        Current.Game.InitData.gameToLoad = "server";
                    }, "Play", "LoadingLongEvent", true, null);
                });
            }
            else if (id == Packets.SERVER_NEW_ID_BLOCK)
            {
                OnMainThread.Enqueue(() =>
                {
                    ServerMod.mainBlock = ScribeUtil.ReadSingle<IdBlock>(data.GetBytes());
                });
            }
        }

        public override void Disconnect()
        {
        }
    }

    public class ClientPlayingState : ConnectionState
    {
        public ClientPlayingState(Connection connection) : base(connection)
        {
        }

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.SERVER_ACTION_SCHEDULE)
            {
                HandleActionSchedule(data);
            }
            else if (id == Packets.SERVER_NEW_FACTIONS)
            {
                OnMainThread.Enqueue(() =>
                {
                    FactionData newFaction = ScribeUtil.ReadSingle<FactionData>(data.GetBytes());

                    Find.FactionManager.Add(newFaction.faction);
                    Find.World.GetComponent<ServerModWorldComp>().playerFactions[newFaction.owner] = newFaction.faction;
                });
            }
            else if (id == Packets.SERVER_NEW_WORLD_OBJ)
            {
                OnMainThread.Enqueue(() =>
                {
                    Find.WorldObjects.Add(ScribeUtil.ReadSingle<WorldObject>(data.GetBytes()));
                });
            }
            else if (id == Packets.SERVER_MAP_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    int tile = data.ReadInt();
                    Settlement settlement = Find.WorldObjects.SettlementAt(tile);

                    ServerMod.savingForEncounter = true;
                    ScribeUtil.StartWriting();
                    Scribe.EnterNode("data");
                    Map map = settlement.Map;
                    Scribe_Deep.Look(ref map, "map");
                    byte[] mapData = ScribeUtil.FinishWriting();
                    ServerMod.savingForEncounter = false;

                    ServerMod.client.Send(Packets.CLIENT_MAP_RESPONSE, mapData);
                });
            }
            else if (id == Packets.SERVER_MAP_RESPONSE)
            {
                OnMainThread.Enqueue(() =>
                {
                    Current.ProgramState = ProgramState.MapInitializing;

                    Log.Message("Encounter map size: " + data.GetBytes().Length);

                    ScribeUtil.StartLoading(data.GetBytes());
                    ScribeUtil.SupplyCrossRefs();
                    Map map = null;
                    Scribe_Deep.Look(ref map, "map");
                    ScribeUtil.FinishLoading();

                    Current.Game.AddMap(map);
                    map.FinalizeLoading();

                    {
                        Faction ownerFaction = map.info.parent.Faction;
                        ServerModWorldComp worldComp = Find.World.GetComponent<ServerModWorldComp>();
                        ServerModMapComp mapComp = map.GetComponent<ServerModMapComp>();

                        mapComp.factionAreas.Add(ownerFaction.GetUniqueLoadID(), map.areaManager);

                        map.areaManager = new AreaManager(map);
                        map.areaManager.AddStartingAreas();
                    }

                    Current.ProgramState = ProgramState.Playing;

                    ServerMod.client.Send(Packets.CLIENT_MAP_LOADED);
                });
            }
            else if (id == Packets.SERVER_NEW_ID_BLOCK)
            {
                OnMainThread.Enqueue(() =>
                {
                    IdBlock block = ScribeUtil.ReadSingle<IdBlock>(data.GetBytes());
                    if (block.mapTile != -1)
                        Find.WorldObjects.MapParentAt(block.mapTile).Map.GetComponent<ServerModMapComp>().encounterIdBlock = block;
                    else
                        ServerMod.mainBlock = block;
                });
            }
        }

        public override void Disconnect()
        {
        }

        public static void HandleActionSchedule(ByteReader data)
        {
            OnMainThread.Enqueue(() =>
            {
                ServerAction action = (ServerAction)data.ReadInt();
                int ticks = data.ReadInt();
                byte[] extraBytes = data.ReadPrefixedBytes();

                ScheduledServerAction schdl = new ScheduledServerAction(ticks, action, extraBytes);
                OnMainThread.ScheduleAction(schdl);

                Log.Message("client got request from server at " + (Current.Game != null ? Find.TickManager.TicksGame : 0) + " for action " + schdl.action + " " + schdl.ticks);
            });
        }

        // Currently covers:
        // - settling after joining
        public static void SyncClientWorldObj(WorldObject obj)
        {
            byte[] data = ScribeUtil.WriteSingle(obj);
            ServerMod.client.Send(Packets.CLIENT_NEW_WORLD_OBJ, data);
        }
    }

    public class OnMainThread : MonoBehaviour
    {
        private static readonly Queue<Action> queue = new Queue<Action>();

        public static readonly Queue<ScheduledServerAction> longActionRelated = new Queue<ScheduledServerAction>();
        public static readonly Queue<ScheduledServerAction> scheduledActions = new Queue<ScheduledServerAction>();

        public static readonly List<LongAction> longActions = new List<LongAction>();
        public static LongAction currentLongAction;

        public static readonly FieldInfo areasField = typeof(AreaManager).GetField("areas", BindingFlags.Instance | BindingFlags.NonPublic);

        public void Update()
        {
            if (ServerMod.client == null) return;

            lock (queue)
                while (queue.Count > 0)
                    queue.Dequeue().Invoke();

            if (Current.Game == null || Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                while (longActionRelated.Count > 0)
                    ExecuteLongActionRelated(longActionRelated.Dequeue());

            if (!LongEventHandler.ShouldWaitForEvent && Current.Game != null && Find.World != null && longActions.Count == 0 && currentLongAction == null)
            {
                while (scheduledActions.Count > 0 && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                {
                    ScheduledServerAction action = scheduledActions.Dequeue();
                    ExecuteServerAction(action, new ByteReader(action.data));
                }

                foreach (Map map in Find.Maps)
                    foreach (KeyValuePair<string, HashSet<int>[]> pair in map.GetComponent<ServerModMapComp>().areaChangesThisTick)
                    {
                        if (pair.Value[0].Count == 0 && pair.Value[1].Count == 0) continue;

                        int[] cells_off = pair.Value[0].ToArray();
                        int[] cells_on = pair.Value[1].ToArray();

                        byte[] extra = Server.GetBytes(1, map.GetUniqueLoadID(), Faction.OfPlayer.GetUniqueLoadID(), pair.Key, cells_off, cells_on);
                        ServerMod.client.Send(Packets.CLIENT_ACTION_REQUEST, new object[] { ServerAction.AREA, extra });

                        pair.Value[0].Clear();
                        pair.Value[1].Clear();
                    }
            }

            if (currentLongAction == null && longActions.Count > 0)
            {
                currentLongAction = longActions[0];
                longActions.RemoveAt(0);
            }

            if (currentLongAction != null && currentLongAction.shouldRun)
            {
                currentLongAction.Run();
                currentLongAction.shouldRun = false;
            }
        }

        public static void Enqueue(Action action)
        {
            lock (queue)
                queue.Enqueue(action);
        }

        public static void ScheduleAction(ScheduledServerAction actionReq)
        {
            if (actionReq.action == ServerAction.LONG_ACTION_SCHEDULE || actionReq.action == ServerAction.LONG_ACTION_END)
            {
                longActionRelated.Enqueue(actionReq);
                return;
            }

            scheduledActions.Enqueue(actionReq);
        }

        public static void ExecuteLongActionRelated(ScheduledServerAction actionReq)
        {
            if (actionReq.action == ServerAction.LONG_ACTION_SCHEDULE)
            {
                if (Current.Game != null)
                    TickUpdatePatch.SetSpeed(TimeSpeed.Paused);

                AddLongAction(ScribeUtil.ReadSingle<LongAction>(actionReq.data));
            }

            if (actionReq.action == ServerAction.LONG_ACTION_END)
            {
                LongAction longAction = ScribeUtil.ReadSingle<LongAction>(actionReq.data);
                if (longAction.Equals(currentLongAction))
                    currentLongAction = null;
                else
                    longActions.RemoveAll(longAction.Equals);
            }
        }

        public static void ExecuteServerAction(ScheduledServerAction actionReq, ByteReader data)
        {
            ServerAction action = actionReq.action;

            if (action == ServerAction.PAUSE)
            {
                TickUpdatePatch.SetSpeed(TimeSpeed.Paused);
            }

            if (action == ServerAction.UNPAUSE)
            {
                TickUpdatePatch.SetSpeed(TimeSpeed.Normal);
            }

            if (action == ServerAction.PAWN_JOB)
            {
                ExecuteJob(actionReq);
            }

            if (action == ServerAction.SPAWN_THING)
            {
                GenSpawnPatch.spawningThing = true;

                GenSpawnPatch.Info info = ScribeUtil.ReadSingle<GenSpawnPatch.Info>(actionReq.data);
                if (info.map != null)
                {
                    GenSpawn.Spawn(info.thing, info.loc, info.map, info.rot);
                    Log.Message("action spawned thing: " + info.thing);
                }

                GenSpawnPatch.spawningThing = false;
            }

            if (action == ServerAction.MAP_ID_BLOCK)
            {
                IdBlock block = ScribeUtil.ReadSingle<IdBlock>(actionReq.data);
                Map map = Find.WorldObjects.MapParentAt(block.mapTile)?.Map;
                if (map != null)
                    map.GetComponent<ServerModMapComp>().encounterIdBlock = block;
            }

            if (action == ServerAction.AREA)
            {
                int subAction = data.ReadInt();
                string mapId = data.ReadString();
                string factionId = data.ReadString();
                Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
                if (map == null) return;
                
                map.GetComponent<ServerModMapComp>().factionAreas.TryGetValue(factionId, out AreaManager areas);
                if (factionId == Faction.OfPlayer.GetUniqueLoadID())
                    areas = map.areaManager;

                if (areas != null)
                {
                    if (subAction == 0) // invert
                    {
                        AreaInvertPatch.dontHandle = true;

                        string areaId = data.ReadString();
                        areas.AllAreas.FirstOrDefault(a => a.GetUniqueLoadID() == areaId)?.Invert();

                        AreaInvertPatch.dontHandle = false;
                    }
                    else if (subAction == 1) // update
                    {
                        string areaId = data.ReadString();
                        Area area = areas.AllAreas.FirstOrDefault(a => a.GetUniqueLoadID() == areaId);
                        if (area != null)
                        {
                            AreaSetPatch.dontHandle = true;

                            int[] cells_off = data.ReadPrefixedInts();
                            for (int i = 0; i < cells_off.Length; i++)
                                area[cells_off[i]] = false;

                            int[] cells_on = data.ReadPrefixedInts();
                            for (int i = 0; i < cells_on.Length; i++)
                                area[cells_on[i]] = true;

                            Log.Message("Updated " + (cells_off.Length + cells_on.Length) + " area cells");

                            AreaSetPatch.dontHandle = false;
                        }
                    }
                }
            }

            Log.Message("executed a scheduled action " + action);
        }

        private static void ExecuteJob(ScheduledServerAction actionReq)
        {
            JobRequest jobReq = ScribeUtil.ReadSingle<JobRequest>(actionReq.data);
            Job job = jobReq.job;

            Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == jobReq.mapId);
            if (map == null) return;

            Pawn pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == jobReq.pawnId);
            if (pawn == null) return;

            JobTrackerPatch.addingJob = true;

            // adjust for state loss
            if (!JobTrackerPatch.IsPawnOwner(pawn))
            {
                job.ignoreDesignations = true;
                job.ignoreForbidden = true;

                if (job.haulMode == HaulMode.ToCellStorage)
                    job.haulMode = HaulMode.ToCellNonStorage;
            }

            if (!JobTrackerPatch.IsPawnOwner(pawn) || pawn.jobs.curJob.expiryInterval == -2)
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                PawnTempData.Get(pawn).actualJob = null;
                PawnTempData.Get(pawn).actualJobDriver = null;
                Log.Message(ServerMod.client.username + " executed job " + job + " for " + pawn);
            }
            else
            {
                Log.Warning("Jobs don't match! p:" + pawn + " j:" + job + " cur:" + pawn.jobs.curJob + " driver:" + pawn.jobs.curDriver);
            }

            JobTrackerPatch.addingJob = false;
        }

        private static void AddLongAction(LongAction action)
        {
            if (action.type == LongActionType.PLAYER_JOIN)
                action.shouldRun = ServerMod.server != null;

            longActions.Add(action);
        }

        public static string GetActionsText()
        {
            int i = longActions.Count;
            StringBuilder str = new StringBuilder();

            if (currentLongAction != null)
            {
                str.Append(currentLongAction.Text).Append("...");
                if (i > 0)
                    str.Append("\n\n");
            }

            foreach (LongAction pause in longActions)
            {
                str.Append(pause.Text);
                if (--i > 0)
                    str.Append("\n");
            }
            return str.ToString();
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.DoSingleTick))]
    public static class TickPatch
    {
        static void Postfix()
        {
            while (OnMainThread.longActionRelated.Count > 0 && OnMainThread.longActionRelated.Peek().ticks == Find.TickManager.TicksGame)
                OnMainThread.ExecuteLongActionRelated(OnMainThread.longActionRelated.Dequeue());

            while (OnMainThread.scheduledActions.Count > 0 && OnMainThread.scheduledActions.Peek().ticks == Find.TickManager.TicksGame && Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
            {
                ScheduledServerAction action = OnMainThread.scheduledActions.Dequeue();
                OnMainThread.ExecuteServerAction(action, new ByteReader(action.data));
            }
        }
    }

    public abstract class LongAction : AttributedExposable
    {
        public LongActionType type;

        public bool shouldRun = ServerMod.server != null;

        public virtual string Text => "Waiting";

        public abstract void Run();

        public virtual bool Equals(LongAction other)
        {
            if (other == null || GetType() != other.GetType()) return false;
            return type == other.type;
        }
    }

    public class LongActionPlayerJoin : LongAction
    {
        private static readonly MethodInfo exposeSmallComps = typeof(Game).GetMethod("ExposeSmallComponents", BindingFlags.NonPublic | BindingFlags.Instance);

        [ExposeValue]
        public string username;

        public override string Text
        {
            get
            {
                if (shouldRun) return "Saving the world for " + username;
                else return "Waiting for " + username + " to load";
            }
        }

        public LongActionPlayerJoin()
        {
            type = LongActionType.PLAYER_JOIN;
        }

        public override void Run()
        {
            Connection conn = ServerMod.server.GetByUsername(username);

            // catch them up
            conn.Send(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, ScribeUtil.WriteSingle(OnMainThread.currentLongAction)));
            foreach (LongAction other in OnMainThread.longActions)
                conn.Send(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, ScribeUtil.WriteSingle(other)));
            foreach (ScheduledServerAction action in OnMainThread.scheduledActions)
                conn.Send(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(action.action, action.data));

            ServerModWorldComp factions = Find.World.GetComponent<ServerModWorldComp>();
            if (!factions.playerFactions.TryGetValue(username, out Faction faction))
            {
                faction = FactionGenerator.NewGeneratedFaction(FactionDefOf.PlayerColony);
                faction.Name = username + "'s faction";
                faction.def = FactionDefOf.Outlander;

                Find.FactionManager.Add(faction);
                factions.playerFactions[username] = faction;

                ServerMod.server.SendToAll(Packets.SERVER_NEW_FACTIONS, ScribeUtil.WriteSingle(new FactionData(username, faction)), conn, ServerMod.localServerConnection);

                Log.Message("New faction: " + faction.Name);
            }

            conn.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteSingle(ServerMod.NextIdBlock()));

            ScribeUtil.StartWriting();

            Scribe.EnterNode("savegame");
            ScribeMetaHeaderUtility.WriteMetaHeader();
            Scribe.EnterNode("game");
            sbyte visibleMapIndex = -1;
            Scribe_Values.Look<sbyte>(ref visibleMapIndex, "visibleMapIndex", -1, false);
            exposeSmallComps.Invoke(Current.Game, null);
            World world = Current.Game.World;
            Scribe_Deep.Look(ref world, "world");
            List<Map> maps = new List<Map>();
            Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
            Scribe.ExitNode();

            ServerMod.savedWorld = ScribeUtil.FinishWriting();

            (conn.GetState() as ServerWorldState).SendData();
        }

        public override bool Equals(LongAction other)
        {
            return base.Equals(other) && username == ((LongActionPlayerJoin)other).username;
        }
    }

    public class FactionData : AttributedExposable
    {
        [ExposeValue]
        public string owner;
        [ExposeDeep]
        public Faction faction;

        public FactionData() { }

        public FactionData(string owner, Faction faction)
        {
            this.owner = owner;
            this.faction = faction;
        }
    }

    public class LongActionEncounter : LongAction
    {
        [ExposeValue]
        public string defender;
        [ExposeValue]
        public string attacker;
        [ExposeValue]
        public int tile;

        public override string Text
        {
            get
            {
                return "Setting up an encounter between " + defender + " and " + attacker;
            }
        }

        public LongActionEncounter()
        {
            type = LongActionType.ENCOUNTER;
        }

        public override void Run()
        {
            Connection conn = ServerMod.server.GetByUsername(defender);
            IdBlock block = ServerMod.NextIdBlock();
            block.mapTile = tile;

            conn.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteSingle(block));
            conn.Send(Packets.SERVER_MAP_REQUEST, new object[] { tile });
        }

        public override bool Equals(LongAction other)
        {
            return base.Equals(other) && defender == ((LongActionEncounter)other).defender && tile == ((LongActionEncounter)other).tile;
        }
    }

    public class LongActionGenerating : LongAction
    {
        [ExposeValue]
        public string username;

        public override string Text => username + " is generating a map";

        public LongActionGenerating()
        {
            type = LongActionType.GENERATING_MAP;
        }

        public override void Run()
        {
        }

        public override bool Equals(LongAction other)
        {
            return base.Equals(other) && username == ((LongActionGenerating)other).username;
        }
    }

    public enum LongActionType
    {
        PLAYER_JOIN, ENCOUNTER, GENERATING_MAP
    }

    public class ServerModWorldComp : WorldComponent
    {
        public string worldId = Guid.NewGuid().ToString();
        public Dictionary<string, Faction> playerFactions = new Dictionary<string, Faction>();

        private List<string> keyWorkingList;
        private List<Faction> valueWorkingList;

        public ServerModWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            ScribeUtil.Look(ref playerFactions, "playerFactions", LookMode.Value, LookMode.Reference, ref keyWorkingList, ref valueWorkingList);
            Scribe_Values.Look(ref ServerMod.highestUniqueId, "highestUniqueId");
        }

        public string GetUsername(Faction faction)
        {
            return playerFactions.FirstOrDefault(pair => pair.Value == faction).Key;
        }

    }

    public class ServerModMapComp : MapComponent
    {
        public IdBlock encounterIdBlock;

        public Dictionary<string, SlotGroupManager> factionSlotGroups = new Dictionary<string, SlotGroupManager>();
        public Dictionary<string, ZoneManager> factionZones = new Dictionary<string, ZoneManager>();
        public Dictionary<string, AreaManager> factionAreas = new Dictionary<string, AreaManager>();

        // zone id => [cells off, cells on]
        public Dictionary<string, HashSet<int>[]> areaChangesThisTick = new Dictionary<string, HashSet<int>[]>();

        public ServerModMapComp(Map map) : base(map)
        {
        }

        public void AreaChange(string areaId, int cell, bool value)
        {
            if (!areaChangesThisTick.TryGetValue(areaId, out HashSet<int>[] changes))
            {
                changes = new HashSet<int>[] { new HashSet<int>(), new HashSet<int>() };
                areaChangesThisTick.Add(areaId, changes);
            }

            if (!changes[value ? 0 : 1].Remove(cell))
                changes[value ? 1 : 0].Add(cell);
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref encounterIdBlock, "encounterIdBlock");
        }
    }

    public class PawnTempData : ThingComp
    {
        public Job actualJob;
        public JobDriver actualJobDriver;

        private bool homeThisTick;

        public override string CompInspectStringExtra()
        {
            return ("Actual job: " + actualJob + "\nActual job driver: " + actualJobDriver + "\nAt home: " + homeThisTick).Trim();
        }

        public override void CompTick()
        {
            homeThisTick = parent.Map.areaManager.Home[parent.Position];
        }

        public static PawnTempData Get(Pawn pawn)
        {
            PawnTempData data = pawn.GetComp<PawnTempData>();
            if (data == null)
            {
                data = new PawnTempData();
                data.parent = pawn;
                pawn.AllComps.Add(data);
            }

            return data;
        }
    }

}

