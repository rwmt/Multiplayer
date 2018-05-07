using Harmony;
using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Verse.Sound;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class Multiplayer
    {
        public static String username;
        public static MultiplayerServer localServer;
        public static IConnection client;
        public static NetManager netClient;
        public static ChatWindow chat = new ChatWindow();
        public static PacketLogWindow packetLog = new PacketLogWindow();
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");

        public static bool loadingEncounter;

        public static IdBlock globalIdBlock;

        public static MultiplayerWorldComp WorldComp => Find.World.GetComponent<MultiplayerWorldComp>();
        public static Faction RealPlayerFaction => client != null ? WorldComp.playerFactions[username] : Faction.OfPlayer;

        public static FactionDef factionDef = FactionDef.Named("MultiplayerColony");

        public static int Seed
        {
            set
            {
                RandSetSeedPatch.dontLog = true;

                Rand.Seed = value;
                UnityEngine.Random.InitState(value);

                RandSetSeedPatch.dontLog = false;
            }
        }

        public static bool Ticking => TickPatch.tickingWorld || MapAsyncTimeComp.tickingMap;
        public static bool ShouldSync => client != null && !Ticking && !OnMainThread.executingCmds;

        static Multiplayer()
        {
            MpLog.info = str => Log.Message(username + " " + str);

            GenCommandLine.TryGetCommandLineArg("username", out username);
            if (username == null)
                username = SteamUtility.SteamPersonaName;
            if (username == "???")
                username = "Player" + Rand.Range(0, 9999);

            SimpleProfiler.Init(username);

            Log.Message("Player's username: " + username);
            Log.Message("Processor: " + SystemInfo.processorType);

            var gameobject = new GameObject();
            gameobject.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(gameobject);

            MultiplayerConnectionState.RegisterState(typeof(ClientWorldState));
            MultiplayerConnectionState.RegisterState(typeof(ClientPlayingState));

            MpPatch.DoPatches(typeof(MethodMarkers));
            MpPatch.DoPatches(typeof(SyncPatches));
            MpPatch.DoPatches(typeof(SyncDelegates));

            RuntimeHelpers.RunClassConstructor(typeof(SyncPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncFieldsPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncDelegates).TypeHandle);

            Sync.RegisterFieldPatches(typeof(SyncFieldsPatches));
            Sync.RegisterSyncDelegates(typeof(SyncDelegates));

            DoPatches();

            LogMessageQueue log = (LogMessageQueue)typeof(Log).GetField("messageQueue", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            log.maxMessages = 500;

            if (GenCommandLine.CommandLineArgPassed("dev"))
            {
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData
                {
                    gameToLoad = "mappo"
                };

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
                    Client.TryConnect(addr, MultiplayerServer.DEFAULT_PORT, conn =>
                    {
                        MpLog.Log("Client connected");

                        client = conn;
                        conn.Username = username;
                        conn.State = new ClientWorldState(conn);
                    }, exception =>
                    {
                        client = null;
                    });
                }, "Connecting", false, null);
            }
        }

        public static XmlDocument SaveGame()
        {
            bool betterSave = SaveCompression.doBetterSave;
            SaveCompression.doBetterSave = true;

            ScribeUtil.StartWritingToDoc();

            Scribe.EnterNode("savegame");
            ScribeMetaHeaderUtility.WriteMetaHeader();
            Scribe.EnterNode("game");
            sbyte visibleMapIndex = Current.Game.visibleMapIndex;
            Scribe_Values.Look(ref visibleMapIndex, "visibleMapIndex", (sbyte)-1);
            Current.Game.ExposeSmallComponents();
            World world = Current.Game.World;
            Scribe_Deep.Look(ref world, "world");
            List<Map> maps = Find.Maps;
            Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
            Find.CameraDriver.Expose();
            Scribe.ExitNode();

            SaveCompression.doBetterSave = betterSave;

            return ScribeUtil.FinishWritingToDoc();
        }

        public static void SendGameData(XmlDocument doc, bool sendGame = true)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            string nonPlayerMaps = "li[components/li/@Class='Multiplayer.Client.MultiplayerMapComp' and components/li/isPlayerHome='False']";
            mapsNode.SelectAndRemove(nonPlayerMaps);

            byte[] mapData = ScribeUtil.XmlToByteArray(mapsNode, "data", true);
            File.WriteAllBytes("map_" + username + ".xml", mapData);

            byte[] compressedMaps = GZipStream.CompressBuffer(mapData);
            // todo send map id
            Multiplayer.client.Send(Packets.CLIENT_AUTOSAVED_DATA, false, 0, compressedMaps);

            if (sendGame)
            {
                gameNode["visibleMapIndex"].RemoveFromParent();
                mapsNode.RemoveAll();

                byte[] gameData = ScribeUtil.XmlToByteArray(doc, null, true);
                File.WriteAllBytes("game.xml", gameData);

                byte[] compressedGame = GZipStream.CompressBuffer(gameData);
                Multiplayer.client.Send(Packets.CLIENT_AUTOSAVED_DATA, true, compressedGame);
            }
        }

        static readonly FieldInfo paramName = AccessTools.Field(typeof(ParameterInfo), "NameImpl");

        private static void DoPatches()
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // General designation handling
            {
                var designatorMethods = new[] { "DesignateSingleCell", "DesignateMultiCell", "DesignateThing" };

                foreach (Type t in typeof(Designator).AllSubtypesAndSelf())
                {
                    foreach (string m in designatorMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (method == null) continue;

                        MethodInfo prefix = AccessTools.Method(typeof(DesignatorPatches), m);
                        harmony.Patch(method, new HarmonyMethod(prefix), null, null);
                    }
                }
            }

            // Remove side effects from methods which are non-deterministic during ticking (e.g. camera dependent motes and sound effects)
            {
                var randPatchPrefix = new HarmonyMethod(typeof(RandPatches).GetMethod("Prefix"));
                var randPatchPostfix = new HarmonyMethod(typeof(RandPatches).GetMethod("Postfix"));

                var subSustainerCtor = typeof(SubSustainer).GetConstructor(new Type[] { typeof(Sustainer), typeof(SubSoundDef) });
                var subSoundPlay = typeof(SubSoundDef).GetMethod("TryPlay");
                var effecterTick = typeof(Effecter).GetMethod("EffectTick");
                var effecterTrigger = typeof(Effecter).GetMethod("Trigger");
                var effectMethods = new MethodBase[] { subSustainerCtor, subSoundPlay, effecterTick, effecterTrigger };
                var moteMethods = typeof(MoteMaker).GetMethods(BindingFlags.Static | BindingFlags.Public);

                foreach (MethodBase m in effectMethods.Concat(moteMethods))
                    harmony.Patch(m, randPatchPrefix, randPatchPostfix);
            }

            // Set Rand.Seed, ThingContext and FactionContext (for pawns) in common Thing methods
            {
                var thingMethodPrefix = new HarmonyMethod(typeof(PatchThingMethods).GetMethod("Prefix"));
                var thingMethodPostfix = new HarmonyMethod(typeof(PatchThingMethods).GetMethod("Postfix"));
                var thingMethods = new[] { "Tick", "TickRare", "TickLong", "SpawnSetup" };

                foreach (Type t in typeof(Thing).AllSubtypesAndSelf())
                {
                    foreach (string m in thingMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (method != null)
                            harmony.Patch(method, thingMethodPrefix, thingMethodPostfix);
                    }
                }
            }

            // Full precision floating point saving (not really needed?)
            {
                var doubleSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.DoubleSave_Prefix)));
                var floatSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.FloatSave_Prefix)));
                var valueSaveMethod = typeof(Scribe_Values).GetMethod(nameof(Scribe_Values.Look));

                harmony.Patch(valueSaveMethod.MakeGenericMethod(typeof(double)), doubleSavePrefix, null);
                harmony.Patch(valueSaveMethod.MakeGenericMethod(typeof(float)), floatSavePrefix, null);
            }

            // Set the map time for GUI methods depending on it
            {
                var setMapTimePrefix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTimeForUI), "Prefix"));
                var setMapTimePostfix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTimeForUI), "Postfix"));
                var setMapTime = new[] { "MapInterfaceOnGUI_BeforeMainTabs", "MapInterfaceOnGUI_AfterMainTabs", "HandleMapClicks", "HandleLowPriorityInput", "MapInterfaceUpdate" };

                foreach (string m in setMapTime)
                    harmony.Patch(AccessTools.Method(typeof(MapInterface), m), setMapTimePrefix, setMapTimePostfix);
                harmony.Patch(AccessTools.Method(typeof(SoundRoot), "Update"), setMapTimePrefix, setMapTimePostfix);
            }

            {
                var worldGridCtor = AccessTools.Constructor(typeof(WorldGrid));
                harmony.Patch(worldGridCtor, new HarmonyMethod(AccessTools.Method(typeof(WorldGridCtorPatch), "Prefix")), null);

                var worldRendererCtor = AccessTools.Constructor(typeof(WorldRenderer));
                harmony.Patch(worldRendererCtor, new HarmonyMethod(AccessTools.Method(typeof(WorldRendererCtorPatch), "Prefix")), null);
            }
        }

        public static void ExposeIdBlock(ref IdBlock block, string label)
        {
            if (Scribe.mode == LoadSaveMode.Saving && block != null)
            {
                string base64 = Convert.ToBase64String(block.Serialize());
                Scribe_Values.Look(ref base64, label);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                string base64 = null;
                Scribe_Values.Look(ref base64, label);

                if (base64 != null)
                    block = IdBlock.Deserialize(new ByteReader(Convert.FromBase64String(base64)));
                else
                    block = null;
            }
        }
    }

    public class Container<T>
    {
        private readonly T _value;
        public T Value => _value;

        public Container(T value)
        {
            _value = value;
        }

        public static implicit operator Container<T>(T value)
        {
            return new Container<T>(value);
        }
    }

    public class Container<T, U>
    {
        private readonly T _first;
        private readonly U _second;

        public T First => _first;
        public U Second => _second;

        public Container(T first, U second)
        {
            _first = first;
            _second = second;
        }
    }

    public class MultiplayerModInstance : Mod
    {
        public MultiplayerModInstance(ModContentPack pack) : base(pack)
        {
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
        }

        public override string SettingsCategory() => "Multiplayer";
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

        public IEnumerable<string> GetPlayers()
        {
            yield return defender;
            foreach (string attacker in attackers)
                yield return attacker;
        }

        public static Encounter Add(int tile, string defender, string attacker)
        {
            Encounter e = new Encounter(tile, defender, attacker);
            encounters.Add(e);
            return e;
        }

        public static Encounter GetByTile(int tile)
        {
            return encounters.FirstOrDefault(e => e.tile == tile);
        }
    }

    public class ClientWorldState : MultiplayerConnectionState
    {
        public ClientWorldState(IConnection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, Multiplayer.username);
            connection.Send(Packets.CLIENT_REQUEST_WORLD);
        }

        [PacketHandler(Packets.SERVER_WORLD_DATA)]
        public void HandleWorldData(ByteReader data)
        {
            Connection.State = new ClientPlayingState(Connection);

            OnMainThread.Enqueue(() =>
            {
                int tickUntil = data.ReadInt32();

                int cmdsLen = data.ReadInt32();
                for (int i = 0; i < cmdsLen; i++)
                {
                    byte[] cmd = data.ReadPrefixedBytes();
                    ClientPlayingState.HandleCmdSchedule(new ByteReader(cmd));
                }

                byte[] compressedGameData = data.ReadPrefixedBytes();
                byte[] gameData = GZipStream.UncompressBuffer(compressedGameData);

                TickPatch.tickUntil = tickUntil;
                LoadPatch.gameToLoad = ScribeUtil.GetDocument(gameData);

                Log.Message("World size: " + gameData.Length + ", " + cmdsLen + " scheduled actions");

                Prefs.PauseOnLoad = false;

                LongEventHandler.QueueLongEvent(() =>
                {
                    MemoryUtility.ClearAllMapsAndWorld();
                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    Current.Game.InitData.gameToLoad = "server";

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        LongEventHandler.QueueLongEvent(() =>
                        {
                            catchupStart = TickPatch.Timer;
                            Log.Message("Catchup start " + catchupStart + " " + TickPatch.tickUntil + " " + Find.TickManager.TicksGame + " " + Find.TickManager.CurTimeSpeed);
                            CatchUp();
                            catchingUp.WaitOne();

                            Multiplayer.client.Send(Packets.CLIENT_WORLD_LOADED);

                            Multiplayer.client.Send(Packets.CLIENT_ENCOUNTER_REQUEST, Find.World.worldObjects.Settlements.First(s => Multiplayer.WorldComp.GetUsernameByFaction(s.Faction) != null).Tile);
                        }, "Loading", true, null);
                    });
                }, "Play", "Loading the game", true, null);
            });
        }

        private static int catchupStart;
        private static AutoResetEvent catchingUp = new AutoResetEvent(false);

        public static void CatchUp()
        {
            OnMainThread.Enqueue(() =>
            {
                int toSimulate = Math.Min(TickPatch.tickUntil - TickPatch.Timer, 100);
                if (toSimulate == 0)
                {
                    catchingUp.Set();
                    return;
                }

                int percent = (int)((TickPatch.Timer - catchupStart) / (float)(TickPatch.tickUntil - catchupStart) * 100);
                LongEventHandler.SetCurrentEventText("Loading (" + percent + "%)");

                int timerStart = TickPatch.Timer;
                while (TickPatch.Timer < timerStart + toSimulate)
                {
                    if (Find.TickManager.TickRateMultiplier > 0)
                    {
                        Find.TickManager.DoSingleTick();
                    }
                    else if (OnMainThread.scheduledCmds.Count > 0)
                    {
                        OnMainThread.ExecuteGlobalCmdsWhilePaused();
                    }
                    // Nothing to do, the game is currently paused
                    else
                    {
                        catchingUp.Set();
                        return;
                    }
                }

                Thread.Sleep(50);
                CatchUp();
            });
        }

        public override void Disconnected(string reason)
        {
        }
    }

    public class ClientPlayingState : MultiplayerConnectionState
    {
        public ClientPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.SERVER_COMMAND)]
        public void HandleCommand(ByteReader data)
        {
            HandleCmdSchedule(data);
        }

        [PacketHandler(Packets.SERVER_PLAYER_LIST)]
        public void HandlePlayerList(ByteReader data)
        {
            string[] players = data.ReadPrefixedStrings();
            Multiplayer.chat.playerList = players;
        }

        [PacketHandler(Packets.SERVER_CHAT)]
        public void HandleChat(ByteReader data)
        {
            string username = data.ReadString();
            string msg = data.ReadString();

            Multiplayer.chat.AddMsg(username + ": " + msg);
        }

        private int mapCatchupStart;
        private static MethodInfo ExecuteToExecuteWhenFinished = AccessTools.Method(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished");

        [PacketHandler(Packets.SERVER_MAP_RESPONSE)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();
            int cmdsLen = data.ReadInt32();
            byte[][] cmds = new byte[cmdsLen][];
            for (int i = 0; i < cmdsLen; i++)
                cmds[i] = data.ReadPrefixedBytes();

            byte[] compressedMapData = data.ReadPrefixedBytes();
            byte[] mapData = GZipStream.UncompressBuffer(compressedMapData);

            LongEventHandler.QueueLongEvent(() =>
            {
                Stopwatch time = Stopwatch.StartNew();

                Multiplayer.loadingEncounter = true;
                Current.ProgramState = ProgramState.MapInitializing;
                SaveCompression.doBetterSave = true;

                ScribeUtil.StartLoading(mapData);
                ScribeUtil.SupplyCrossRefs();
                List<Map> maps = null;
                Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
                ScribeUtil.FinalizeLoading();

                MpLog.Log("Maps " + maps.Count);
                Map map = maps[0];

                Current.Game.AddMap(map);
                map.FinalizeLoading();

                SaveCompression.doBetterSave = false;
                Multiplayer.loadingEncounter = false;
                Current.ProgramState = ProgramState.Playing;

                Find.World.renderer.wantedMode = WorldRenderMode.None;
                Current.Game.VisibleMap = map;

                // RegenerateEverythingNow
                ExecuteToExecuteWhenFinished.Invoke(null, new object[0]);

                MapAsyncTimeComp asyncTime = map.GetComponent<MapAsyncTimeComp>();

                foreach (byte[] cmd in cmds)
                    HandleCmdSchedule(new ByteReader(cmd));

                Faction ownerFaction = map.info.parent.Faction;

                mapCatchupStart = TickPatch.Timer;

                Log.Message("Loading took " + time.ElapsedMilliseconds);
                time = Stopwatch.StartNew();

                Log.Message("Map catchup start " + mapCatchupStart + " " + TickPatch.tickUntil + " " + Find.TickManager.TicksGame + " " + Find.TickManager.CurTimeSpeed);

                map.PushFaction(ownerFaction);

                while (asyncTime.Timer < TickPatch.tickUntil)
                {
                    // Handle TIME_CONTROL and schedule new commands
                    OnMainThread.queue.RunQueue();

                    if (asyncTime.TickRateMultiplier > 0)
                    {
                        asyncTime.Tick();
                    }
                    else if (asyncTime.scheduledCmds.Count > 0)
                    {
                        asyncTime.ExecuteMapCmdsWhilePaused();
                    }
                    else
                    {
                        break;
                    }
                }

                map.PopFaction();
                map.GetComponent<MultiplayerMapComp>().SetFaction(Faction.OfPlayer);

                Log.Message("Catchup took " + time.ElapsedMilliseconds);

                Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
            }, "Loading the map", false, null);
        }

        [PacketHandler(Packets.SERVER_TIME_CONTROL)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            TickPatch.tickUntil = tickUntil;
        }

        [PacketHandler(Packets.SERVER_NOTIFICATION)]
        public void HandleNotification(ByteReader data)
        {
            string msg = data.ReadString();
            Messages.Message(msg, MessageTypeDefOf.SilentInput);
        }

        [PacketHandler(Packets.SERVER_KEEP_ALIVE)]
        public void HandleKeepAlive(ByteReader data)
        {
            Multiplayer.client.Send(Packets.CLIENT_KEEP_ALIVE, new byte[0]);
        }

        public override void Disconnected(string reason)
        {
        }

        public static void HandleCmdSchedule(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int ticks = data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extraBytes = data.ReadPrefixedBytes();

            ScheduledCommand schdl = new ScheduledCommand(cmd, ticks, mapId, extraBytes);
            OnMainThread.ScheduleCommand(schdl);

            MpLog.Log("Client command on map " + schdl.mapId);
        }

        // Currently covers:
        // - settling after joining
        public static void SyncClientWorldObj(WorldObject obj)
        {
            //byte[] data = ScribeUtil.WriteExposable(obj);
            //Multiplayer.client.Send(Packets.CLIENT_NEW_WORLD_OBJ, data);
        }
    }

    public class OnMainThread : MonoBehaviour
    {
        public static ActionQueue queue = new ActionQueue();
        public static readonly Queue<ScheduledCommand> scheduledCmds = new Queue<ScheduledCommand>();
        public static bool executingCmds;

        public void Update()
        {
            if (Multiplayer.netClient != null)
                Multiplayer.netClient.PollEvents();

            queue.RunQueue();

            if (Multiplayer.client == null) return;

            if (!LongEventHandler.ShouldWaitForEvent && Current.Game != null && Find.World != null)
                ExecuteGlobalCmdsWhilePaused();
        }

        public void OnApplicationQuit()
        {
            if (Multiplayer.netClient != null)
                Multiplayer.netClient.Stop();

            if (Multiplayer.localServer != null)
                Multiplayer.localServer.Stop();
        }

        public static void ExecuteGlobalCmdsWhilePaused()
        {
            executingCmds = true;

            try
            {
                while (scheduledCmds.Count > 0 && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                {
                    ScheduledCommand cmd = scheduledCmds.Dequeue();
                    ExecuteGlobalServerCmd(cmd, new ByteReader(cmd.data));
                }
            }
            finally
            {
                executingCmds = false;
            }
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void ScheduleCommand(ScheduledCommand cmd)
        {
            if (cmd.mapId == ScheduledCommand.GLOBAL)
            {
                scheduledCmds.Enqueue(cmd);
            }
            else
            {
                Map map = cmd.GetMap();
                if (map == null) return;

                map.GetComponent<MapAsyncTimeComp>().scheduledCmds.Enqueue(cmd);
            }
        }

        public static void ExecuteMapCmd(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            if (map == null) return;

            Multiplayer.Seed = Find.TickManager.TicksGame;
            CommandType cmdType = cmd.type;

            VisibleMapGetPatch.visibleMap = map;
            VisibleMapSetPatch.ignore = true;

            try
            {
                if (cmdType == CommandType.SYNC)
                {
                    data.context = map;
                    Sync.HandleCmd(data);
                }

                if (cmdType == CommandType.CREATE_MAP_FACTION_DATA)
                {
                    HandleMapFactionData(cmd, data);
                }

                if (cmdType == CommandType.MAP_TIME_SPEED)
                {
                    MapAsyncTimeComp comp = map.GetComponent<MapAsyncTimeComp>();
                    TimeSpeed prevSpeed = comp.timeSpeed;
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();

                    comp.SetTimeSpeed(speed);

                    if (prevSpeed == TimeSpeed.Paused)
                        comp.timerInt = cmd.ticks;
                }

                if (cmdType == CommandType.MAP_ID_BLOCK)
                {
                    IdBlock block = IdBlock.Deserialize(data);

                    if (map != null)
                    {
                        map.GetComponent<MultiplayerMapComp>().mapIdBlock = block;
                        Log.Message(Multiplayer.username + "encounter id block set");
                    }
                }

                if (cmdType == CommandType.DESIGNATOR)
                {
                    HandleDesignator(cmd, data);
                }

                if (cmdType == CommandType.DELETE_ZONE)
                {
                    string factionId = data.ReadString();
                    string zoneId = data.ReadString();

                    map.PushFaction(factionId);
                    map.zoneManager.AllZones.FirstOrDefault(z => z.label == zoneId)?.Delete();
                    map.PopFaction();
                }

                if (cmdType == CommandType.SPAWN_PAWN)
                {
                    string factionId = data.ReadString();

                    map.PushFaction(Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId));
                    Pawn pawn = ScribeUtil.ReadExposable<Pawn>(data.ReadPrefixedBytes());

                    IntVec3 spawn = CellFinderLoose.TryFindCentralCell(map, 7, 10, (IntVec3 x) => !x.Roofed(map));
                    GenSpawn.Spawn(pawn, spawn, map);
                    map.PopFaction();
                    Log.Message("spawned " + pawn);
                }

                if (cmdType == CommandType.FORBID)
                {
                    HandleForbid(cmd, data);
                }
            }
            finally
            {
                VisibleMapSetPatch.ignore = false;
                VisibleMapGetPatch.visibleMap = null;
            }
        }

        private static void HandleForbid(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            string thingId = data.ReadString();
            string factionId = data.ReadString();
            bool value = data.ReadBool();

            ThingWithComps thing = map.listerThings.AllThings.FirstOrDefault(t => t.GetUniqueLoadID() == thingId) as ThingWithComps;
            if (thing == null) return;

            CompForbiddable forbiddable = thing.GetComp<CompForbiddable>();
            if (forbiddable == null) return;

            map.PushFaction(factionId);
            forbiddable.Forbidden = value;
            map.PopFaction();
        }

        private static void HandleMapFactionData(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            string username = data.ReadString();

            Faction faction = Multiplayer.WorldComp.playerFactions[username];
            MultiplayerMapComp comp = map.GetComponent<MultiplayerMapComp>();

            if (!comp.factionMapData.ContainsKey(faction.GetUniqueLoadID()))
            {
                FactionMapData factionMapData = FactionMapData.New(map);
                comp.factionMapData[faction.GetUniqueLoadID()] = factionMapData;

                AreaAddPatch.ignore = true;
                factionMapData.areaManager.AddStartingAreas();
                AreaAddPatch.ignore = false;

                map.pawnDestinationReservationManager.RegisterFaction(faction);

                MpLog.Log("New map faction data for {0}", faction.GetUniqueLoadID());
            }
        }

        public static void ExecuteGlobalServerCmd(ScheduledCommand cmd, ByteReader data)
        {
            Multiplayer.Seed = Find.TickManager.TicksGame;
            CommandType cmdType = cmd.type;

            UniqueIdsPatch.CurrentBlock = Multiplayer.globalIdBlock;

            try
            {
                if (cmdType == CommandType.SYNC)
                {
                    Sync.HandleCmd(data);
                }

                if (cmdType == CommandType.WORLD_TIME_SPEED)
                {
                    TimeSpeed prevSpeed = Find.TickManager.CurTimeSpeed;
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();
                    TimeChangePatch.SetSpeed(speed);

                    MpLog.Log("Set world speed " + speed + " " + TickPatch.Timer + " " + Find.TickManager.TicksGame);

                    if (prevSpeed == TimeSpeed.Paused)
                        TickPatch.timerInt = cmd.ticks;
                }

                if (cmdType == CommandType.SETUP_FACTION)
                {
                    HandleSetupFaction(cmd, data);
                }

                if (cmdType == CommandType.AUTOSAVE)
                {
                    HandleAutosave(cmd, data);
                }
            }
            finally
            {
                UniqueIdsPatch.CurrentBlock = null;
            }
        }

        private static void HandleSetupFaction(ScheduledCommand command, ByteReader data)
        {
            string username = data.ReadString();
            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

            if (!comp.playerFactions.TryGetValue(username, out Faction faction))
            {
                faction = FactionGenerator.NewGeneratedFaction(FactionDefOf.PlayerColony);
                faction.Name = username + "'s faction";
                faction.def = Multiplayer.factionDef;

                Find.FactionManager.Add(faction);
                comp.playerFactions[username] = faction;

                foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
                {
                    if (current == faction) continue;
                    current.TryMakeInitialRelationsWith(faction);
                }

                MpLog.Log("New faction {0} of player {1}", faction.GetUniqueLoadID(), username);
            }

            if (username == Multiplayer.username)
            {
                Faction.OfPlayer.def = Multiplayer.factionDef;
                faction.def = FactionDefOf.PlayerColony;
            }
        }

        private static void HandleAutosave(ScheduledCommand command, ByteReader data)
        {
            TimeChangePatch.SetSpeed(TimeSpeed.Paused);

            LongEventHandler.QueueLongEvent(() =>
            {
                SaveCompression.doBetterSave = true;

                WorldGridCtorPatch.copyFrom = Find.WorldGrid;
                WorldRendererCtorPatch.copyFrom = Find.World.renderer;

                foreach (Map map in Find.Maps)
                    map.GetComponent<MultiplayerMapComp>().SetFaction(map.ParentFaction);

                XmlDocument gameDoc = Multiplayer.SaveGame();
                LoadPatch.gameToLoad = gameDoc;

                MemoryUtility.ClearAllMapsAndWorld();

                Prefs.PauseOnLoad = false;
                SavedGameLoader.LoadGameFromSaveFile("server");
                SaveCompression.doBetterSave = false;

                foreach (Map map in Find.Maps)
                    map.GetComponent<MultiplayerMapComp>().SetFaction(Multiplayer.RealPlayerFaction);

                Multiplayer.SendGameData(gameDoc);
            }, "Autosaving", false, null);

            // todo unpause after everyone completes the autosave
        }

        private static void HandleDesignator(ScheduledCommand command, ByteReader data)
        {
            Map map = command.GetMap();
            int mode = data.ReadInt32();
            string desName = data.ReadString();
            string buildDefName = data.ReadString();
            Designator designator = GetDesignator(desName, buildDefName);
            if (designator == null) return;

            string factionId = data.ReadString();

            map.PushFaction(factionId);

            try
            {
                if (!SetDesignatorState(map, designator, data)) return;

                if (mode == 0)
                {
                    IntVec3 cell = map.cellIndices.IndexToCell(data.ReadInt32());
                    designator.DesignateSingleCell(cell);
                    designator.Finalize(true);
                }
                else if (mode == 1)
                {
                    int[] cellData = data.ReadPrefixedInts();
                    IntVec3[] cells = new IntVec3[cellData.Length];
                    for (int i = 0; i < cellData.Length; i++)
                        cells[i] = map.cellIndices.IndexToCell(cellData[i]);

                    designator.DesignateMultiCell(cells.AsEnumerable());

                    Find.Selector.ClearSelection();
                }
                else if (mode == 2)
                {
                    string thingId = data.ReadString();
                    Thing thing = map.listerThings.AllThings.FirstOrDefault(t => t.GetUniqueLoadID() == thingId);

                    if (thing != null)
                    {
                        designator.DesignateThing(thing);
                        designator.Finalize(true);
                    }
                }

                foreach (Zone zone in map.zoneManager.AllZones)
                    zone.SetPropertyOrField("cellsShuffled", true);
            }
            finally
            {
                DesignatorInstallPatch.thingToInstall = null;

                map.PopFaction();
            }
        }

        private static Designator GetDesignator(string name, string buildDefName = null)
        {
            List<DesignationCategoryDef> allDefsListForReading = DefDatabase<DesignationCategoryDef>.AllDefsListForReading;
            foreach (DesignationCategoryDef cat in allDefsListForReading)
            {
                List<Designator> allResolvedDesignators = cat.AllResolvedDesignators;
                foreach (Designator des in allResolvedDesignators)
                {
                    if (des.GetType().FullName == name && (buildDefName.NullOrEmpty() || (des is Designator_Build desBuild && desBuild.PlacingDef.defName == buildDefName)))
                        return des;
                }
            }

            return null;
        }

        private static bool SetDesignatorState(Map map, Designator designator, ByteReader data)
        {
            if (designator is Designator_AreaAllowed)
            {
                string areaId = data.ReadString();
                Area area = map.areaManager.AllAreas.FirstOrDefault(a => a.GetUniqueLoadID() == areaId);
                if (area == null) return false;
                DesignatorPatches.selectedAreaField.SetValue(null, area);
            }

            if (designator is Designator_Place)
            {
                DesignatorPatches.buildRotField.SetValue(designator, new Rot4(data.ReadByte()));
            }

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
            {
                string thingDefName = data.ReadString();
                ThingDef stuffDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(t => t.defName == thingDefName);
                if (stuffDef == null) return false;
                DesignatorPatches.buildStuffField.SetValue(designator, stuffDef);
            }

            if (designator is Designator_Install)
            {
                string thingId = data.ReadString();
                Thing thing = map.listerThings.AllThings.FirstOrDefault(t => t.GetUniqueLoadID() == thingId);
                if (thing == null) return false;
                DesignatorInstallPatch.thingToInstall = thing;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.DoSingleTick))]
    public static class TickPatch
    {
        public static int tickUntil;
        public static double timerInt;

        public static int Timer => (int)timerInt;

        public static bool tickingWorld;
        private static float currentTickRate;

        static bool Prefix()
        {
            tickingWorld = Multiplayer.client == null || Timer < tickUntil;
            currentTickRate = Find.TickManager.TickRateMultiplier;

            if (tickingWorld)
            {
                UniqueIdsPatch.CurrentBlock = Multiplayer.globalIdBlock;
            }

            return tickingWorld;
        }

        static void Postfix()
        {
            if (!tickingWorld) return;

            TickManager tickManager = Find.TickManager;

            while (OnMainThread.scheduledCmds.Count > 0 && OnMainThread.scheduledCmds.Peek().ticks == Timer)
            {
                ScheduledCommand cmd = OnMainThread.scheduledCmds.Dequeue();
                OnMainThread.ExecuteGlobalServerCmd(cmd, new ByteReader(cmd.data));
            }

            /*if (Multiplayer.client != null && Find.TickManager.TicksGame % (60 * 15) == 0)
            {
                Stopwatch watch = Stopwatch.StartNew();

                Find.VisibleMap.PushFaction(Find.VisibleMap.ParentFaction);

                ScribeUtil.StartWriting();
                Scribe.EnterNode("data");
                World world = Find.World;
                Scribe_Deep.Look(ref world, "world");
                Map map = Find.VisibleMap;
                Scribe_Deep.Look(ref map, "map");
                byte[] data = ScribeUtil.FinishWriting();
                string hex = BitConverter.ToString(MD5.Create().ComputeHash(data)).Replace("-", "");

                Multiplayer.client.Send(Packets.CLIENT_MAP_STATE_DEBUG, data);

                Find.VisibleMap.PopFaction();

                Log.Message(Multiplayer.username + " replay " + watch.ElapsedMilliseconds + " " + data.Length + " " + hex);
            }*/

            UniqueIdsPatch.CurrentBlock = null;

            tickingWorld = false;

            if (currentTickRate >= 1)
                timerInt += 1f / currentTickRate;
        }
    }

    public class FactionWorldData : IExposable
    {
        public ResearchManager researchManager;
        public DrugPolicyDatabase drugPolicyDatabase;
        public OutfitDatabase outfitDatabase;
        public PlaySettings playSettings;

        public FactionWorldData()
        {
            researchManager = new ResearchManager();
            drugPolicyDatabase = new DrugPolicyDatabase();
            outfitDatabase = new OutfitDatabase();
            playSettings = new PlaySettings();
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref researchManager, "researchManager");
            Scribe_Deep.Look(ref drugPolicyDatabase, "drugPolicyDatabase");
            Scribe_Deep.Look(ref outfitDatabase, "outfitDatabase");
            Scribe_Deep.Look(ref playSettings, "playSettings");
        }
    }

    public class FactionMapData : IExposable
    {
        public Map map;
        public DesignationManager designationManager;
        public AreaManager areaManager;
        public ZoneManager zoneManager;
        public SlotGroupManager slotGroupManager;
        public ListerHaulables listerHaulables;
        public ResourceCounter resourceCounter;

        private FactionMapData()
        {
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref designationManager, "designationManager", map);
            Scribe_Deep.Look(ref areaManager, "areaManager", map);
            Scribe_Deep.Look(ref zoneManager, "zoneManager", map);
        }

        public static FactionMapData New(Map map)
        {
            return new FactionMapData()
            {
                map = map,
                designationManager = new DesignationManager(map),
                areaManager = new AreaManager(map),
                zoneManager = new ZoneManager(map),
                slotGroupManager = new SlotGroupManager(map),
                listerHaulables = new ListerHaulables(map),
                resourceCounter = new ResourceCounter(map),
            };
        }

        public static FactionMapData FromMap(Map map)
        {
            return new FactionMapData()
            {
                map = map,
                designationManager = map.designationManager,
                areaManager = map.areaManager,
                zoneManager = map.zoneManager,
                slotGroupManager = map.slotGroupManager,
                listerHaulables = map.listerHaulables,
                resourceCounter = map.resourceCounter,
            };
        }
    }

    public class MultiplayerWorldComp : WorldComponent
    {
        public string worldId = Guid.NewGuid().ToString();
        public int sessionId = new System.Random().Next();

        public Dictionary<string, Faction> playerFactions = new Dictionary<string, Faction>();
        private List<string> keyWorkingList;
        private List<Faction> valueWorkingList;

        public Dictionary<string, FactionWorldData> factionData = new Dictionary<string, FactionWorldData>();

        public MultiplayerWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            Scribe_Values.Look(ref TickPatch.timerInt, "timer");
            Scribe_Values.Look(ref sessionId, "sessionId");

            ScribeUtil.Look(ref playerFactions, "playerFactions", LookMode.Reference, ref keyWorkingList, ref valueWorkingList);
            if (Scribe.mode == LoadSaveMode.LoadingVars && playerFactions == null)
                playerFactions = new Dictionary<string, Faction>();

            TimeSpeed timeSpeed = Find.TickManager.CurTimeSpeed;
            Scribe_Values.Look(ref timeSpeed, "timeSpeed");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Find.TickManager.CurTimeSpeed = timeSpeed;

            Multiplayer.ExposeIdBlock(ref Multiplayer.globalIdBlock, "globalIdBlock");
        }

        public string GetUsernameByFaction(Faction faction)
        {
            return playerFactions.FirstOrDefault(pair => pair.Value == faction).Key;
        }
    }

    public class MultiplayerMapComp : MapComponent
    {
        public IdBlock mapIdBlock;

        public Dictionary<string, FactionMapData> factionMapData = new Dictionary<string, FactionMapData>();
        private List<string> keyWorkingList;
        private List<FactionMapData> valueWorkingList;

        // for BetterSaver
        public List<Thing> loadedThings;

        public static bool tickingFactions;

        public MultiplayerMapComp(Map map) : base(map)
        {
            if (map.info != null && map.info.parent != null)
                factionMapData[map.ParentFaction.GetUniqueLoadID()] = FactionMapData.FromMap(map);
        }

        public override void MapComponentTick()
        {
            if (Multiplayer.client == null) return;

            tickingFactions = true;

            foreach (KeyValuePair<string, FactionMapData> data in factionMapData)
            {
                map.PushFaction(data.Key);
                data.Value.listerHaulables.ListerHaulablesTick();
                data.Value.resourceCounter.ResourceCounterTick();
                map.PopFaction();
            }

            tickingFactions = false;
        }

        public void SetFaction(Faction faction, bool silent = false)
        {
            string factionId = faction.GetUniqueLoadID();
            if (!factionMapData.TryGetValue(factionId, out FactionMapData data))
            {
                if (!silent)
                    MpLog.Log("No map faction data for faction {0} on map {1}", faction, map.uniqueID);
                return;
            }

            map.designationManager = data.designationManager;
            map.areaManager = data.areaManager;
            map.zoneManager = data.zoneManager;
            map.slotGroupManager = data.slotGroupManager;
            map.listerHaulables = data.listerHaulables;
            map.resourceCounter = data.resourceCounter;
        }

        public override void ExposeData()
        {
            // saving indicator
            bool isPlayerHome = map.IsPlayerHome;
            Scribe_Values.Look(ref isPlayerHome, "isPlayerHome", false, true);

            Multiplayer.ExposeIdBlock(ref mapIdBlock, "mapIdBlock");

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                string parentFaction = map.ParentFaction.GetUniqueLoadID();
                if (map.areaManager != factionMapData[parentFaction].areaManager)
                    MpLog.Log("Current map faction data is not parent's faction data during map saving. This might cause problems.");

                Dictionary<string, FactionMapData> data = new Dictionary<string, FactionMapData>(factionMapData);
                data.Remove(parentFaction);
                ScribeUtil.Look(ref data, "factionMapData", LookMode.Deep, ref keyWorkingList, ref valueWorkingList, map);
            }
            else
            {
                ScribeUtil.Look(ref factionMapData, "factionMapData", LookMode.Deep, ref keyWorkingList, ref valueWorkingList, map);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars && factionMapData == null)
                factionMapData = new Dictionary<string, FactionMapData>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                string parentFaction = map.ParentFaction.GetUniqueLoadID();
                if (factionMapData.ContainsKey(parentFaction))
                    MpLog.Log("Map's saved faction data includes parent's faction data.");

                factionMapData[parentFaction] = FactionMapData.FromMap(map);
            }
        }
    }

    public static class FactionContext
    {
        public static Stack<Faction> stack = new Stack<Faction>();

        public static Faction Push(Faction faction)
        {
            if (faction == null || (faction.def != Multiplayer.factionDef && faction.def != FactionDefOf.PlayerColony))
            {
                // so we can pop it later
                stack.Push(null);
                return null;
            }

            stack.Push(OfPlayer);
            Set(faction);
            return faction;
        }

        public static Faction Pop()
        {
            Faction f = stack.Pop();
            if (f != null)
                Set(f);
            return f;
        }

        private static void Set(Faction faction)
        {
            OfPlayer.def = Multiplayer.factionDef;
            faction.def = FactionDefOf.PlayerColony;
        }

        public static Faction OfPlayer => Find.FactionManager.AllFactions.FirstOrDefault(f => f.IsPlayer);
    }

}

