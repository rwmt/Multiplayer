extern alias zip;

using Harmony;
using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
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
using Verse.Profile;
using Verse.Sound;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class Multiplayer
    {
        public static String username;
        public static MultiplayerServer localServer;
        public static Thread serverThread;
        public static IConnection client;
        public static NetManager netClient;
        public static ChatWindow chat = new ChatWindow();
        public static PacketLogWindow packetLog = new PacketLogWindow();
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");

        public static bool reloading;
        public static bool simulating;

        public static IdBlock globalIdBlock;

        public static MultiplayerWorldComp WorldComp => Find.World.GetComponent<MultiplayerWorldComp>();
        public static Faction RealPlayerFaction => client != null ? WorldComp.myFaction : Faction.OfPlayer;

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

        public static bool Ticking => MultiplayerWorldComp.tickingWorld || MapAsyncTimeComp.tickingMap != null;
        public static bool ShouldSync => client != null && !Ticking && !OnMainThread.executingCmds && !reloading;

        static Multiplayer()
        {
            if (GenCommandLine.CommandLineArgPassed("profiler"))
                SimpleProfiler.CheckAvailable();

            MpLog.info = str => Log.Message($"{username} {(Current.Game != null ? (Find.VisibleMap != null ? Find.VisibleMap.GetComponent<MapAsyncTimeComp>().mapTicks.ToString() : "") : "")} {str}");

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

            harmony.DoMpPatches(typeof(MethodMarkers));
            harmony.DoMpPatches(typeof(SyncPatches));
            harmony.DoMpPatches(typeof(SyncDelegates));
            harmony.DoMpPatches(typeof(SyncThingFilters));
            harmony.DoMpPatches(typeof(MapParentFactionPatch));
            harmony.DoMpPatches(typeof(CancelMapManagersTick));
            harmony.DoMpPatches(typeof(CancelMapManagersUpdate));

            RuntimeHelpers.RunClassConstructor(typeof(SyncPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncFieldsPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncDelegates).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncThingFilters).TypeHandle);

            Sync.RegisterFieldPatches(typeof(SyncFieldsPatches));
            Sync.RegisterSyncDelegates(typeof(SyncDelegates));
            Sync.RegisterSyncMethods(typeof(SyncPatches));
            Sync.RegisterSyncMethods(typeof(SyncThingFilters));

            DoPatches();

            Log.messageQueue.maxMessages = 1000;
            DebugSettings.noAnimals = true;

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

        public static XmlDocument SaveAndReload()
        {
            XmlDocument SaveGame()
            {
                SaveCompression.doSaveCompression = true;

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

                SaveCompression.doSaveCompression = false;

                return ScribeUtil.FinishWritingToDoc();
            }

            reloading = true;

            WorldGridCtorPatch.copyFrom = Find.WorldGrid;
            WorldRendererCtorPatch.copyFrom = Find.World.renderer;
            Dictionary<int, Vector3> tweenedPos = new Dictionary<int, Vector3>();

            foreach (Map map in Find.Maps)
            {
                map.GetComponent<MultiplayerMapComp>().SetFaction(map.ParentFaction);
                MapDrawerRegenPatch.copyFrom[map.uniqueID] = map.mapDrawer;
                //RebuildRegionsAndRoomsPatch.copyFrom[map.uniqueID] = map.regionGrid;

                foreach (Pawn p in map.mapPawns.AllPawns)
                    if (p.drawer != null)
                        tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;
            }

            Stopwatch watch = Stopwatch.StartNew();
            SimpleProfiler.Start();
            XmlDocument gameDoc = SaveGame();
            SimpleProfiler.Pause();
            SimpleProfiler.Print("profiler_save.txt");
            SimpleProfiler.Init(username);
            Log.Message("Saving took " + watch.ElapsedMilliseconds);

            MemoryUtility.ClearAllMapsAndWorld();

            SaveCompression.doSaveCompression = true;

            watch = Stopwatch.StartNew();
            LoadPatch.gameToLoad = gameDoc;
            Prefs.PauseOnLoad = false;
            SimpleProfiler.Start();
            SavedGameLoader.LoadGameFromSaveFile("server");
            SimpleProfiler.Pause();
            SimpleProfiler.Print("profiler_load.txt");
            SimpleProfiler.Init(username);
            Log.Message("Loading took " + watch.ElapsedMilliseconds);

            foreach (Map m in Find.Maps)
                foreach (Pawn p in m.mapPawns.AllPawns)
                    if (p.drawer != null && tweenedPos.TryGetValue(p.thingIDNumber, out Vector3 v))
                    {
                        p.drawer.tweener.tweenedPos = v;
                        p.drawer.tweener.lastDrawFrame = Time.frameCount;
                    }

            SaveCompression.doSaveCompression = false;

            foreach (Map map in Find.Maps)
                map.GetComponent<MultiplayerMapComp>().SetFaction(RealPlayerFaction);

            reloading = false;

            return gameDoc;
        }

        public static void CacheAndSendGameData(XmlDocument doc, bool sendGame = true)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                OnMainThread.localMapData[id] = ScribeUtil.XmlToByteArray(mapNode);
            }

            string nonPlayerMaps = "li[components/li/@Class='Multiplayer.Client.MultiplayerMapComp' and components/li/isPlayerHome='False']";
            mapsNode.SelectAndRemove(nonPlayerMaps);

            byte[] mapData = ScribeUtil.XmlToByteArray(mapsNode["li"], null, true);
            File.WriteAllBytes("map_" + username + ".xml", mapData);

            byte[] compressedMaps = GZipStream.CompressBuffer(mapData);
            // todo send map id
            client.Send(Packets.CLIENT_AUTOSAVED_DATA, false, 0, compressedMaps);

            gameNode["visibleMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(doc, null, true);
            OnMainThread.localGameData = gameData;

            if (sendGame)
            {
                File.WriteAllBytes("game.xml", gameData);

                byte[] compressedGame = GZipStream.CompressBuffer(gameData);
                client.Send(Packets.CLIENT_AUTOSAVED_DATA, true, compressedGame);
            }
        }

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

            // Set ThingContext and FactionContext (for pawns and buildings) in common Thing methods
            {
                var thingMethodPrefix = new HarmonyMethod(typeof(PatchThingMethods).GetMethod("Prefix"));
                var thingMethodPostfix = new HarmonyMethod(typeof(PatchThingMethods).GetMethod("Postfix"));
                var thingMethods = new[] { "Tick", "TickRare", "TickLong", "SpawnSetup", "TakeDamage", "Kill" };

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

            int tickUntil = data.ReadInt32();

            List<ScheduledCommand> cmds = new List<ScheduledCommand>();

            int cmdsLen = data.ReadInt32();
            for (int i = 0; i < cmdsLen; i++)
                cmds.Add(ClientPlayingState.DeserializeCmd(new ByteReader(data.ReadPrefixedBytes())));

            byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.localGameData = worldData;

            XmlDocument gameDoc = ScribeUtil.GetDocument(worldData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            int maps = data.ReadInt32();
            for (int i = 0; i < maps; i++)
            {
                int mapId = data.ReadInt32();

                int mapCmdsLen = data.ReadInt32();
                for (int j = 0; j < mapCmdsLen; j++)
                    cmds.Add(ClientPlayingState.DeserializeCmd(new ByteReader(data.ReadPrefixedBytes())));

                byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
                OnMainThread.localMapData[mapId] = mapData;

                using (XmlReader reader = XmlReader.Create(new MemoryStream(mapData)))
                {
                    XmlNode mapNode = gameDoc.ReadNode(reader);
                    gameNode["maps"].AppendChild(mapNode);

                    if (gameNode["visibleMapIndex"] == null)
                        gameNode.AddNode("visibleMapIndex", mapId.ToString());
                }
            }

            cmds.Sort((a, b) => a.id - b.id);

            foreach (ScheduledCommand cmd in cmds)
                OnMainThread.ScheduleCommand(cmd);

            TickPatch.tickUntil = tickUntil;
            LoadPatch.gameToLoad = gameDoc;

            Log.Message("Game data size: " + data.GetBytes().Length);

            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = "server";

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(CatchUp(() =>
                    {
                        Multiplayer.client.Send(Packets.CLIENT_WORLD_LOADED);
                    }), "Loading", null);
                });
            }, "Play", "Loading the game", true, null);
        }

        public static IEnumerable CatchUp(Action finishAction)
        {
            int start = TickPatch.Timer;
            int startTicks = Find.Maps[0].AsyncTime().mapTicks;
            float startTime = Time.realtimeSinceStartup;

            while (TickPatch.Timer < TickPatch.tickUntil)
            {
                TickPatch.accumulator = 100;

                Multiplayer.simulating = true;
                TickPatch.Tick();
                Multiplayer.simulating = false;

                int pct = (int)((float)(TickPatch.Timer - start) / (TickPatch.tickUntil - start) * 100);
                LongEventHandler.SetCurrentEventText($"Loading game {pct}/100 " + TickPatch.Timer + " " + TickPatch.tickUntil + " " + OnMainThread.scheduledCmds.Count + " " + Find.Maps[0].AsyncTime().mapTicks + " " + (Find.Maps[0].AsyncTime().mapTicks - startTicks) / (Time.realtimeSinceStartup - startTime));

                bool allPaused = TickPatch.AllTickables.All(t => t.CurTimePerTick == 0);
                if (allPaused) break;

                yield return null;
            }

            finishAction();
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
            OnMainThread.ScheduleCommand(DeserializeCmd(data));
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

                Multiplayer.reloading = true;
                Current.ProgramState = ProgramState.MapInitializing;
                SaveCompression.doSaveCompression = true;

                // Faction context for Pawn.ExposeData is set in a patch
                ScribeUtil.StartLoading(mapData);
                ScribeUtil.SupplyCrossRefs();
                List<Map> maps = null;
                Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
                ScribeUtil.FinalizeLoading();

                MpLog.Log("Maps " + maps.Count);
                Map map = maps[0];

                Current.Game.AddMap(map);

                // Faction context for Pawn.SpawnSetup is set in a patch
                map.FinalizeLoading();

                SaveCompression.doSaveCompression = false;
                Multiplayer.reloading = false;
                Current.ProgramState = ProgramState.Playing;

                Find.World.renderer.wantedMode = WorldRenderMode.None;
                Current.Game.VisibleMap = map;

                // RegenerateEverythingNow
                LongEventHandler.ExecuteToExecuteWhenFinished();

                MapAsyncTimeComp asyncTime = map.GetComponent<MapAsyncTimeComp>();

                foreach (byte[] cmd in cmds)
                    OnMainThread.ScheduleCommand(DeserializeCmd(new ByteReader(cmd)));

                mapCatchupStart = TickPatch.Timer;

                Log.Message("Loading took " + time.ElapsedMilliseconds);
                Log.Message("Map catchup start " + mapCatchupStart + " " + TickPatch.tickUntil + " " + Find.TickManager.TicksGame + " " + Find.TickManager.CurTimeSpeed);

                time = Stopwatch.StartNew();
                Faction ownerFaction = map.info.parent.Faction;
                map.PushFaction(ownerFaction);
                Multiplayer.simulating = true;

                //while (asyncTime.Timer < TickPatch.tickUntil)
                {
                    // Handle TIME_CONTROL and schedule new commands
                    OnMainThread.queue.RunQueue();

                    /*if (asyncTime.TickRateMultiplier > 0)
                    {
                        asyncTime.Tick();
                    }
                    else if (asyncTime.scheduledCmds.Count > 0)
                    {
                        //asyncTime.ExecuteMapCmdsWhilePaused();
                    }
                    else
                    {
                        //break;
                    }*/
                }

                // Update pawn rendering position
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                    pawn.drawer.tweener.tweenedPos = pawn.drawer.tweener.TweenedPosRoot();

                Multiplayer.simulating = false;
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

        public static ScheduledCommand DeserializeCmd(ByteReader data)
        {
            int id = data.ReadInt32();
            CommandType cmd = (CommandType)data.ReadInt32();
            int ticks = data.ReadInt32();
            int factionId = data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extraBytes = data.ReadPrefixedBytes();

            return new ScheduledCommand(id, cmd, ticks, factionId, mapId, extraBytes);
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

        public static byte[] localGameData;
        public static Dictionary<int, byte[]> localMapData = new Dictionary<int, byte[]>();
        public static List<ScheduledCommand> localCmds = new List<ScheduledCommand>();

        public void Update()
        {
            if (Multiplayer.netClient != null)
                Multiplayer.netClient.PollEvents();

            queue.RunQueue();

            if (Multiplayer.client == null) return;

            UpdateSync();
        }

        private void UpdateSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (!data.sent && Environment.TickCount - data.timestamp > 100)
                    {
                        data.handler.DoSync(data.target, data.newValue);
                        data.sent = true;
                    }

                    return !Equals(data.target.GetPropertyOrField(data.handler.memberPath), data.oldValue);
                });
            }
        }

        public void OnApplicationQuit()
        {
            StopMultiplayer();
        }

        public static void StopMultiplayer()
        {
            if (Multiplayer.netClient != null)
            {
                Multiplayer.netClient.Stop();
                Multiplayer.netClient = null;
            }

            if (Multiplayer.localServer != null)
            {
                Multiplayer.localServer.running = false;
                Multiplayer.serverThread = null;
                Multiplayer.localServer = null;
            }

            if (Multiplayer.client != null)
            {
                Multiplayer.client = null;
            }

            Sync.bufferedChanges.Clear();

            localGameData = null;
            localMapData.Clear();
            localCmds.Clear();
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void ScheduleCommand(ScheduledCommand cmd)
        {
            scheduledCmds.Enqueue(cmd);
            localCmds.Add(cmd);

            MpLog.Log($"Cmd: {cmd.type}, faction: {cmd.factionId}, map: {cmd.mapId}, ticks: {cmd.ticks}");
        }

        public static void ExecuteMapCmd(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            if (map == null) return;

            MapAsyncTimeComp comp = map.GetComponent<MapAsyncTimeComp>();
            CommandType cmdType = cmd.type;

            VisibleMapGetPatch.visibleMap = map;
            VisibleMapSetPatch.ignore = true;

            map.PushFaction(cmd.GetFaction());
            comp.PreContext();

            executingCmds = true;

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
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();
                    comp.TimeSpeed = speed;
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

                if (cmdType == CommandType.SPAWN_PAWN)
                {
                    Pawn pawn = ScribeUtil.ReadExposable<Pawn>(data.ReadPrefixedBytes());

                    IntVec3 spawn = CellFinderLoose.TryFindCentralCell(map, 7, 10, (IntVec3 x) => !x.Roofed(map));
                    GenSpawn.Spawn(pawn, spawn, map);
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
                map.PopFaction();
                executingCmds = false;
                comp.PostContext();
            }
        }

        private static void HandleForbid(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            int thingId = data.ReadInt32();
            bool value = data.ReadBool();

            ThingWithComps thing = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == thingId) as ThingWithComps;
            if (thing == null) return;

            CompForbiddable forbiddable = thing.GetComp<CompForbiddable>();
            if (forbiddable == null) return;

            forbiddable.Forbidden = value;
        }

        private static void HandleMapFactionData(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            int factionId = data.ReadInt32();

            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == factionId);
            MultiplayerMapComp comp = map.GetComponent<MultiplayerMapComp>();

            if (!comp.factionMapData.ContainsKey(factionId))
            {
                FactionMapData factionMapData = FactionMapData.New(map);
                comp.factionMapData[factionId] = factionMapData;

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
            FactionContext.Push(cmd.GetFaction());

            executingCmds = true;

            try
            {
                if (cmdType == CommandType.SYNC)
                {
                    Sync.HandleCmd(data);
                }

                if (cmdType == CommandType.WORLD_TIME_SPEED)
                {
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();
                    Multiplayer.WorldComp.timeSpeedInt = speed;

                    MpLog.Log("Set world speed " + speed + " " + TickPatch.Timer + " " + Find.TickManager.TicksGame);
                }

                if (cmdType == CommandType.SETUP_FACTION)
                {
                    HandleSetupFaction(cmd, data);
                }

                if (cmdType == CommandType.AUTOSAVE)
                {
                    WorldTimeChangePatch.SetSpeed(TimeSpeed.Paused);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        localGameData = null;
                        localMapData.Clear();
                        localCmds.Clear();

                        XmlDocument doc = Multiplayer.SaveAndReload();
                        //Multiplayer.CacheAndSendGameData(doc);
                    }, "Autosaving", false, null);
                }
            }
            finally
            {
                UniqueIdsPatch.CurrentBlock = null;
                FactionContext.Pop();
                executingCmds = false;
            }
        }

        private static void HandleSetupFaction(ScheduledCommand command, ByteReader data)
        {
            string username = data.ReadString();
            int factionId = data.ReadInt32();

            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == factionId);
            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

            if (faction == null)
            {
                faction = new Faction
                {
                    loadID = factionId,
                    def = Multiplayer.factionDef,
                    Name = username + "'s faction",
                    centralMelanin = Rand.Value
                };

                Find.FactionManager.Add(faction);

                foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
                {
                    if (current == faction) continue;
                    current.TryMakeInitialRelationsWith(faction);
                }

                MpLog.Log("New faction {0} of player {1}", faction.GetUniqueLoadID(), username);
            }

            if (username == Multiplayer.username)
            {
                comp.myFaction = faction;
                Faction.OfPlayer.def = Multiplayer.factionDef;
                faction.def = FactionDefOf.PlayerColony;
            }
        }

        private static void HandleDesignator(ScheduledCommand command, ByteReader data)
        {
            Map map = command.GetMap();
            int mode = data.ReadInt32();
            string desName = data.ReadString();
            string buildDefName = data.ReadString();
            Designator designator = GetDesignator(desName, buildDefName);
            if (designator == null) return;

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
                    int thingId = data.ReadInt32();
                    Thing thing = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == thingId);

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
                int areaId = data.ReadInt32();
                Area area = map.areaManager.AllAreas.FirstOrDefault(a => a.ID == areaId);
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
                int thingId = data.ReadInt32();
                Thing thing = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == thingId);
                if (thing == null) return false;
                DesignatorInstallPatch.thingToInstall = thing;
            }

            return true;
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

    public class MultiplayerWorldComp : WorldComponent, ITickable
    {
        public static bool tickingWorld;

        public float RealTimeToTickThrough { get; set; }

        public float CurTimePerTick
        {
            get
            {
                if (TickRateMultiplier == 0f)
                    return 0f;
                return 1f / TickRateMultiplier;
            }
        }

        public float TickRateMultiplier
        {
            get
            {
                switch (timeSpeedInt)
                {
                    case TimeSpeed.Normal:
                        return 1f;
                    case TimeSpeed.Fast:
                        return 3f;
                    case TimeSpeed.Superfast:
                        return 6f;
                    case TimeSpeed.Ultrafast:
                        return 15f;
                    default:
                        return 0f;
                }
            }
        }

        public TimeSpeed TimeSpeed
        {
            get => timeSpeedInt;
            set => timeSpeedInt = value;
        }

        public Faction myFaction;

        public string worldId = Guid.NewGuid().ToString();
        public int sessionId = new System.Random().Next();
        public TimeSpeed timeSpeedInt;
        public Dictionary<int, FactionWorldData> factionData = new Dictionary<int, FactionWorldData>();

        public MultiplayerWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            Scribe_Values.Look(ref TickPatch.timerInt, "timer");
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");

            Scribe_References.Look(ref myFaction, "myFaction"); // local only

            Multiplayer.ExposeIdBlock(ref Multiplayer.globalIdBlock, "globalIdBlock");
        }

        public void Tick()
        {
            tickingWorld = true;
            UniqueIdsPatch.CurrentBlock = Multiplayer.globalIdBlock;
            Find.TickManager.CurTimeSpeed = timeSpeedInt;

            Find.TickManager.DoSingleTick();

            UniqueIdsPatch.CurrentBlock = null;
            tickingWorld = false;
        }
    }

    public class MultiplayerMapComp : MapComponent
    {
        public IdBlock mapIdBlock;

        public Dictionary<int, FactionMapData> factionMapData = new Dictionary<int, FactionMapData>();
        private List<FactionMapData> valueWorkingList;

        // for SaveCompression
        public List<Thing> loadedThings;

        public static bool tickingFactions;

        public MultiplayerMapComp(Map map) : base(map)
        {
            if (map.info != null && map.info.parent != null)
                factionMapData[map.ParentFaction.loadID] = FactionMapData.FromMap(map);
        }

        public override void MapComponentTick()
        {
            if (Multiplayer.client == null) return;

            tickingFactions = true;

            foreach (KeyValuePair<int, FactionMapData> data in factionMapData)
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
            if (!factionMapData.TryGetValue(faction.loadID, out FactionMapData data))
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

            List<int> keyWorkingList = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int parentFaction = map.ParentFaction.loadID;
                if (map.areaManager != factionMapData[parentFaction].areaManager)
                    MpLog.Log("Current map faction data is not parent's faction data during map saving. This might cause problems.");

                Dictionary<int, FactionMapData> data = new Dictionary<int, FactionMapData>(factionMapData);
                data.Remove(parentFaction);
                ScribeUtil.Look(ref data, "factionMapData", LookMode.Deep, ref keyWorkingList, ref valueWorkingList, map);
            }
            else
            {
                ScribeUtil.Look(ref factionMapData, "factionMapData", LookMode.Deep, ref keyWorkingList, ref valueWorkingList, map);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars && factionMapData == null)
                factionMapData = new Dictionary<int, FactionMapData>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int parentFaction = map.ParentFaction.loadID;
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

