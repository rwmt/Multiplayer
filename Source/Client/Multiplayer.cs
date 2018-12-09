extern alias zip;

using Harmony;
using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
        public static PacketLogWindow PacketLog => session?.packetLog;
        public static bool IsReplay => session?.replay ?? false;

        public static string username;
        public static bool arbiterInstance;
        public static HarmonyInstance harmony => MultiplayerMod.harmony;

        public static bool reloading;

        public static FactionDef FactionDef = FactionDef.Named("MultiplayerColony");
        public static FactionDef DummyFactionDef = FactionDef.Named("MultiplayerDummy");

        public static IdBlock GlobalIdBlock => game.worldComp.globalIdBlock;
        public static Faction DummyFaction => game.dummyFaction;
        public static MultiplayerWorldComp WorldComp => game.worldComp;

        // Null during loading
        public static Faction RealPlayerFaction
        {
            get => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;
            set => game.RealPlayerFaction = value;
        }

        public static bool ExecutingCmds => MultiplayerWorldComp.executingCmdWorld || MapAsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => MultiplayerWorldComp.tickingWorld || MapAsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static Map MapContext => MapAsyncTimeComp.tickingMap ?? MapAsyncTimeComp.executingCmdMap;

        public static bool dontSync;
        public static bool ShouldSync => Client != null && !Ticking && !ExecutingCmds && !reloading && Current.ProgramState == ProgramState.Playing && LongEventHandler.currentEvent == null && !dontSync;

        public static string ReplaysDir => GenFilePaths.FolderUnderSaveData("MpReplays");
        public static string DesyncsDir => GenFilePaths.FolderUnderSaveData("MpDesyncs");

        public static Callback<P2PSessionRequest_t> sessionReqCallback;
        public static Callback<P2PSessionConnectFail_t> p2pFail;
        public static Callback<FriendRichPresenceUpdate_t> friendRchpUpdate;
        public static Callback<GameRichPresenceJoinRequested_t> gameJoinReq;
        public static Callback<PersonaStateChange_t> personaChange;
        public static AppId_t RimWorldAppId;

        public static Stopwatch Clock = Stopwatch.StartNew();

        public const string SteamConnectStart = " -mpserver=";

        static Multiplayer()
        {
            if (GenCommandLine.CommandLineArgPassed("profiler"))
                SimpleProfiler.CheckAvailable();

            MpLog.info = str => Log.Message($"{username} {TickPatch.Timer} {str}");
            MpLog.error = str => Log.Error(str);

            GenCommandLine.TryGetCommandLineArg("username", out username);
            if (username == null)
                username = SteamUtility.SteamPersonaName;
            if (username == "???")
                username = "Player" + Rand.Range(0, 9999);

            SimpleProfiler.Init(username);

            if (SteamManager.Initialized)
                InitSteam();

            Log.Message($"Player's username: {username}");
            Log.Message($"Processor: {SystemInfo.processorType}");

            PlantWindSwayPatch.Init();

            var gameobject = new GameObject();
            gameobject.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(gameobject);

            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientSteam, typeof(ClientSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientJoining, typeof(ClientJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientPlaying, typeof(ClientPlayingState));

            CollectCursorIcons();

            harmony.DoAllMpPatches();

            SyncHandlers.Init();
            Sync.RegisterAllSyncMethods();

            DoPatches();

            Log.messageQueue.maxMessages = 1000;

            HandleCommandLine();
        }

        private static void HandleCommandLine()
        {
            if (GenCommandLine.TryGetCommandLineArg("connect", out string ip))
            {
                int port = MultiplayerServer.DefaultPort;

                var split = ip.Split(':');
                if (split.Length == 0)
                    ip = "127.0.0.1";
                else if (split.Length >= 1)
                    ip = split[0];

                if (split.Length == 2)
                    int.TryParse(split[1], out port);

                if (IPAddress.TryParse(ip, out IPAddress addr))
                    LongEventHandler.QueueLongEvent(() => ClientUtil.TryConnect(addr, port), "Connecting", false, null);
            }

            if (GenCommandLine.CommandLineArgPassed("arbiter"))
            {
                arbiterInstance = true;
                username = "The Arbiter";
                Prefs.VolumeGame = 0;
            }

            if (GenCommandLine.TryGetCommandLineArg("replay", out string replay))
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    Replay.LoadReplay(replay, true, () =>
                    {
                        var rand = Find.Maps.Select(m => m.AsyncTime().randState).Select(s => $"{(uint)s} {s >> 32}");
                        Log.Message($"map rand {rand.ToStringSafeEnumerable()} | {TickPatch.Timer} | {Find.Maps.Select(m => m.AsyncTime().mapTicks).ToStringSafeEnumerable()}");
                    });
                }, "Replay", false, null);
            }
        }

        private static void InitSteam()
        {
            RimWorldAppId = SteamUtils.GetAppID();

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
                EP2PSessionError error = (EP2PSessionError)fail.m_eP2PSessionError;

                if (Client is SteamConnection clientConn && clientConn.remoteId == remoteId)
                {
                    session.disconnectNetReason = error == EP2PSessionError.k_EP2PSessionErrorTimeout ? "Connection timed out" : "Connection error";
                    ConnectionStatusListeners.TryNotifyAll_Disconnected();
                    OnMainThread.StopMultiplayer();
                }

                if (LocalServer == null) return;

                LocalServer.Enqueue(() =>
                {
                    ServerPlayer player = LocalServer.FindPlayer(p => p.conn is SteamConnection conn && conn.remoteId == remoteId);
                    if (player != null)
                        LocalServer.OnDisconnected(player.conn);
                });
            });
        }

        public static XmlDocument SaveGame()
        {
            //SaveCompression.doSaveCompression = true;

            ScribeUtil.StartWritingToDoc();

            Scribe.EnterNode("savegame");
            ScribeMetaHeaderUtility.WriteMetaHeader();
            Scribe.EnterNode("game");
            int currentMapIndex = Current.Game.currentMapIndex;
            Scribe_Values.Look(ref currentMapIndex, "currentMapIndex", -1);
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

            reloading = true;

            WorldGrid worldGridSaved = Find.WorldGrid;
            WorldRenderer worldRendererSaved = Find.World.renderer;
            var tweenedPos = new Dictionary<int, Vector3>();
            var drawers = new Dictionary<int, MapDrawer>();
            int localFactionId = RealPlayerFaction.loadID;
            var mapCmds = new Dictionary<int, Queue<ScheduledCommand>>();

            //RealPlayerFaction = DummyFaction;

            foreach (Map map in Find.Maps)
            {
                drawers[map.uniqueID] = map.mapDrawer;
                //RebuildRegionsAndRoomsPatch.copyFrom[map.uniqueID] = map.regionGrid;

                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    tweenedPos[p.thingIDNumber] = p.drawer.tweener.tweenedPos;

                mapCmds[map.uniqueID] = map.AsyncTime().cmds;
            }

            mapCmds[ScheduledCommand.Global] = WorldComp.cmds;

            Stopwatch watch = Stopwatch.StartNew();
            XmlDocument gameDoc = SaveGame();
            Log.Message("Saving took " + watch.ElapsedMilliseconds);

            MapDrawerRegenPatch.copyFrom = drawers;
            WorldGridCachePatch.copyFrom = worldGridSaved;
            WorldRendererCachePatch.copyFrom = worldRendererSaved;

            LoadInMainThread(gameDoc);

            RealPlayerFaction = Find.FactionManager.GetById(localFactionId);

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

                m.AsyncTime().cmds = mapCmds[m.uniqueID];
            }

            WorldComp.cmds = mapCmds[ScheduledCommand.Global];

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

        public static void LoadInMainThread(XmlDocument gameDoc)
        {
            var watch = Stopwatch.StartNew();
            MemoryUtility.ClearAllMapsAndWorld();

            LoadPatch.gameToLoad = gameDoc;

            CancelRootPlayStartLongEvents.cancel = true;
            Find.Root.Start();
            CancelRootPlayStartLongEvents.cancel = false;

            SavedGameLoaderNow.LoadGameFromSaveFileNow(null);

            Log.Message("Loading took " + watch.ElapsedMilliseconds);
        }

        public static void CacheGameData(XmlDocument doc)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            OnMainThread.cachedMapData.Clear();
            OnMainThread.cachedMapCmds.Clear();

            foreach (XmlNode mapNode in mapsNode)
            {
                int id = int.Parse(mapNode["uniqueID"].InnerText);
                byte[] mapData = ScribeUtil.XmlToByteArray(mapNode);
                OnMainThread.cachedMapData[id] = mapData;
                OnMainThread.cachedMapCmds[id] = new List<ScheduledCommand>(Find.Maps.First(m => m.uniqueID == id).AsyncTime().cmds);
            }

            gameNode["currentMapIndex"].RemoveFromParent();
            mapsNode.RemoveAll();

            byte[] gameData = ScribeUtil.XmlToByteArray(doc);
            OnMainThread.cachedAtTime = TickPatch.Timer;
            OnMainThread.cachedGameData = gameData;
            OnMainThread.cachedMapCmds[ScheduledCommand.Global] = new List<ScheduledCommand>(WorldComp.cmds);
        }

        public static void SendCurrentGameData(bool async)
        {
            var mapsData = new Dictionary<int, byte[]>(OnMainThread.cachedMapData);
            var gameData = OnMainThread.cachedGameData;

            void Send()
            {
                var writer = new ByteWriter();

                writer.WriteInt32(mapsData.Count);
                foreach (var mapData in mapsData)
                {
                    writer.WriteInt32(mapData.Key);
                    writer.WritePrefixedBytes(GZipStream.CompressBuffer(mapData.Value));
                }

                writer.WritePrefixedBytes(GZipStream.CompressBuffer(gameData));

                Client.SendFragmented(Packets.Client_AutosavedData, writer.GetArray());
            };

            if (async)
                ThreadPool.QueueUserWorkItem(c => Send());
            else
                Send();
        }

        private static void DoPatches()
        {
            harmony.PatchAll();

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

                var subSustainerCtor = typeof(SubSustainer).GetConstructor(new[] { typeof(Sustainer), typeof(SubSoundDef) });
                var sampleCtor = typeof(Sample).GetConstructor(new[] { typeof(SubSoundDef) });
                var subSoundPlay = typeof(SubSoundDef).GetMethod("TryPlay");
                var effecterTick = typeof(Effecter).GetMethod("EffectTick");
                var effecterTrigger = typeof(Effecter).GetMethod("Trigger");
                var effecterCleanup = typeof(Effecter).GetMethod("Cleanup");
                var randomBoltMesh = typeof(LightningBoltMeshPool).GetProperty("RandomBoltMesh").GetGetMethod();

                var effectMethods = new MethodBase[] { subSustainerCtor, sampleCtor, subSoundPlay, effecterTick, effecterTrigger, effecterCleanup, randomBoltMesh };
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

            // Full precision floating point saving
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

                var mapInterfaceMethods = new[]
                {
                    nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs),
                    nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs),
                    nameof(MapInterface.HandleMapClicks),
                    nameof(MapInterface.HandleLowPriorityInput),
                    nameof(MapInterface.MapInterfaceUpdate)
                };

                foreach (string m in mapInterfaceMethods)
                    harmony.Patch(AccessTools.Method(typeof(MapInterface), m), setMapTimePrefix, setMapTimePostfix);

                var windowMethods = new[] { "DoWindowContents", "WindowUpdate" };

                foreach (string m in windowMethods)
                    harmony.Patch(typeof(MainTabWindow_Inspect).GetMethod(m), setMapTimePrefix, setMapTimePostfix);

                foreach (Type t in typeof(InspectTabBase).AllSubtypesAndSelf())
                {
                    MethodInfo method = t.GetMethod("FillTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (method != null && !method.IsAbstract)
                        harmony.Patch(method, setMapTimePrefix, setMapTimePostfix);
                }

                harmony.Patch(AccessTools.Method(typeof(SoundRoot), nameof(SoundRoot.Update)), setMapTimePrefix, setMapTimePostfix);
            }
        }

        public static UniqueList<Texture2D> icons = new UniqueList<Texture2D>();
        public static UniqueList<IconInfo> iconInfos = new UniqueList<IconInfo>();

        public class IconInfo
        {
            public bool hasStuff;
        }

        private static void CollectCursorIcons()
        {
            icons.Add(null);
            iconInfos.Add(null);

            foreach (var des in DefDatabase<DesignationCategoryDef>.AllDefsListForReading.SelectMany(c => c.AllResolvedDesignators))
            {
                if (des.icon == null) continue;

                if (icons.Add(des.icon))
                    iconInfos.Add(new IconInfo()
                    {
                        hasStuff = des is Designator_Build build && build.entDef.MadeFromStuff
                    });
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

        public static void HandleReceive(byte[] data, bool reliable)
        {
            try
            {
                Client.HandleReceive(data, reliable);
            }
            catch (Exception e)
            {
                Log.Error($"Exception handling packet by {Client}: {e}");
            }
        }
    }

    public class MultiplayerSession
    {
        public string gameName;

        public IConnection client;
        public NetManager netClient;
        public PacketLogWindow packetLog = new PacketLogWindow();
        public int myFactionId;
        public List<PlayerInfo> players = new List<PlayerInfo>();

        public bool replay;
        public int replayTimerStart = -1;
        public int replayTimerEnd = -1;
        public List<ReplayEvent> events = new List<ReplayEvent>();

        public bool desynced;

        public string disconnectNetReason;
        public string disconnectServerReason;

        public bool allowSteam;
        public List<CSteamID> pendingSteam = new List<CSteamID>();

        public const int MaxMessages = 200;
        public List<ChatMsg> messages = new List<ChatMsg>();
        public bool hasUnread;

        public MultiplayerServer localServer;
        public Thread serverThread;
        public ServerSettings localSettings;

        public Process arbiter;
        public bool ArbiterPlaying => players.Any(p => p.type == PlayerType.Arbiter && p.status == PlayerStatus.Playing);

        public void Stop()
        {
            if (client != null)
                client.State = ConnectionStateEnum.Disconnected;

            if (localServer != null)
            {
                localServer.running = false;
                serverThread.Join();
            }

            if (netClient != null)
                netClient.Stop();

            if (arbiter != null)
            {
                arbiter.TryKill();
                arbiter = null;
            }

            Log.Message("Multiplayer session stopped.");
        }

        public PlayerInfo GetPlayerInfo(int id)
        {
            return players.FirstOrDefault(p => p.id == id);
        }

        public void AddMsg(string msg)
        {
            AddMsg(new ChatMsg_Text(msg, DateTime.Now));
        }

        public void AddMsg(ChatMsg msg)
        {
            var window = Find.WindowStack.WindowOfType<ChatWindow>();
            if (window == null)
                hasUnread = true;
            else
                window.OnChatReceived();

            messages.Add(msg);

            if (messages.Count > MaxMessages)
                messages.RemoveAt(0);
        }
    }

    public class PlayerInfo
    {
        public int id;
        public string username;
        public int latency;
        public PlayerType type;
        public PlayerStatus status;

        public byte cursorSeq;
        public byte map = byte.MaxValue;
        public Vector3 cursor;
        public Vector3 lastCursor;
        public double updatedAt;
        public double lastDelta;
        public byte cursorIcon;

        private PlayerInfo(int id, string username, int latency, PlayerType type)
        {
            this.id = id;
            this.username = username;
            this.latency = latency;
            this.type = type;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerInfo info && info.id == id;
        }

        public override int GetHashCode()
        {
            return id;
        }

        public static PlayerInfo Read(ByteReader data)
        {
            int id = data.ReadInt32();
            string username = data.ReadString();
            int latency = data.ReadInt32();
            var type = (PlayerType)data.ReadByte();
            var status = (PlayerStatus)data.ReadByte();

            return new PlayerInfo(id, username, latency, type)
            {
                status = status
            };
        }
    }

    public class MultiplayerGame
    {
        public SyncInfoBuffer sync = new SyncInfoBuffer();

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
                Faction.OfPlayer.def = Multiplayer.FactionDef;
                value.def = FactionDefOf.PlayerColony;
                Find.FactionManager.ofPlayer = value;

                worldComp.SetFaction(value);

                foreach (Map m in Find.Maps)
                    m.MpComp().SetFaction(value);
            }
        }

        public MultiplayerGame()
        {
            Toils_Ingest.cardinals = GenAdj.CardinalDirections.ToList();
            Toils_Ingest.diagonals = GenAdj.DiagonalDirections.ToList();
            GenAdj.adjRandomOrderList = null;
            CellFinder.mapEdgeCells = null;
            CellFinder.mapSingleEdgeCells = new List<IntVec3>[4];

            TradeSession.trader = null;
            TradeSession.playerNegotiator = null;
            TradeSession.deal = null;
            TradeSession.giftMode = false;

            foreach (var maker in CaptureThingSetMakers.captured)
            {
                if (maker is ThingSetMaker_Nutrition n)
                    n.nextSeed = 1;
                if (maker is ThingSetMaker_MarketValue m)
                    m.nextSeed = 1;
            }

            DebugTools.curTool = null;
            PortraitsCache.Clear();

            Room.nextRoomID = 1;
            Region.nextId = 1;

            ZoneColorUtility.nextGrowingZoneColorIndex = 0;
            ZoneColorUtility.nextStorageZoneColorIndex = 0;

            foreach (var field in typeof(DebugSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (!field.IsLiteral && field.FieldType == typeof(bool))
                    field.SetValue(null, default(bool));

            typeof(DebugSettings).TypeInitializer.Invoke(null, null);
        }
    }

    public struct Container<T>
    {
        private readonly T _value;
        public T Inner => _value;

        public Container(T value)
        {
            _value = value;
        }

        public static implicit operator Container<T>(T value)
        {
            return new Container<T>(value);
        }
    }

    [HotSwappable]
    public class MultiplayerMod : Mod
    {
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");
        public static MpSettings settings;

        public MultiplayerMod(ModContentPack pack) : base(pack)
        {
            EarlyMarkNoInline();
            EarlyPatches();
            EarlyInit();

            settings = GetSettings<MpSettings>();
        }

        private void EarlyMarkNoInline()
        {
            foreach (var type in MpUtil.AllModTypes())
            {
                MpPatchExtensions.DoMpPatches(null, type)?.ForEach(m => MpUtil.MarkNoInlining(m));

                var harmonyMethods = type.GetHarmonyMethods();
                if (harmonyMethods?.Count > 0)
                {
                    var original = MpUtil.GetOriginalMethod(HarmonyMethod.Merge(harmonyMethods));
                    if (original != null)
                        MpUtil.MarkNoInlining(original);
                }
            }
        }

        private void EarlyPatches()
        {
            {
                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CaptureThingSetMakers), "Prefix"));
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_MarketValue)), prefix);
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_Nutrition)), prefix);
            }
        }

        private void EarlyInit()
        {
            foreach (var thingMaker in DefDatabase<ThingSetMakerDef>.AllDefs)
            {
                CaptureThingSetMakers.captured.Add(thingMaker.root);

                if (thingMaker.root is ThingSetMaker_Sum sum)
                    sum.options.Select(o => o.thingSetMaker).Do(CaptureThingSetMakers.captured.Add);

                if (thingMaker.root is ThingSetMaker_Conditional cond)
                    CaptureThingSetMakers.captured.Add(cond.thingSetMaker);

                if (thingMaker.root is ThingSetMaker_RandomOption rand)
                    rand.options.Select(o => o.thingSetMaker).Do(CaptureThingSetMakers.captured.Add);
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.ColumnWidth = 200f;
            listing.CheckboxLabeled("Show player cursors", ref settings.showCursors);
            listing.End();
        }

        public override string SettingsCategory() => "Multiplayer";
    }

    public class MpSettings : ModSettings
    {
        public bool showCursors = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref showCursors, "showCursors", true);
        }
    }

    [HotSwappable]
    public class OnMainThread : MonoBehaviour
    {
        public static ActionQueue queue = new ActionQueue();

        public static int cachedAtTime;
        public static byte[] cachedGameData;
        public static Dictionary<int, byte[]> cachedMapData = new Dictionary<int, byte[]>();

        // Global cmds are -1
        public static Dictionary<int, List<ScheduledCommand>> cachedMapCmds = new Dictionary<int, List<ScheduledCommand>>();

        public void Update()
        {
            Multiplayer.session?.netClient?.PollEvents();

            queue.RunQueue();

            if (Multiplayer.Client == null) return;

            if (SteamManager.Initialized)
                UpdateSteam();

            UpdateSync();

            if (!Multiplayer.arbiterInstance && Application.isFocused && Time.realtimeSinceStartup - lastCursorSend > 0.05f && TickPatch.skipTo < 0)
            {
                lastCursorSend = Time.realtimeSinceStartup;
                SendCursor();
            }
        }

        private byte cursorSeq;
        private float lastCursorSend;

        private void SendCursor()
        {
            var writer = new ByteWriter();
            writer.WriteByte(cursorSeq++);

            if (Find.CurrentMap != null && !WorldRendererUtility.WorldRenderedNow)
            {
                writer.WriteByte((byte)Find.CurrentMap.Index);

                var icon = Find.MapUI?.designatorManager?.SelectedDesignator?.icon;
                int iconId = icon == null ? 0 : !Multiplayer.icons.Contains(icon) ? 0 : Multiplayer.icons.IndexOf(icon);
                writer.WriteByte((byte)iconId);

                writer.WriteShort((short)(UI.MouseMapPosition().x * 10f));
                writer.WriteShort((short)(UI.MouseMapPosition().z * 10f));
            }
            else
            {
                writer.WriteByte(byte.MaxValue);
            }

            Multiplayer.Client.Send(Packets.Client_Cursor, writer.GetArray(), reliable: false);
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

            ReadSteamPackets(0); // Reliable
            ReadSteamPackets(1); // Unreliable
        }

        private void ReadSteamPackets(int channel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, channel))
            {
                byte[] data = new byte[size];
                SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote, channel);
                HandleSteamPacket(remote, data, channel);
            }
        }

        private void HandleSteamPacket(CSteamID remote, byte[] data, int channel)
        {
            if (Multiplayer.Client is SteamConnection localConn && localConn.remoteId == remote)
                Multiplayer.HandleReceive(data, channel == 0);

            if (Multiplayer.LocalServer == null) return;

            Multiplayer.LocalServer.Enqueue(() =>
            {
                ServerPlayer player = Multiplayer.LocalServer.FindPlayer(p => p.conn is SteamConnection conn && conn.remoteId == remote);

                if (player == null)
                {
                    IConnection conn = new SteamConnection(remote);
                    conn.State = ConnectionStateEnum.ServerSteam;
                    player = Multiplayer.LocalServer.OnConnected(conn);
                    player.type = PlayerType.Steam;
                }

                player.HandleReceive(data, channel == 0);
            });
        }

        private void UpdateSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (CheckShouldRemove(f, k, data))
                        return true;

                    if (Utils.MillisNow - data.timestamp > 200)
                    {
                        f.DoSync(k.first, data.toSend, k.second);
                        data.sent = true;
                        data.timestamp = Utils.MillisNow;
                    }

                    return false;
                });
            }
        }

        public static bool CheckShouldRemove(SyncField field, Pair<object, object> target, BufferData data)
        {
            if (Equals(data.toSend, data.actualValue))
                return true;

            object currentValue = target.first.GetPropertyOrField(field.memberPath, target.second);

            if (!Equals(currentValue, data.actualValue))
            {
                if (data.sent)
                    return true;
                else
                    data.actualValue = currentValue;
            }

            return false;
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

            TickPatch.ClearSkipping();
            TickPatch.Timer = 0;
            TickPatch.tickUntil = 0;
            TickPatch.accumulator = 0;

            Find.WindowStack?.WindowOfType<ServerBrowser>()?.Cleanup(true);

            foreach (var entry in Sync.bufferedChanges)
                entry.Value.Clear();

            ClearCaches();

            if (Multiplayer.arbiterInstance)
            {
                Multiplayer.arbiterInstance = false;
                Application.Quit();
            }
        }

        public static void ClearCaches()
        {
            cachedAtTime = 0;
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
            cachedMapCmds.GetOrAddNew(cmd.mapId).Add(cmd);

            if (Current.ProgramState != ProgramState.Playing) return;

            if (cmd.mapId == ScheduledCommand.Global)
                Multiplayer.WorldComp.cmds.Enqueue(cmd);
            else
                cmd.GetMap()?.AsyncTime().cmds.Enqueue(cmd);
        }
    }

    public static class FactionContext
    {
        private static Stack<Faction> stack = new Stack<Faction>();

        public static Faction Push(Faction faction)
        {
            if (faction == null || (faction.def != Multiplayer.FactionDef && faction.def != FactionDefOf.PlayerColony))
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
            Find.FactionManager.OfPlayer.def = Multiplayer.FactionDef;
            faction.def = FactionDefOf.PlayerColony;
            Find.FactionManager.ofPlayer = faction;
        }
    }

    public class ReplayConnection : IConnection
    {
        public override void SendRaw(byte[] raw, bool reliable)
        {
        }

        public override void HandleReceive(byte[] rawData, bool reliable)
        {
        }

        public override void Close()
        {
        }
    }

}

