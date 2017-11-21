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
        public static bool savingWorld;
        public static List<string> sendToQueue = new List<string>();
        public static HashSet<string> sentToWaiting = new HashSet<string>();
        // player owner => faction
        public static Dictionary<string, Faction> newFactions = new Dictionary<string, Faction>();
        public static byte[] clientFaction;
        public static byte[] mapsData;
        public static bool savingForEncounter;
        public static AutoResetEvent pause = new AutoResetEvent(false);

        public static Queue<ScheduledServerAction> actions = new Queue<ScheduledServerAction>();

        public const string WORLD_DOWNLOAD = "Waiting for players to load";

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
            else if (GenCommandLine.CommandLineArgPassed("connect"))
            {
                DebugSettings.noAnimals = true;
                LongEventHandler.QueueLongEvent(() =>
                {
                    IPAddress.TryParse("127.0.0.1", out IPAddress addr);
                    Client.TryConnect(addr, ServerMod.DEFAULT_PORT, (conn, e) =>
                    {
                        if (e != null)
                        {
                            ServerMod.client = null;
                            return;
                        }

                        ServerMod.client = conn;
                        conn.username = ServerMod.username;
                        conn.State = new ClientWorldState(conn);
                    });
                }, "Connecting", false, null);
            }
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

        public readonly string defender;
        public List<string> attackers = new List<string>();
        public Queue<string> waitingForMap = new Queue<string>();

        private Encounter(string defender)
        {
            this.defender = defender;
        }

        public static void Add(string defender, string attacker)
        {
            if (GetByDefender(defender) != null) return;

            encounters.Add(new Encounter(defender));
        }

        public static Encounter GetByDefender(string username)
        {
            return encounters.FirstOrDefault(e => e.defender == username);
        }
    }

    public static class Packets
    {
        public const int CLIENT_REQUEST_WORLD = 0;
        public const int CLIENT_WORLD_FINISHED = 1;
        public const int CLIENT_ACTION_REQUEST = 2;
        public const int CLIENT_USERNAME = 3;
        public const int CLIENT_NEW_WORLD_OBJ = 4;
        public const int CLIENT_SAVE_MAP = 5;
        public const int CLIENT_ENCOUNTER_REQUEST = 6;
        public const int CLIENT_ENCOUNTER_MAP = 7;

        public const int SERVER_WORLD_DATA = 0;
        public const int SERVER_ACTION_SCHEDULE = 1;
        public const int SERVER_NEW_FACTIONS = 2;
        public const int SERVER_NEW_WORLD_OBJ = 3;
        public const int SERVER_ENCOUNTER_REQUEST = 4;
        public const int SERVER_ENCOUNTER_MAP = 5;
        public const int SERVER_NOTIFICATION = 6;
    }

    public enum ServerAction : int
    {
        PAUSE, UNPAUSE, JOB, SPAWN_THING, HARD_PAUSE, HARD_UNPAUSE
    }

    public struct ScheduledServerAction
    {
        public readonly int ticks;
        public readonly ServerAction action;
        public readonly object extra;

        public ScheduledServerAction(int ticks, ServerAction action, object extra)
        {
            this.ticks = ticks;
            this.action = action;
            this.extra = extra;
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

        public static byte[] GetBytes(this ServerAction action)
        {
            return BitConverter.GetBytes((int)action);
        }

        public static byte[] GetBytes(this string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        public static void Write(this MemoryStream stream, byte[] arr)
        {
            stream.Write(arr, 0, arr.Length);
        }

        public static byte[] ReadBytes(this MemoryStream stream)
        {
            return stream.ReadBytes(stream.ReadInt());
        }

        public static byte[] ReadBytes(this MemoryStream stream, int len)
        {
            byte[] arr = new byte[len];
            stream.Read(arr, 0, arr.Length);
            return arr;
        }

        public static int ReadInt(this MemoryStream stream)
        {
            return BitConverter.ToInt32(stream.ReadBytes(4), 0);
        }

        public static string ReadString(this MemoryStream stream)
        {
            return Encoding.UTF8.GetString(stream.ReadBytes());
        }
    }

    public class LocalClientConnection : Connection
    {
        public LocalServerConnection server;

        public LocalClientConnection() : base(null)
        {
        }

        public override void Send(int id, byte[] msg = null)
        {
            msg = msg ?? new byte[] { 0 };
            server.State?.Message(id, msg);
        }

        public override void Close()
        {
            connectionClosed();
            server.connectionClosed();
        }

        public override string ToString()
        {
            return "Local";
        }
    }

    public class LocalServerConnection : Connection
    {
        public LocalClientConnection client;

        public LocalServerConnection() : base(null)
        {
        }

        public override void Send(int id, byte[] msg = null)
        {
            msg = msg ?? new byte[] { 0 };
            client.State?.Message(id, msg);
        }

        public override void Close()
        {
            connectionClosed();
            client.connectionClosed();
        }

        public override string ToString()
        {
            return "Local";
        }
    }

    public class CountdownLock
    {
        public AutoResetEvent eventObj = new AutoResetEvent(false);
        private HashSet<object> ids = new HashSet<object>();

        public void Add(object id)
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

        public bool Done(object id)
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
    }

    public class ServerWorldState : ConnectionState
    {
        public ServerWorldState(Connection connection) : base(connection)
        {
        }

        public override void Message(int id, byte[] data)
        {
            if (id == Packets.CLIENT_REQUEST_WORLD)
            {
                OnMainThread.Queue(() =>
                {
                    if (ServerMod.savedWorld == null)
                    {
                        if (!ServerMod.savingWorld)
                        {
                            ServerMod.savingWorld = true;
                            ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.HARD_PAUSE, ServerMod.WORLD_DOWNLOAD));
                        }

                        ServerMod.sendToQueue.Add(Connection.username);
                    }
                    else
                    {
                        SendData();
                    }
                });
            }
            else if (id == Packets.CLIENT_WORLD_FINISHED)
            {
                OnMainThread.Queue(() =>
                {
                    this.Connection.State = new ServerPlayingState(this.Connection);

                    if (ServerMod.sentToWaiting.Remove(Connection.username) && ServerMod.sentToWaiting.Count == 0)
                    {
                        ServerMod.savedWorld = null;

                        ScribeUtil.StartWriting();
                        Scribe.EnterNode("data");
                        ScribeUtil.Look(ref ServerMod.newFactions, "newFactions", LookMode.Value, LookMode.Deep);
                        byte[] factionData = ScribeUtil.FinishWriting();

                        ServerMod.server.SendToAll(Packets.SERVER_NEW_FACTIONS, factionData, ServerMod.localServerConnection);
                        ServerMod.newFactions.Clear();

                        ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.HARD_UNPAUSE, ServerMod.WORLD_DOWNLOAD));

                        Log.Message("world sending finished");
                    }
                });
            }
            else if (id == Packets.CLIENT_USERNAME)
            {
                OnMainThread.Queue(() => Connection.username = Encoding.UTF8.GetString(data));
            }
        }

        public void SendData()
        {
            ServerModWorldComp factions = Find.World.GetComponent<ServerModWorldComp>();
            if (!factions.playerFactions.TryGetValue(Connection.username, out Faction faction))
            {
                faction = FactionGenerator.NewGeneratedFaction(FactionDefOf.PlayerColony);
                faction.Name = Connection.username + "'s faction";
                faction.def = FactionDefOf.Outlander;
                Find.FactionManager.Add(faction);
                factions.playerFactions[Connection.username] = faction;
                ServerMod.newFactions[Connection.username] = faction;

                Connection.Send(Packets.SERVER_NEW_FACTIONS, ScribeUtil.WriteSingle(faction));

                Log.Message("New faction: " + faction.Name);
            }

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

            ServerMod.sentToWaiting.Add(Connection.username);
            Connection.Send(Packets.SERVER_WORLD_DATA, new object[] { ServerMod.savedWorld.Length, ServerMod.savedWorld, mapsData.Length, mapsData });
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

        public override void Message(int id, byte[] data)
        {
            if (id == Packets.CLIENT_ACTION_REQUEST)
            {
                OnMainThread.Queue(() =>
                {
                    ServerAction action = (ServerAction)BitConverter.ToInt32(data, 0);
                    int ticks = Find.TickManager.TicksGame + 15;
                    byte[] extra = data.SubArray(4, data.Length - 4);

                    ServerMod.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(action, extra));

                    Log.Message("server got request from client at " + Find.TickManager.TicksGame + " for " + action + " " + ticks);
                });
            }
            else if (id == Packets.CLIENT_NEW_WORLD_OBJ)
            {
                ServerMod.server.SendToAll(Packets.SERVER_NEW_WORLD_OBJ, data, this.Connection);
            }
            else if (id == Packets.CLIENT_SAVE_MAP)
            {
                OnMainThread.Queue(() =>
                {
                    try
                    {
                        using (MemoryStream stream = new MemoryStream(data))
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
                });
            }
            else if (id == Packets.CLIENT_ENCOUNTER_REQUEST)
            {
                OnMainThread.Queue(() =>
                {
                    int tile = BitConverter.ToInt32(data, 0);
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

                    Encounter.Add(defender, Connection.username);
                    conn.Send(Packets.SERVER_ENCOUNTER_REQUEST, new object[] { tile });
                });
            }
            else if (id == Packets.CLIENT_ENCOUNTER_MAP)
            {
                OnMainThread.Queue(() =>
                {
                    /*if (Encounter.GetByDefender(Connection.username))
                    {
                        Connection conn = ServerMod.server.GetByUsername(attacker);
                        if (conn == null) return;
                        conn.Send(Packets.SERVER_ENCOUNTER_MAP, data);
                    }*/
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

        public static object[] GetServerActionMsg(ServerAction action, object extra = null)
        {
            return new object[] { action, Find.TickManager.TicksGame + 15, extra };
        }
    }

    public class ClientWorldState : ConnectionState
    {
        public ClientWorldState(Connection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, Encoding.ASCII.GetBytes(ServerMod.username));
            connection.Send(Packets.CLIENT_REQUEST_WORLD);
        }

        public override void Message(int id, byte[] data)
        {
            if (id == Packets.SERVER_WORLD_DATA)
            {
                OnMainThread.Queue(() =>
                {
                    int worldLen = BitConverter.ToInt32(data, 0);
                    ServerMod.savedWorld = data.SubArray(4, worldLen);
                    int mapsLen = BitConverter.ToInt32(data, worldLen + 4);
                    ServerMod.mapsData = data.SubArray(worldLen + 8, mapsLen);

                    Log.Message("World size: " + worldLen + ", Maps size: " + mapsLen);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        MemoryUtility.ClearAllMapsAndWorld();
                        Current.Game = new Game();
                        Current.Game.InitData = new GameInitData();
                        Current.Game.InitData.gameToLoad = "server";
                    }, "Play", "LoadingLongEvent", true, null);
                });
            }
            else if (id == Packets.SERVER_NEW_FACTIONS)
            {
                OnMainThread.Queue(() =>
                {
                    ServerMod.clientFaction = data;
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

        public override void Message(int id, byte[] data)
        {
            if (id == Packets.SERVER_ACTION_SCHEDULE)
            {
                OnMainThread.Queue(() =>
                {
                    ServerAction action = (ServerAction)BitConverter.ToInt32(data, 0);
                    int ticks = BitConverter.ToInt32(data, 4);
                    byte[] extraBytes = data.SubArray(8, data.Length - 8);
                    object extraObj = null;

                    if (action == ServerAction.JOB)
                    {
                        extraObj = ScribeUtil.ReadSingle<JobRequest>(extraBytes);
                    }
                    else if (action == ServerAction.HARD_PAUSE || action == ServerAction.HARD_UNPAUSE)
                    {
                        extraObj = Encoding.UTF8.GetString(extraBytes);
                    }

                    ScheduledServerAction schdl = new ScheduledServerAction(ticks, action, extraObj);
                    ServerMod.actions.Enqueue(schdl);

                    Log.Message("client got request from server at " + Find.TickManager.TicksGame + " for action " + schdl.action + " " + schdl.ticks);
                });
            }
            else if (id == Packets.SERVER_NEW_FACTIONS)
            {
                OnMainThread.Queue(() =>
                {
                    ScribeUtil.StartLoading(data);
                    ScribeUtil.SupplyCrossRefs();
                    ScribeUtil.Look(ref ServerMod.newFactions, "newFactions", LookMode.Value, LookMode.Deep);
                    ScribeUtil.FinishLoading();

                    foreach (KeyValuePair<string, Faction> pair in ServerMod.newFactions)
                    {
                        // our own faction has already been sent
                        if (pair.Key == Connection.username)
                        {
                            Log.Message("skip");
                            continue;
                        }

                        Find.FactionManager.Add(pair.Value);
                        Find.World.GetComponent<ServerModWorldComp>().playerFactions[pair.Key] = pair.Value;
                    }

                    Log.Message("Got " + ServerMod.newFactions.Count + " new factions");

                    ServerMod.newFactions.Clear();
                });
            }
            else if (id == Packets.SERVER_NEW_WORLD_OBJ)
            {
                OnMainThread.Queue(() =>
                {
                    ScribeUtil.StartLoading(data);
                    ScribeUtil.SupplyCrossRefs();
                    WorldObject obj = null;
                    Scribe_Deep.Look(ref obj, "worldObj");
                    ScribeUtil.FinishLoading();
                    Find.WorldObjects.Add(obj);
                });
            }
            else if (id == Packets.SERVER_ENCOUNTER_REQUEST)
            {
                OnMainThread.Queue(() =>
                {
                    int tile = BitConverter.ToInt32(data, 0);
                    Settlement settlement = Find.WorldObjects.SettlementAt(tile);
                    if (settlement == null || !settlement.HasMap) return;

                    ServerMod.savingForEncounter = true;
                    ScribeUtil.StartWriting();
                    Scribe.EnterNode("data");
                    Map map = settlement.Map;
                    Scribe_Deep.Look(ref map, "map");
                    byte[] mapData = ScribeUtil.FinishWriting();
                    ServerMod.savingForEncounter = false;

                    ServerMod.client.Send(Packets.CLIENT_ENCOUNTER_MAP, mapData);
                });
            }
            else if (id == Packets.SERVER_ENCOUNTER_MAP)
            {
                OnMainThread.Queue(() =>
                {
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        Current.ProgramState = ProgramState.MapInitializing;

                        Log.Message("Encounter map size: " + data.Length);

                        ScribeUtil.StartLoading(data);
                        ScribeUtil.SupplyCrossRefs();
                        Map map = null;
                        Scribe_Deep.Look(ref map, "map");
                        ScribeUtil.FinishLoading();

                        Current.Game.AddMap(map);
                        map.FinalizeLoading();

                        Current.ProgramState = ProgramState.Playing;
                    }, "Loading encounter map", false, null);
                });
            }
        }

        public override void Disconnect()
        {
        }

        // Currently covers:
        // - settling after joining
        public static void SyncClientWorldObj(WorldObject obj)
        {
            ScribeUtil.StartWriting();
            Scribe.EnterNode("data");
            Scribe_Deep.Look(ref obj, "worldObj");
            byte[] data = ScribeUtil.FinishWriting();
            ServerMod.client.Send(Packets.CLIENT_NEW_WORLD_OBJ, data);
        }
    }

    public class OnMainThread : MonoBehaviour
    {
        private static readonly Queue<Action> queue = new Queue<Action>();

        public void Update()
        {
            lock (queue)
                while (queue.Count > 0)
                    queue.Dequeue().Invoke();

            if (Current.Game != null)
                // when paused, execute immediately
                while (ServerMod.actions.Count > 0 && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                    ExecuteServerAction(ServerMod.actions.Dequeue());
        }

        public static void Queue(Action action)
        {
            lock (queue)
                queue.Enqueue(action);
        }

        public static void ExecuteServerAction(ScheduledServerAction actionReq)
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

            if (action == ServerAction.JOB)
            {
                JobRequest jobReq = (JobRequest)actionReq.extra;
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
                    Log.Message("executed job " + job + " for " + pawn);
                }
                else
                {
                    Log.Warning("Jobs don't match! p:" + pawn + " j:" + job + " cur:" + pawn.jobs.curJob + " driver:" + pawn.jobs.curDriver);
                }

                JobTrackerPatch.addingJob = false;
            }

            if (action == ServerAction.HARD_PAUSE)
            {
                TickUpdatePatch.SetSpeed(TimeSpeed.Paused);
                string text = (string)actionReq.extra;

                if (text == ServerMod.WORLD_DOWNLOAD && ServerMod.server != null)
                {
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        IncrIds(); // an id block for the joining player

                        ScribeUtil.StartWriting();

                        Scribe.EnterNode("savegame");
                        ScribeMetaHeaderUtility.WriteMetaHeader();
                        Scribe.EnterNode("game");
                        sbyte visibleMapIndex = -1;
                        Scribe_Values.Look<sbyte>(ref visibleMapIndex, "visibleMapIndex", -1, false);
                        typeof(Game).GetMethod("ExposeSmallComponents", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Current.Game, null);
                        World world = Current.Game.World;
                        Scribe_Deep.Look(ref world, "world");
                        List<Map> maps = new List<Map>();
                        Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
                        Scribe.ExitNode();

                        ServerMod.savedWorld = ScribeUtil.FinishWriting();
                        ServerMod.savingWorld = false;

                        IncrIds(); // an id block for us

                        foreach (string s in ServerMod.sendToQueue)
                            (ServerMod.server.GetByUsername(s)?.State as ServerWorldState)?.SendData();

                        ServerMod.sendToQueue.Clear();
                    }, "Saving world for incoming players", false, null);
                }

                LongEventHandler.QueueLongEvent(() =>
                {
                    ServerMod.pause.WaitOne();
                }, text, true, null);
            }

            if (action == ServerAction.HARD_UNPAUSE)
            {
                ServerMod.pause.Set();
            }

            Log.Message("executed a scheduled action " + action);
        }

        // todo temporary
        public static void IncrIds()
        {
            foreach (FieldInfo f in typeof(UniqueIDsManager).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(int)) continue;

                int curr = (int)f.GetValue(Find.UniqueIDsManager);
                f.SetValue(Find.UniqueIDsManager, curr + 40000);

                Log.Message("field " + curr);
            }
        }
    }

    public class ServerModWorldComp : WorldComponent
    {
        public Dictionary<string, Faction> playerFactions = new Dictionary<string, Faction>();
        public string worldId = Guid.NewGuid().ToString();

        private List<string> keyWorkingList;
        private List<Faction> valueWorkingList;

        public ServerModWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            ScribeUtil.Look(ref playerFactions, "playerFactions", LookMode.Value, LookMode.Reference, ref keyWorkingList, ref valueWorkingList);
            Scribe_Values.Look(ref worldId, "worldId", null);
        }

        public string GetUsername(Faction faction)
        {
            return playerFactions.FirstOrDefault(pair => pair.Value == faction).Key;
        }

    }

    public class PawnTempData : ThingComp
    {
        public Job actualJob;
        public JobDriver actualJobDriver;

        public static PawnTempData Get(Pawn pawn)
        {
            PawnTempData data = pawn.GetComp<PawnTempData>();
            if (data == null)
            {
                data = new PawnTempData();
                pawn.AllComps.Add(data);
            }

            return data;
        }
    }

}

