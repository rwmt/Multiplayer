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
                RandSetSeedPatch.dontLog = false;
            }
        }

        public static bool ExecutingCmds => MultiplayerWorldComp.executingCmdWorld || MapAsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => MultiplayerWorldComp.tickingWorld || MapAsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static bool ShouldSync => Client != null && !Ticking && !ExecutingCmds && !reloading && Current.ProgramState == ProgramState.Playing;

        public static Callback<P2PSessionRequest_t> sessionReqCallback;
        public static Callback<P2PSessionConnectFail_t> p2pFail;
        public static Callback<FriendRichPresenceUpdate_t> friendRchpUpdate;
        public static Callback<GameRichPresenceJoinRequested_t> gameJoinReq;
        public static Callback<PersonaStateChange_t> personaChange;
        public static AppId_t RimWorldAppId;

        public const string SteamConnectStart = " -mpserver=";
        public const string CurrentMapIndexXml = "currentMapIndex";

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
            harmony.DoMpPatches(typeof(MessagesMarker));

            harmony.DoMpPatches(typeof(MainMenuMarker));
            harmony.DoMpPatches(typeof(MainMenuPatch));

            SyncHandlers.Init();

            DoPatches();

            Log.messageQueue.maxMessages = 1000;

            if (GenCommandLine.CommandLineArgPassed("dev"))
            {
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData
                {
                    gameToLoad = "mappo"
                };

                LongEventHandler.QueueLongEvent(null, "Play", "LoadingLongEvent", true, null);
            }
            else if (GenCommandLine.TryGetCommandLineArg("connect", out string ip))
            {
                if (String.IsNullOrEmpty(ip))
                    ip = "127.0.0.1";

                LongEventHandler.QueueLongEvent(() =>
                {
                    IPAddress.TryParse(ip, out IPAddress addr);
                    ClientUtil.TryConnect(addr, MultiplayerServer.DefaultPort);
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
                EP2PSessionError error = (EP2PSessionError)fail.m_eP2PSessionError;

                if (Client is SteamConnection clientConn && clientConn.remoteId == remoteId)
                {
                    session.disconnectNetReason = error == EP2PSessionError.k_EP2PSessionErrorTimeout ? "Connection timed out" : "Connection error";
                    ConnectionStatusListeners.All.Do(a => a.Disconnected());
                    OnMainThread.StopMultiplayer();
                }

                if (LocalServer == null) return;

                ServerPlayer player = LocalServer.players.Find(p => p.connection is SteamConnection conn && conn.remoteId == remoteId);
                if (player != null)
                    LocalServer.OnDisconnected(player.connection);
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
                int currentMapIndex = Current.Game.currentMapIndex;
                Scribe_Values.Look(ref currentMapIndex, CurrentMapIndexXml, -1);
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

            gameNode[CurrentMapIndexXml].RemoveFromParent();
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
                var methods = new[] {
                    "MapInterfaceOnGUI_BeforeMainTabs",
                    "MapInterfaceOnGUI_AfterMainTabs",
                    "HandleMapClicks",
                    "HandleLowPriorityInput",
                    "MapInterfaceUpdate"
                };

                foreach (string m in methods)
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

        public string disconnectNetReason;
        public string disconnectServerReason;

        public bool allowSteam;
        public List<CSteamID> pendingSteam = new List<CSteamID>();

        public MultiplayerServer localServer;
        public Thread serverThread;

        public void Stop()
        {
            if (netClient != null)
                netClient.Stop();

            if (localServer != null)
            {
                localServer.running = false;
                serverThread.Join();
            }
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

    public class OnMainThread : MonoBehaviour
    {
        public static ActionQueue queue = new ActionQueue();

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

        public static void ClearCaches()
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
            cachedMapCmds.GetOrAddDefault(cmd.mapId).Add(cmd);

            if (Current.ProgramState != ProgramState.Playing) return;

            if (cmd.mapId == ScheduledCommand.Global)
                Multiplayer.WorldComp.cmds.Enqueue(cmd);
            else
                cmd.GetMap()?.AsyncTime().cmds.Enqueue(cmd);
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

