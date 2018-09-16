extern alias zip;

using Harmony;
using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Profile;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class Multiplayer
    {
        public static MultiplayerSession session;
        public static MultiplayerGame game;

        public static IConnection Client => session?.client;
        public static MultiplayerServer LocalServer => session?.localServer;
        public static ChatWindow Chat => session?.chat;
        public static PacketLogWindow PacketLog => session?.packetLog;

        public static string username;
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");

        public static bool reloading;
        public static bool simulating;

        public static FactionDef factionDef = FactionDef.Named("MultiplayerColony");
        public static FactionDef dummyFactionDef = FactionDef.Named("MultiplayerDummy");

        public static IdBlock GlobalIdBlock => game.worldComp.globalIdBlock;
        public static Faction DummyFaction => game.dummyFaction;
        public static MultiplayerWorldComp WorldComp => game.worldComp;

        public static Faction RealPlayerFaction
        {
            get => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;
            set => game.RealPlayerFaction = value;
        }

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

        public static bool Ticking => MultiplayerWorldComp.tickingWorld || MapAsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static bool ShouldSync => Client != null && !Ticking && !OnMainThread.executingCmds && !reloading && Current.ProgramState == ProgramState.Playing;

        public static Callback<P2PSessionRequest_t> sessionReqCallback;
        public static Callback<P2PSessionConnectFail_t> p2pFail;
        public static Callback<FriendRichPresenceUpdate_t> friendRchpUpdate;
        public static Callback<GameRichPresenceJoinRequested_t> gameJoinReq;
        public static Callback<PersonaStateChange_t> personaChange;
        public static AppId_t RimWorldAppId;

        public const string SteamConnectStart = " -mpserver=";

        static Multiplayer()
        {
            if (GenCommandLine.CommandLineArgPassed("profiler"))
                SimpleProfiler.CheckAvailable();

            MpLog.info = str => Log.Message($"{username} {(Current.Game != null ? (Find.CurrentMap != null ? Find.CurrentMap.GetComponent<MapAsyncTimeComp>().mapTicks.ToString() : "") : "")} {str}");

            GenCommandLine.TryGetCommandLineArg("username", out username);
            if (username == null)
                username = SteamUtility.SteamPersonaName;
            if (username == "???")
                username = "Player" + Rand.Range(0, 9999);

            SimpleProfiler.Init(username);

            if (SteamManager.Initialized)
                InitSteam();

            Log.Message("Player's username: " + username);
            Log.Message("Processor: " + SystemInfo.processorType);

            var gameobject = new GameObject();
            gameobject.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(gameobject);

            MpConnectionState.RegisterState(typeof(ClientSteamState));
            MpConnectionState.RegisterState(typeof(ClientJoiningState));
            MpConnectionState.RegisterState(typeof(ClientPlayingState));

            harmony.DoMpPatches(typeof(HarmonyPatches));

            harmony.DoMpPatches(typeof(CancelMapManagersTick));
            harmony.DoMpPatches(typeof(CancelMapManagersUpdate));
            harmony.DoMpPatches(typeof(CancelReinitializationDuringLoading));

            harmony.DoMpPatches(typeof(MainMenuMarker));
            harmony.DoMpPatches(typeof(MainMenuPatch));

            SyncHandlers.Init();

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
                    ClientUtil.TryConnect(addr, MultiplayerServer.DefaultPort, conn =>
                    {
                        MpLog.Log("Client connected");
                    });
                }, "Connecting", false, null);
            }
        }

        private static void InitSteam()
        {
            SteamFriends.GetFriendGamePlayed(SteamUser.GetSteamID(), out FriendGameInfo_t gameInfo);
            RimWorldAppId = gameInfo.m_gameID.AppID();

            sessionReqCallback = Callback<P2PSessionRequest_t>.Create(req =>
            {
                if (session != null && session.allowSteam && !session.pendingSteam.Contains(req.m_steamIDRemote))
                {
                    session.pendingSteam.Add(req.m_steamIDRemote);
                    SteamFriends.RequestUserInformation(req.m_steamIDRemote, true);
                }
            });

            friendRchpUpdate = Callback<FriendRichPresenceUpdate_t>.Create(update =>
            {
            });

            gameJoinReq = Callback<GameRichPresenceJoinRequested_t>.Create(req =>
            {
            });

            personaChange = Callback<PersonaStateChange_t>.Create(change =>
            {
            });

            p2pFail = Callback<P2PSessionConnectFail_t>.Create(fail =>
            {
                if (session == null) return;

                CSteamID remoteId = fail.m_steamIDRemote;

                if (Client is SteamConnection clientConn && clientConn.remoteId == remoteId)
                    OnMainThread.StopMultiplayer();

                if (LocalServer == null) return;

                ServerPlayer player = LocalServer.players.Find(p => p.connection is SteamConnection conn && conn.remoteId == remoteId);
                if (player != null)
                    LocalServer.OnDisconnected(player.connection, null);
            });
        }

        public static XmlDocument SaveAndReload()
        {
            /*if (serverThread != null)
            {
                Map map = Find.Maps[0];
                List<Region> regions = new List<Region>(map.regionGrid.AllRegions);
                foreach (Region region in regions)
                {
                    region.id = map.cellIndices.CellToIndex(region.AnyCell);
                    region.cachedCellCount = -1;
                    region.precalculatedHashCode = Gen.HashCombineInt(region.id, 1295813358);
                    region.closedIndex = new uint[RegionTraverser.NumWorkers];

                    if (region.Room != null)
                    {
                        region.Room.ID = region.id;
                        region.Room.Group.ID = region.id;
                    }
                }

                StringBuilder builder = new StringBuilder();
                SimpleProfiler.DumpMemory(Find.Maps[0].spawnedThings, builder);
                File.WriteAllText("memory_save", builder.ToString());
            }*/

            XmlDocument SaveGame()
            {
                SaveCompression.doSaveCompression = true;

                ScribeUtil.StartWritingToDoc();

                Scribe.EnterNode("savegame");
                ScribeMetaHeaderUtility.WriteMetaHeader();
                Scribe.EnterNode("game");
                sbyte visibleMapIndex = Current.Game.currentMapIndex;
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
            int localFactionId = RealPlayerFaction.loadID;

            RealPlayerFaction = DummyFaction;

            foreach (Map map in Find.Maps)
            {
                MapDrawerRegenPatch.copyFrom[map.uniqueID] = map.mapDrawer;
                //RebuildRegionsAndRoomsPatch.copyFrom[map.uniqueID] = map.regionGrid;

                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;
            }

            Stopwatch watch = Stopwatch.StartNew();
            XmlDocument gameDoc = SaveGame();
            Log.Message("Saving took " + watch.ElapsedMilliseconds);

            MemoryUtility.ClearAllMapsAndWorld();

            SaveCompression.doSaveCompression = true;

            watch = Stopwatch.StartNew();
            LoadPatch.gameToLoad = gameDoc;
            Prefs.PauseOnLoad = false;
            SavedGameLoaderNow.LoadGameFromSaveFileNow("server");
            Log.Message("Loading took " + watch.ElapsedMilliseconds);

            RealPlayerFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == localFactionId);

            foreach (Map m in Find.Maps)
            {
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned)
                {
                    if (tweenedPos.TryGetValue(p.thingIDNumber, out Vector3 v))
                    {
                        p.drawer.tweener.tweenedPos = v;
                        p.drawer.tweener.lastDrawFrame = Time.frameCount;
                    }
                }
            }

            SaveCompression.doSaveCompression = false;

            reloading = false;

            /*if (serverThread != null)
            {
                Map map = Find.Maps[0];
                List<Region> regions = new List<Region>(map.regionGrid.AllRegions);
                foreach (Region region in regions)
                {
                    region.id = map.cellIndices.CellToIndex(region.AnyCell);
                    region.cachedCellCount = -1;
                    region.precalculatedHashCode = Gen.HashCombineInt(region.id, 1295813358);
                    region.closedIndex = new uint[RegionTraverser.NumWorkers];

                    if (region.Room != null)
                    {
                        region.Room.ID = region.id;
                        region.Room.Group.ID = region.id;
                    }
                }

                TickList[] tickLists = new[] {
                    Find.Maps[0].AsyncTime().tickListLong,
                    Find.Maps[0].AsyncTime().tickListRare,
                    Find.Maps[0].AsyncTime().tickListNormal
                };

                foreach (TickList list in tickLists)
                {
                    for (int i = 0; i < list.thingsToRegister.Count; i++)
                    {
                        list.BucketOf(list.thingsToRegister[i]).Add(list.thingsToRegister[i]);
                    }
                    list.thingsToRegister.Clear();
                    for (int j = 0; j < list.thingsToDeregister.Count; j++)
                    {
                        list.BucketOf(list.thingsToDeregister[j]).Remove(list.thingsToDeregister[j]);
                    }
                    list.thingsToDeregister.Clear();
                }

                StringBuilder builder = new StringBuilder();
                SimpleProfiler.DumpMemory(Find.Maps[0].spawnedThings, builder);
                File.WriteAllText("memory_reload", builder.ToString());
            }*/

            return gameDoc;
        }

        public static void CacheAndSendGameData(XmlDocument doc, bool sendGame = true)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                OnMainThread.cachedMapData[id] = ScribeUtil.XmlToByteArray(mapNode);
            }

            string nonPlayerMaps = "li[components/li/@Class='Multiplayer.Client.MultiplayerMapComp' and components/li/isPlayerHome='False']";
            //mapsNode.SelectAndRemove(nonPlayerMaps);

            byte[] mapData = ScribeUtil.XmlToByteArray(mapsNode["li"], null, true);
            File.WriteAllBytes("map_" + username + ".xml", mapData);
            byte[] compressedMaps = GZipStream.CompressBuffer(mapData);
            // todo send map id
            Client.Send(Packets.CLIENT_AUTOSAVED_DATA, 1, compressedMaps, 0);

            gameNode["visibleMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(doc, null, true);
            OnMainThread.cachedGameData = gameData;

            if (sendGame)
            {
                File.WriteAllBytes("game.xml", gameData);

                byte[] compressedGame = GZipStream.CompressBuffer(gameData);
                Client.Send(Packets.CLIENT_AUTOSAVED_DATA, 0, compressedGame);
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

    public class MultiplayerSession
    {
        public IConnection client;
        public NetManager netClient;
        public ChatWindow chat = new ChatWindow();
        public PacketLogWindow packetLog = new PacketLogWindow();
        public int myFactionId;

        public bool allowSteam;
        public List<CSteamID> pendingSteam = new List<CSteamID>();

        public MultiplayerServer localServer;
        public Thread serverThread;

        public void Stop()
        {
            if (netClient != null)
                netClient.Stop();

            if (localServer != null)
                localServer.running = false;
        }
    }

    public class MultiplayerGame
    {
        public MultiplayerWorldComp worldComp;
        public SharedCrossRefs sharedCrossRefs = new SharedCrossRefs();

        public Faction dummyFaction;
        private Faction myFaction;

        public Faction RealPlayerFaction
        {
            get => myFaction;

            set
            {
                myFaction = value;
                Faction.OfPlayer.def = Multiplayer.factionDef;
                value.def = FactionDefOf.PlayerColony;
                Find.FactionManager.ofPlayer = value;

                worldComp.SetFaction(value);

                foreach (Map m in Find.Maps)
                    m.MpComp().SetFaction(value);
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

    public class ClientJoiningState : MpConnectionState
    {
        public ClientJoiningState(IConnection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, Multiplayer.username);
            connection.Send(Packets.CLIENT_REQUEST_WORLD);
        }

        [PacketHandler(Packets.SERVER_WORLD_DATA)]
        public void HandleWorldData(ByteReader data)
        {
            Connection.State = new ClientPlayingState(Connection);
            Log.Message("Game data size: " + data.GetBytes().Length);

            int factionId = data.ReadInt32();
            Multiplayer.session.myFactionId = factionId;

            int tickUntil = data.ReadInt32();

            int globalCmdsLen = data.ReadInt32();
            List<ScheduledCommand> globalCmds = new List<ScheduledCommand>(globalCmdsLen);
            for (int i = 0; i < globalCmdsLen; i++)
                globalCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            OnMainThread.cachedMapCmds[ScheduledCommand.Global] = globalCmds;

            byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedGameData = worldData;

            List<int> mapIds = new List<int>();
            int maps = data.ReadInt32();
            for (int i = 0; i < maps; i++)
            {
                int mapId = data.ReadInt32();

                int mapCmdsLen = data.ReadInt32();
                List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
                for (int j = 0; j < mapCmdsLen; j++)
                    mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

                OnMainThread.cachedMapCmds[mapId] = mapCmds;

                byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
                OnMainThread.cachedMapData[mapId] = mapData;
                mapIds.Add(mapId);
            }

            ReloadGame(tickUntil, mapIds, () =>
            {
                Multiplayer.Client.Send(Packets.CLIENT_WORLD_LOADED);
            });
        }

        public static void ReloadGame(int tickUntil, List<int> maps, Action done = null)
        {
            XmlDocument gameDoc = ScribeUtil.GetDocument(OnMainThread.cachedGameData);
            XmlNode gameNode = gameDoc.DocumentElement["game"];

            foreach (int map in maps)
            {
                using (XmlReader reader = XmlReader.Create(new MemoryStream(OnMainThread.cachedMapData[map])))
                {
                    XmlNode mapNode = gameDoc.ReadNode(reader);
                    gameNode["maps"].AppendChild(mapNode);

                    if (gameNode["visibleMapIndex"] == null)
                        gameNode.AddNode("visibleMapIndex", map.ToString());
                }
            }

            TickPatch.tickUntil = tickUntil;
            LoadPatch.gameToLoad = gameDoc;

            LongEventHandler.QueueLongEvent(() =>
            {
                MemoryUtility.ClearAllMapsAndWorld();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                Current.Game.InitData.gameToLoad = "server";

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    LongEventHandler.QueueLongEvent(CatchUp(done), "Loading", null);
                });
            }, "Play", "Loading the game", true, null);
        }

        public static IEnumerable CatchUp(Action finishAction)
        {
            FactionWorldData factionData = Multiplayer.WorldComp.factionData.GetValueSafe(Multiplayer.session.myFactionId);
            if (factionData != null && factionData.online)
                Multiplayer.RealPlayerFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == factionData.factionId);
            else
                Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;

            Multiplayer.WorldComp.cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(ScheduledCommand.Global) ?? new List<ScheduledCommand>());
            foreach (Map m in Find.Maps)
                m.AsyncTime().cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(m.uniqueID) ?? new List<ScheduledCommand>());

            int start = TickPatch.Timer;
            int startTicks = Find.Maps[0].AsyncTime().mapTicks;
            float startTime = Time.realtimeSinceStartup;

            while (TickPatch.Timer < TickPatch.tickUntil)
            {
                TickPatch.accumulator = Math.Min(100, TickPatch.tickUntil - TickPatch.Timer);

                SimpleProfiler.Start();
                Multiplayer.simulating = true;
                TickPatch.Tick();
                Multiplayer.simulating = false;
                SimpleProfiler.Pause();

                int pct = (int)((float)(TickPatch.Timer - start) / (TickPatch.tickUntil - start) * 100);
                LongEventHandler.SetCurrentEventText($"Loading game {pct}/100 " + TickPatch.Timer + " " + TickPatch.tickUntil + " " + Find.Maps[0].AsyncTime().mapTicks + " " + (Find.Maps[0].AsyncTime().mapTicks - startTicks) / (Time.realtimeSinceStartup - startTime));

                bool allPaused = TickPatch.AllTickables.All(t => t.CurTimePerTick == 0);
                if (allPaused) break;

                yield return null;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    pawn.drawer.tweener.tweenedPos = pawn.drawer.tweener.TweenedPosRoot();
            }

            SimpleProfiler.Print("prof_sim.txt");
            SimpleProfiler.Init("");

            finishAction();
        }

        public override void Disconnected(string reason)
        {
        }
    }

    public class ClientPlayingState : MpConnectionState
    {
        public ClientPlayingState(IConnection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.SERVER_TIME_CONTROL)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            TickPatch.tickUntil = tickUntil;
        }

        [PacketHandler(Packets.SERVER_KEEP_ALIVE)]
        public void HandleKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            Connection.Send(Packets.CLIENT_KEEP_ALIVE, id);
        }

        [PacketHandler(Packets.SERVER_COMMAND)]
        public void HandleCommand(ByteReader data)
        {
            OnMainThread.ScheduleCommand(ScheduledCommand.Deserialize(data));
        }

        [PacketHandler(Packets.SERVER_PLAYER_LIST)]
        public void HandlePlayerList(ByteReader data)
        {
            string[] playerList = data.ReadPrefixedStrings();
            Multiplayer.Chat.playerList = playerList;
        }

        [PacketHandler(Packets.SERVER_CHAT)]
        public void HandleChat(ByteReader data)
        {
            string username = data.ReadString();
            string msg = data.ReadString();

            Multiplayer.Chat.AddMsg(username + ": " + msg);
        }

        [PacketHandler(Packets.SERVER_MAP_RESPONSE)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            OnMainThread.cachedMapCmds[mapId] = mapCmds;

            byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            OnMainThread.cachedMapData[mapId] = mapData;

            ClientJoiningState.ReloadGame(TickPatch.tickUntil, Find.Maps.Select(m => m.uniqueID).Concat(mapId).ToList());
            // todo Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
        }

        [PacketHandler(Packets.SERVER_NOTIFICATION)]
        public void HandleNotification(ByteReader data)
        {
            string msg = data.ReadString();
            Messages.Message(msg, MessageTypeDefOf.SilentInput);
        }

        public override void Disconnected(string reason)
        {
        }
    }

    public class ClientSteamState : MpConnectionState
    {
        public ClientSteamState(IConnection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_STEAM_REQUEST);
        }

        [PacketHandler(Packets.SERVER_STEAM_ACCEPT)]
        public void HandleSteamAccept(ByteReader data)
        {
            Connection.State = new ClientJoiningState(Connection);
        }

        public override void Disconnected(string reason)
        {
        }
    }

    public class OnMainThread : MonoBehaviour
    {
        public static ActionQueue queue = new ActionQueue();
        public static bool executingCmds;

        public static byte[] cachedGameData;
        public static Dictionary<int, byte[]> cachedMapData = new Dictionary<int, byte[]>();
        public static Dictionary<int, List<ScheduledCommand>> cachedMapCmds = new Dictionary<int, List<ScheduledCommand>>();

        public void Update()
        {
            Multiplayer.session?.netClient?.PollEvents();

            queue.RunQueue();

            if (Multiplayer.Client == null) return;

            if (SteamManager.Initialized)
                UpdateSteam();

            UpdateSync();
        }

        private Stopwatch lastSteamUpdate = Stopwatch.StartNew();

        private void UpdateSteam()
        {
            if (lastSteamUpdate.ElapsedMilliseconds > 1000)
            {
                if (Multiplayer.session.allowSteam)
                    SteamFriends.SetRichPresence("connect", Multiplayer.SteamConnectStart + "" + SteamUser.GetSteamID());
                else
                    SteamFriends.SetRichPresence("connect", null);

                lastSteamUpdate.Restart();
            }

            while (SteamNetworking.IsP2PPacketAvailable(out uint size))
            {
                byte[] data = new byte[size];
                SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote);
                HandleSteamPacket(remote, data);
            }
        }

        private void HandleSteamPacket(CSteamID remote, byte[] data)
        {
            if (Multiplayer.Client is SteamConnection localConn && localConn.remoteId == remote)
                localConn.HandleReceive(data);

            if (Multiplayer.LocalServer == null) return;

            ServerPlayer player = Multiplayer.LocalServer.players.Find(p => p.connection is SteamConnection conn && conn.remoteId == remote);
            if (player != null)
                player.connection.HandleReceive(data);
        }

        private void UpdateSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (!data.sent && Environment.TickCount - data.timestamp > 200)
                    {
                        f.DoSync(k.first, data.toSend, k.second);
                        data.sent = true;
                    }

                    return !Equals(k.first.GetPropertyOrField(f.memberPath, k.second), data.currentValue);
                });
            }
        }

        public void OnApplicationQuit()
        {
            StopMultiplayer();
        }

        public static void StopMultiplayer()
        {
            if (Multiplayer.session != null)
            {
                Multiplayer.session.Stop();
                Multiplayer.session = null;
            }

            if (Find.WindowStack != null)
            {
                Find.WindowStack.WindowOfType<ServerBrowser>()?.PostClose();
            }

            Sync.bufferedChanges.Clear();
            ClearCaches();
        }

        private static void ClearCaches()
        {
            cachedGameData = null;
            cachedMapData.Clear();
            cachedMapCmds.Clear();
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void ScheduleCommand(ScheduledCommand cmd)
        {
            MpLog.Log($"Cmd: {cmd.type}, faction: {cmd.factionId}, map: {cmd.mapId}, ticks: {cmd.ticks}");
            cachedMapCmds.AddOrGet(cmd.mapId, new List<ScheduledCommand>()).Add(cmd);

            if (Current.ProgramState != ProgramState.Playing) return;

            if (cmd.mapId == ScheduledCommand.Global)
                Multiplayer.WorldComp.cmds.Enqueue(cmd);
            else
                cmd.GetMap()?.AsyncTime().cmds.Enqueue(cmd);
        }

        public static void ExecuteMapCmd(ScheduledCommand cmd, ByteReader data)
        {
            Map map = cmd.GetMap();
            if (map == null) return;

            MapAsyncTimeComp mapComp = map.GetComponent<MapAsyncTimeComp>();
            CommandType cmdType = cmd.type;

            executingCmds = true;

            CurrentMapGetPatch.visibleMap = map;
            CurrentMapSetPatch.ignore = true;

            mapComp.PreContext();
            map.PushFaction(cmd.GetFaction());

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
                    mapComp.TimeSpeed = speed;
                }

                if (cmdType == CommandType.MAP_ID_BLOCK)
                {
                    IdBlock block = IdBlock.Deserialize(data);

                    if (map != null)
                    {
                        map.MpComp().mapIdBlock = block;
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
                CurrentMapSetPatch.ignore = false;
                CurrentMapGetPatch.visibleMap = null;
                map.PopFaction();
                mapComp.PostContext();
                executingCmds = false;
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
            MultiplayerMapComp comp = map.MpComp();

            if (!comp.factionMapData.ContainsKey(factionId))
            {
                FactionMapData factionMapData = FactionMapData.New(factionId, map);
                comp.factionMapData[factionId] = factionMapData;

                factionMapData.areaManager.AddStartingAreas();
                map.pawnDestinationReservationManager.RegisterFaction(faction);

                MpLog.Log("New map faction data for {0}", faction.GetUniqueLoadID());
            }
        }

        public static void ExecuteGlobalCmd(ScheduledCommand cmd, ByteReader data)
        {
            Multiplayer.Seed = Find.TickManager.TicksGame;
            CommandType cmdType = cmd.type;

            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;
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

                if (cmdType == CommandType.FACTION_OFFLINE)
                {
                    int factionId = data.ReadInt32();
                    Multiplayer.WorldComp.factionData[factionId].online = false;

                    if (Multiplayer.session.myFactionId == factionId)
                        Multiplayer.RealPlayerFaction = Multiplayer.DummyFaction;
                }

                if (cmdType == CommandType.FACTION_ONLINE)
                {
                    int factionId = data.ReadInt32();
                    Multiplayer.WorldComp.factionData[factionId].online = true;

                    if (Multiplayer.session.myFactionId == factionId)
                        Multiplayer.RealPlayerFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == factionId);
                }

                if (cmdType == CommandType.AUTOSAVE)
                {
                    WorldTimeChangePatch.SetSpeed(TimeSpeed.Paused);

                    LongEventHandler.QueueLongEvent(() =>
                    {
                        ClearCaches();

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
            int factionId = data.ReadInt32();
            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == factionId);

            if (faction == null)
            {
                faction = new Faction
                {
                    loadID = factionId,
                    def = Multiplayer.factionDef,
                    Name = "Multiplayer faction",
                    centralMelanin = Rand.Value
                };

                Find.FactionManager.Add(faction);

                foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
                {
                    if (current == faction) continue;
                    current.TryMakeInitialRelationsWith(faction);
                }

                Multiplayer.WorldComp.factionData[factionId] = FactionWorldData.New(factionId);

                MpLog.Log("New faction {0}", faction.GetUniqueLoadID());
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
                    zone.cellsShuffled = true;
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
                Area area = map.areaManager.AllAreas.Find(a => a.ID == areaId);
                if (area == null) return false;
                Designator_AreaAllowed.selectedArea = area;
            }

            if (designator is Designator_Place place)
            {
                place.placingRot = new Rot4(data.ReadByte());
            }

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
            {
                string thingDefName = data.ReadString();
                ThingDef stuffDef = DefDatabase<ThingDef>.AllDefsListForReading.Find(t => t.defName == thingDefName);
                if (stuffDef == null) return false;
                build.stuffDef = stuffDef;
            }

            if (designator is Designator_Install)
            {
                int thingId = data.ReadInt32();
                Thing thing = map.listerThings.AllThings.Find(t => t.thingIDNumber == thingId);
                if (thing == null) return false;
                DesignatorInstallPatch.thingToInstall = thing;
            }

            return true;
        }
    }

    public class FactionWorldData : IExposable
    {
        public int factionId;
        public bool online;

        public ResearchManager researchManager;
        public DrugPolicyDatabase drugPolicyDatabase;
        public OutfitDatabase outfitDatabase;
        public PlaySettings playSettings;

        public ResearchSpeed researchSpeed;

        public FactionWorldData() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Values.Look(ref online, "online");

            Scribe_Deep.Look(ref researchManager, "researchManager");
            Scribe_Deep.Look(ref drugPolicyDatabase, "drugPolicyDatabase");
            Scribe_Deep.Look(ref outfitDatabase, "outfitDatabase");
            Scribe_Deep.Look(ref playSettings, "playSettings");

            Scribe_Deep.Look(ref researchSpeed, "researchSpeed");
        }

        public void Tick()
        {
        }

        public static FactionWorldData New(int factionId)
        {
            return new FactionWorldData()
            {
                factionId = factionId,

                researchManager = new ResearchManager(),
                drugPolicyDatabase = new DrugPolicyDatabase(),
                outfitDatabase = new OutfitDatabase(),
                playSettings = new PlaySettings(),
                researchSpeed = new ResearchSpeed(),
            };
        }

        public static FactionWorldData FromCurrent()
        {
            return new FactionWorldData()
            {
                factionId = Faction.OfPlayer.loadID,
                online = true,

                researchManager = Find.ResearchManager,
                drugPolicyDatabase = Current.Game.drugPolicyDatabase,
                outfitDatabase = Current.Game.outfitDatabase,
                playSettings = Current.Game.playSettings,

                researchSpeed = new ResearchSpeed(),
            };
        }

        public static XmlDocument ExtractFromGameDoc(XmlDocument gameDoc)
        {
            XmlDocument doc = new XmlDocument();
            doc.AppendChild(doc.CreateElement("factionWorldData"));
            XmlNode root = doc.DocumentElement;

            string[] fromGame = new[] {
                "researchManager",
                "drugPolicyDatabase",
                "outfitDatabase",
                "playSettings",
                "history"
            };

            string[] fromWorld = new[] {
                "settings"
            };

            foreach (string s in fromGame)
                root.AppendChild(doc.ImportNode(gameDoc.DocumentElement["game"][s], true));

            foreach (string s in fromWorld)
                root.AppendChild(doc.ImportNode(gameDoc.DocumentElement["game"]["world"][s], true));

            return doc;
        }
    }

    public class FactionMapData : IExposable
    {
        public Map map;
        public int factionId;

        // Saved
        public DesignationManager designationManager;
        public AreaManager areaManager;
        public ZoneManager zoneManager;

        // Not saved
        public HaulDestinationManager haulDestinationManager;
        public ListerHaulables listerHaulables;
        public ResourceCounter resourceCounter;
        public ListerFilthInHomeArea listerFilthInHomeArea;

        // Loading ctor
        public FactionMapData(Map map)
        {
            this.map = map;

            haulDestinationManager = new HaulDestinationManager(map);
            listerHaulables = new ListerHaulables(map);
            resourceCounter = new ResourceCounter(map);
            listerFilthInHomeArea = new ListerFilthInHomeArea(map);
        }

        private FactionMapData(int factionId, Map map) : this(map)
        {
            this.factionId = factionId;

            designationManager = new DesignationManager(map);
            areaManager = new AreaManager(map);
            zoneManager = new ZoneManager(map);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Deep.Look(ref designationManager, "designationManager", map);
            Scribe_Deep.Look(ref areaManager, "areaManager", map);
            Scribe_Deep.Look(ref zoneManager, "zoneManager", map);
        }

        public static FactionMapData New(int factionId, Map map)
        {
            return new FactionMapData(factionId, map);
        }

        public static FactionMapData FromMap(Map map)
        {
            return new FactionMapData(map)
            {
                factionId = map.ParentFaction.loadID,

                designationManager = map.designationManager,
                areaManager = map.areaManager,
                zoneManager = map.zoneManager,

                haulDestinationManager = map.haulDestinationManager,
                listerHaulables = map.listerHaulables,
                resourceCounter = map.resourceCounter,
                listerFilthInHomeArea = map.listerFilthInHomeArea,
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
                    // todo speed up when nothing is happening
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

        public Queue<ScheduledCommand> Cmds { get => cmds; }

        public Dictionary<int, FactionWorldData> factionData = new Dictionary<int, FactionWorldData>();

        public ConstantTicker ticker = new ConstantTicker();
        public IdBlock globalIdBlock;
        public string worldId = Guid.NewGuid().ToString();
        public int sessionId = new System.Random().Next();
        public TimeSpeed timeSpeedInt;

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public MultiplayerWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            Scribe_Values.Look(ref TickPatch.timerInt, "timer");
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");

            ExposeFactionData();

            Multiplayer.ExposeIdBlock(ref globalIdBlock, "globalIdBlock");
        }

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var factionData = new Dictionary<int, FactionWorldData>(this.factionData);
                factionData.Remove(Multiplayer.DummyFaction.loadID);

                ScribeUtil.Look(ref factionData, "factionData", LookMode.Deep);
            }
            else
            {
                ScribeUtil.Look(ref factionData, "factionData", LookMode.Deep);
                if (factionData == null)
                    factionData = new Dictionary<int, FactionWorldData>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionData[Multiplayer.DummyFaction.loadID] = FactionWorldData.FromCurrent();
            }
        }

        public void Tick()
        {
            tickingWorld = true;
            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;
            Find.TickManager.CurTimeSpeed = timeSpeedInt;

            try
            {
                Find.TickManager.DoSingleTick();
            }
            finally
            {
                UniqueIdsPatch.CurrentBlock = null;
                tickingWorld = false;
            }
        }

        public void SetFaction(Faction faction)
        {
            if (!factionData.TryGetValue(faction.loadID, out FactionWorldData data))
            {
                if (!Multiplayer.simulating)
                    MpLog.Log("No world faction data for faction {0} {1}", faction.loadID, faction);
                return;
            }

            Game game = Current.Game;
            game.researchManager = data.researchManager;
            game.drugPolicyDatabase = data.drugPolicyDatabase;
            game.outfitDatabase = data.outfitDatabase;
            game.playSettings = data.playSettings;

            SyncResearch.researchSpeed = data.researchSpeed;
        }

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            OnMainThread.ExecuteGlobalCmd(cmd, new ByteReader(cmd.data));
        }
    }

    public class MultiplayerMapComp : MapComponent
    {
        public static bool tickingFactions;

        public IdBlock mapIdBlock;
        public Dictionary<int, FactionMapData> factionMapData = new Dictionary<int, FactionMapData>();

        // for SaveCompression
        public List<Thing> loadedThings;

        public MultiplayerMapComp(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Multiplayer.Client == null) return;

            tickingFactions = true;

            foreach (var data in factionMapData)
            {
                map.PushFaction(data.Key);
                data.Value.listerHaulables.ListerHaulablesTick();
                data.Value.resourceCounter.ResourceCounterTick();
                map.PopFaction();
            }

            tickingFactions = false;
        }

        public void SetFaction(Faction faction)
        {
            if (!factionMapData.TryGetValue(faction.loadID, out FactionMapData data))
            {
                if (!Multiplayer.simulating)
                    MpLog.Log("No map faction data for faction {0} on map {1}", faction, map.uniqueID);
                return;
            }

            map.designationManager = data.designationManager;
            map.areaManager = data.areaManager;
            map.zoneManager = data.zoneManager;
            map.haulDestinationManager = data.haulDestinationManager;
            map.listerHaulables = data.listerHaulables;
            map.resourceCounter = data.resourceCounter;
            map.listerFilthInHomeArea = data.listerFilthInHomeArea;
        }

        public override void ExposeData()
        {
            // Data marker
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                bool isPlayerHome = map.IsPlayerHome;
                Scribe_Values.Look(ref isPlayerHome, "isPlayerHome", false, true);
            }

            Multiplayer.ExposeIdBlock(ref mapIdBlock, "mapIdBlock");

            ExposeFactionData();
        }

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int dummyId = Multiplayer.DummyFaction.loadID;
                if (map.areaManager != factionMapData[dummyId].areaManager)
                    Log.Warning("Current map faction data is not dummy faction's data during map saving. This might cause problems.");

                Dictionary<int, FactionMapData> data = new Dictionary<int, FactionMapData>(factionMapData);
                data.Remove(dummyId);
                ScribeUtil.Look(ref data, "factionMapData", LookMode.Deep, map);
            }
            else
            {
                ScribeUtil.Look(ref factionMapData, "factionMapData", LookMode.Deep, map);
                if (factionMapData == null)
                    factionMapData = new Dictionary<int, FactionMapData>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int dummyFaction = Multiplayer.DummyFaction.loadID;
                if (factionMapData.ContainsKey(dummyFaction))
                    Log.Warning("Map's saved faction data includes dummy faction's data.");

                factionMapData[dummyFaction] = FactionMapData.FromMap(map);
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

            stack.Push(Find.FactionManager.OfPlayer);
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
            Find.FactionManager.OfPlayer.def = Multiplayer.factionDef;
            faction.def = FactionDefOf.PlayerColony;
            Find.FactionManager.ofPlayer = faction;
        }
    }

}

