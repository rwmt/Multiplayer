using Harmony;
using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
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
    public class Multiplayer
    {
        public static String username;
        public static MultiplayerServer localServer;
        public static Connection client;
        public static ChatWindow chat = new ChatWindow();

        public static bool loadingEncounter;

        public static IdBlock mainBlock;

        public static Faction RealPlayerFaction => WorldComp.playerFactions[username];
        public static MultiplayerWorldComp WorldComp => Find.World.GetComponent<MultiplayerWorldComp>();

        public static FactionDef factionDef = FactionDef.Named("MultiplayerColony");

        public static Map currentMap;

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

        static Multiplayer()
        {
            MpLog.action = str => Log.Message(username + " " + str);

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

            Connection.RegisterState(typeof(ClientPlayingState));
            Connection.RegisterState(typeof(ClientWorldState));

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
                        Multiplayer.client = conn;
                        conn.username = Multiplayer.username;
                        conn.State = new ClientWorldState(conn);
                    }, exception =>
                    {
                        Multiplayer.client = null;
                    });
                }, "Connecting", false, null);
            }
        }

        public static XmlDocument SaveGame()
        {
            bool betterSave = BetterSaver.doBetterSave;
            BetterSaver.doBetterSave = true;

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

            BetterSaver.doBetterSave = betterSave;

            return ScribeUtil.FinishWritingToDoc();
        }

        public static void SendGameData(XmlDocument doc, bool sendGame = true)
        {
            XmlNode gameNode = doc.DocumentElement["game"];
            XmlNode mapsNode = gameNode["maps"];

            string nonPlayerMaps = "li[components/li/@Class='Multiplayer.MultiplayerMapComp' and components/li/isPlayerHome='False']";
            mapsNode.SelectAndRemove(nonPlayerMaps);

            byte[] compressedMaps = GZipStream.CompressBuffer(ScribeUtil.XmlToByteArray(mapsNode, "data"));
            // todo send map id
            Multiplayer.client.Send(Packets.CLIENT_AUTOSAVED_DATA, false, 0, compressedMaps);

            if (sendGame)
            {
                gameNode["visibleMapIndex"].RemoveFromParent();
                mapsNode.RemoveAll();

                byte[] compressedGame = GZipStream.CompressBuffer(ScribeUtil.XmlToByteArray(doc));
                Multiplayer.client.Send(Packets.CLIENT_AUTOSAVED_DATA, true, compressedGame);
            }
        }

        private static void DoPatches()
        {
            var harmony = HarmonyInstance.Create("multiplayer");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            var designatorMethods = new[] { "DesignateSingleCell", "DesignateMultiCell", "DesignateThing" };

            foreach (Type t in typeof(Designator).AllSubtypesAndSelf())
            {
                foreach (string m in designatorMethods)
                {
                    MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    if (method != null)
                        harmony.Patch(method, null, null, new HarmonyMethod(typeof(DesignatorPatches).GetMethod(m + "_Transpiler")));
                }
            }

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

            var thingTickPrefix = new HarmonyMethod(typeof(PatchThingTick).GetMethod("Prefix"));
            var thingTickPostfix = new HarmonyMethod(typeof(PatchThingTick).GetMethod("Postfix"));
            var thingMethods = new[] { "Tick", "TickRare", "TickLong", "SpawnSetup" };

            foreach (Type t in typeof(Thing).AllSubtypesAndSelf())
            {
                foreach (string m in thingMethods)
                {
                    MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    if (method != null)
                        harmony.Patch(method, thingTickPrefix, thingTickPostfix);
                }
            }

            var doubleSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.DoubleSave_Prefix)));
            var floatSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.FloatSave_Prefix)));
            var valueSaveMethod = typeof(Scribe_Values).GetMethod(nameof(Scribe_Values.Look));

            harmony.Patch(valueSaveMethod.MakeGenericMethod(typeof(double)), doubleSavePrefix, null);
            harmony.Patch(valueSaveMethod.MakeGenericMethod(typeof(float)), floatSavePrefix, null);

            var setMapTimePrefix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTime), "Prefix"));
            var setMapTimePostfix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTime), "Postfix"));
            var setMapTime = new[] { "MapInterfaceOnGUI_BeforeMainTabs", "MapInterfaceOnGUI_AfterMainTabs", "HandleMapClicks", "HandleLowPriorityInput", "MapInterfaceUpdate" };

            foreach (string m in setMapTime)
                harmony.Patch(AccessTools.Method(typeof(MapInterface), m), setMapTimePrefix, setMapTimePostfix);
            harmony.Patch(AccessTools.Method(typeof(SoundRoot), "Update"), setMapTimePrefix, setMapTimePostfix);

            var worldGridCtor = AccessTools.Constructor(typeof(WorldGrid));
            harmony.Patch(worldGridCtor, new HarmonyMethod(AccessTools.Method(typeof(WorldGridCtorPatch), "Prefix")), null);

            var worldRendererCtor = AccessTools.Constructor(typeof(WorldRenderer));
            harmony.Patch(worldRendererCtor, new HarmonyMethod(AccessTools.Method(typeof(WorldRendererCtorPatch), "Prefix")), null);
        }

        public static IEnumerable<CodeInstruction> PrefixTranspiler(MethodBase method, ILGenerator gen, IEnumerable<CodeInstruction> inputCode, MethodBase prefix)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(inputCode);
            Label firstInst = gen.DefineLabel();

            list[0].labels.Add(firstInst);

            List<CodeInstruction> newCode = new List<CodeInstruction>();
            newCode.Add(new CodeInstruction(OpCodes.Ldarg_0));
            for (int i = 0; i < method.GetParameters().Length; i++)
                newCode.Add(new CodeInstruction(OpCodes.Ldarg, 1 + i));
            newCode.Add(new CodeInstruction(OpCodes.Call, prefix));
            newCode.Add(new CodeInstruction(OpCodes.Brtrue, firstInst));
            newCode.Add(new CodeInstruction(OpCodes.Ret));

            list.InsertRange(0, newCode);

            return list;
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

    public class ClientWorldState : ConnectionState
    {
        public ClientWorldState(Connection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, Multiplayer.username);
            connection.Send(Packets.CLIENT_REQUEST_WORLD);
        }

        [PacketHandler(Packets.SERVER_DISCONNECT_REASON)]
        public void HandleDisconnectReason(ByteReader data)
        {
            string reason = data.ReadString();

            ConnectingWindow window = Find.WindowStack.WindowOfType<ConnectingWindow>();
            if (window != null)
                window.text = reason;
        }

        [PacketHandler(Packets.SERVER_WORLD_DATA)]
        [HandleImmediately]
        public void HandleWorldData_ChangeState(ByteReader data)
        {
            Connection.State = new ClientPlayingState(Connection);

            OnMainThread.Enqueue(() =>
            {
                int tickUntil = data.ReadInt();

                int cmdsLen = data.ReadInt();
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
                        OnMainThread.ExecuteCommands();
                    }
                    // nothing to do, the game is currently paused
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

        public override void Disconnected()
        {
        }
    }

    public class ClientPlayingState : ConnectionState
    {
        public ClientPlayingState(Connection connection) : base(connection)
        {
        }

        [PacketHandler(Packets.SERVER_COMMAND)]
        public void HandleCommand(ByteReader data)
        {
            HandleCmdSchedule(data);

            MpLog.Log("Client command");
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
            int cmdsLen = data.ReadInt();
            byte[][] cmds = new byte[cmdsLen][];
            for (int i = 0; i < cmdsLen; i++)
                cmds[i] = data.ReadPrefixedBytes();

            byte[] compressedMapData = data.ReadPrefixedBytes();
            byte[] mapData = GZipStream.UncompressBuffer(compressedMapData);

            LongEventHandler.QueueLongEvent(() =>
            {
                Multiplayer.loadingEncounter = true;
                Current.ProgramState = ProgramState.MapInitializing;
                Multiplayer.Seed = Find.TickManager.TicksGame;
                BetterSaver.doBetterSave = true;

                ScribeUtil.StartLoading(mapData);
                ScribeUtil.SupplyCrossRefs();
                List<Map> maps = null;
                Scribe_Collections.Look(ref maps, "maps", LookMode.Deep);
                ScribeUtil.FinishLoading();

                MpLog.Log("maps " + maps.Count);
                Map map = maps[0];

                Current.Game.AddMap(map);
                map.FinalizeLoading();

                BetterSaver.doBetterSave = false;
                Multiplayer.loadingEncounter = false;
                Current.ProgramState = ProgramState.Playing;

                Find.World.renderer.wantedMode = WorldRenderMode.None;
                Current.Game.VisibleMap = map;

                MapAsyncTimeComp asyncTime = map.GetComponent<MapAsyncTimeComp>();

                foreach (byte[] cmd in cmds)
                    HandleCmdSchedule(new ByteReader(cmd));

                Faction ownerFaction = map.info.parent.Faction;

                mapCatchupStart = TickPatch.Timer;
                Log.Message("Map catchup start " + mapCatchupStart + " " + TickPatch.tickUntil + " " + Find.TickManager.TicksGame + " " + Find.TickManager.CurTimeSpeed);

                map.PushFaction(ownerFaction);

                while (asyncTime.Timer < TickPatch.tickUntil)
                {
                    if (asyncTime.TickRateMultiplier > 0)
                    {
                        asyncTime.Tick();
                    }
                    else if (asyncTime.scheduledCmds.Count > 0)
                    {
                        asyncTime.PreContext();
                        asyncTime.ExecuteCommands();
                        asyncTime.PostContext();
                    }
                    else
                    {
                        break;
                    }
                }

                map.PopFaction();

                Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
            }, "Loading the map", false, null);

            /*MultiplayerWorldComp worldComp = Find.World.GetComponent<MultiplayerWorldComp>();
            MultiplayerMapComp mapComp = map.GetComponent<MultiplayerMapComp>();

            string ownerFactionId = ownerFaction.GetUniqueLoadID();
            string playerFactionId = Multiplayer.RealPlayerFaction.GetUniqueLoadID();

            if (playerFactionId != ownerFactionId)
            {
                map.areaManager = new AreaManager(map);
                map.areaManager.AddStartingAreas();
                mapComp.factionAreas[playerFactionId] = map.areaManager;

                map.designationManager = new DesignationManager(map);
                mapComp.factionDesignations[playerFactionId] = map.designationManager;

                map.slotGroupManager = new SlotGroupManager(map);
                mapComp.factionSlotGroups[playerFactionId] = map.slotGroupManager;

                map.zoneManager = new ZoneManager(map);
                mapComp.factionZones[playerFactionId] = map.zoneManager;

                map.listerHaulables = new ListerHaulables(map);
                mapComp.factionHaulables[playerFactionId] = map.listerHaulables;

                map.resourceCounter = new ResourceCounter(map);
                mapComp.factionResources[playerFactionId] = map.resourceCounter;

                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (!(t is ThingWithComps thing)) continue;

                    CompForbiddable forbiddable = thing.GetComp<CompForbiddable>();
                    if (forbiddable == null) continue;

                    bool ownerValue = forbiddable.Forbidden;

                    t.SetForbidden(true, false);

                    thing.GetComp<MultiplayerThingComp>().factionForbidden[ownerFactionId] = ownerValue;
                }
            }*/
        }

        [PacketHandler(Packets.SERVER_NEW_ID_BLOCK)]
        public void HandleNewIdBlock(ByteReader data)
        {
            IdBlock block = IdBlock.Deserialize(data);

            if (block.mapTile != -1)
                Find.WorldObjects.MapParentAt(block.mapTile).Map.GetComponent<MultiplayerMapComp>().encounterIdBlock = block;
            else
                Multiplayer.mainBlock = block;
        }

        [PacketHandler(Packets.SERVER_TIME_CONTROL)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt();
            TickPatch.tickUntil = tickUntil;
        }

        [PacketHandler(Packets.SERVER_NOTIFICATION)]
        public void HandleNotification(ByteReader data)
        {
            string msg = data.ReadString();
            Messages.Message(msg, MessageTypeDefOf.SilentInput);
        }

        [PacketHandler(Packets.SERVER_NEW_FACTION_REQUEST)]
        public void HandleNewFactionRequest(ByteReader data)
        {
            string username = data.ReadString();

            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
            byte[] packetData;

            if (!comp.playerFactions.TryGetValue(username, out Faction faction))
            {
                faction = FactionGenerator.NewGeneratedFaction(FactionDefOf.PlayerColony);
                faction.Name = username + "'s faction";
                faction.def = Multiplayer.factionDef;

                packetData = NetworkServer.GetBytes(username, ScribeUtil.WriteExposable(faction));
            }
            else
            {
                packetData = NetworkServer.GetBytes(username, new byte[0]);
            }

            Multiplayer.client.Send(Packets.CLIENT_NEW_FACTION_RESPONSE, packetData);
        }

        public override void Disconnected()
        {
        }

        public static void HandleCmdSchedule(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt();
            int ticks = data.ReadInt();
            int mapId = data.ReadInt();
            byte[] extraBytes = data.ReadPrefixedBytes();

            ScheduledCommand schdl = new ScheduledCommand(cmd, ticks, mapId, extraBytes);
            OnMainThread.ScheduleCommand(schdl);
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
        private static ActionQueue queue = new ActionQueue();
        public static readonly Queue<ScheduledCommand> scheduledCmds = new Queue<ScheduledCommand>();

        private static readonly FieldInfo zoneShuffled = typeof(Zone).GetField("cellsShuffled", BindingFlags.NonPublic | BindingFlags.Instance);

        public void Update()
        {
            try
            {
                queue.RunQueue();
            }
            catch (Exception e)
            {
                MpLog.LogLines("Exception while executing client action queue", e.ToString());
            }

            if (Multiplayer.client == null) return;

            if (!LongEventHandler.ShouldWaitForEvent && Current.Game != null && Find.World != null)
                ExecuteCommands();
        }

        public static void ExecuteCommands()
        {
            while (scheduledCmds.Count > 0 && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
            {
                ScheduledCommand cmd = scheduledCmds.Dequeue();
                ExecuteServerCmd(cmd, new ByteReader(cmd.data));
            }
        }

        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public static void ScheduleCommand(ScheduledCommand cmd)
        {
            if (cmd.mapId == -1)
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

        public static void ExecuteServerCmd(ScheduledCommand cmd, ByteReader data)
        {
            Multiplayer.Seed = Find.TickManager.TicksGame;

            CommandType cmdType = cmd.type;

            if (cmdType == CommandType.WORLD_TIME_SPEED)
            {
                TimeSpeed prevSpeed = Find.TickManager.CurTimeSpeed;
                TimeSpeed speed = (TimeSpeed)data.ReadByte();
                TimeChangePatch.SetSpeed(speed);

                MpLog.Log("Set speed " + speed + " " + TickPatch.Timer + " " + Find.TickManager.TicksGame);

                if (prevSpeed == TimeSpeed.Paused)
                    TickPatch.timerInt = cmd.ticks;
            }

            if (cmdType == CommandType.NEW_FACTION)
            {
                string username = data.ReadString();
                byte[] factionData = data.ReadPrefixedBytes();

                Faction newFaction = ScribeUtil.ReadExposable<Faction>(factionData);
                MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();

                Find.FactionManager.Add(newFaction);
                comp.playerFactions[username] = newFaction;

                foreach (Faction current in Find.FactionManager.AllFactionsListForReading)
                {
                    if (current == newFaction) continue;
                    current.TryMakeInitialRelationsWith(newFaction);
                }

                if (Multiplayer.username == username)
                {
                    Faction.OfPlayer.def = Multiplayer.factionDef;
                    newFaction.def = FactionDefOf.PlayerColony;
                }
            }

            if (cmdType == CommandType.AUTOSAVE)
            {
                TimeChangePatch.SetSpeed(TimeSpeed.Paused);

                LongEventHandler.QueueLongEvent(() =>
                {
                    BetterSaver.doBetterSave = true;

                    WorldGridCtorPatch.copyFrom = Find.WorldGrid;
                    WorldRendererCtorPatch.copyFrom = Find.World.renderer;

                    XmlDocument gameDoc = Multiplayer.SaveGame();
                    LoadPatch.gameToLoad = gameDoc;

                    MemoryUtility.ClearAllMapsAndWorld();

                    Prefs.PauseOnLoad = false;
                    SavedGameLoader.LoadGameFromSaveFile("server");
                    BetterSaver.doBetterSave = false;

                    Multiplayer.SendGameData(gameDoc);
                }, "Autosaving", false, null);

                // todo unpause after everyone completes the autosave
            }

            if (cmdType == CommandType.MAP_TIME_SPEED)
            {
                Map map = cmd.GetMap();
                if (map == null) return;

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
                Map map = cmd.GetMap();

                if (map != null)
                {
                    map.GetComponent<MultiplayerMapComp>().encounterIdBlock = block;
                    Log.Message(Multiplayer.username + "encounter id block set");
                }
            }

            if (cmdType == CommandType.DESIGNATOR)
            {
                HandleDesignator(cmd, data);
            }

            if (cmdType == CommandType.ORDER_JOB)
            {
                HandleOrderJob(cmd, data);
            }

            if (cmdType == CommandType.DELETE_ZONE)
            {
                Map map = cmd.GetMap();
                if (map == null) return;

                string factionId = data.ReadString();
                string zoneId = data.ReadString();

                map.PushFaction(factionId);
                map.zoneManager.AllZones.FirstOrDefault(z => z.label == zoneId)?.Delete();
                map.PopFaction();
            }

            if (cmdType == CommandType.SPAWN_PAWN)
            {
                Map map = cmd.GetMap();
                if (map == null) return;

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
                Map map = cmd.GetMap();
                if (map == null) return;

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

            if (cmdType == CommandType.DRAFT_PAWN)
            {
                Map map = cmd.GetMap();
                if (map == null) return;

                string pawnId = data.ReadString();
                bool draft = data.ReadBool();

                Pawn pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.GetUniqueLoadID() == pawnId);
                if (pawn == null) return;

                DraftSetPatch.dontHandle = true;
                map.PushFaction(pawn.Faction);
                pawn.drafter.Drafted = draft;
                map.PopFaction();
                DraftSetPatch.dontHandle = false;
            }
        }

        private static void HandleOrderJob(ScheduledCommand command, ByteReader data)
        {
            Map map = command.GetMap();
            if (map == null) return;

            string pawnId = data.ReadString();
            Pawn pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.GetUniqueLoadID() == pawnId);
            if (pawn == null) return;

            Job job = ScribeUtil.ReadExposable<Job>(data.ReadPrefixedBytes());
            job.playerForced = true;

            if (pawn.jobs.curJob != null && pawn.jobs.curJob.JobIsSameAs(job)) return;

            bool shouldQueue = data.ReadBool();
            int mode = data.ReadInt();

            map.PushFaction(pawn.Faction);

            if (mode == 0)
            {
                JobTag tag = (JobTag)data.ReadByte();
                OrderJob(pawn, job, tag, shouldQueue);
            }
            else if (mode == 1)
            {
                string defName = data.ReadString();
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.AllDefs.FirstOrDefault(d => d.defName == defName);
                IntVec3 cell = map.cellIndices.IndexToCell(data.ReadInt());

                if (OrderJob(pawn, job, workGiver.tagToGive, shouldQueue))
                {
                    pawn.mindState.lastGivenWorkType = workGiver.workType;
                    if (workGiver.prioritizeSustains)
                        pawn.mindState.priorityWork.Set(cell, workGiver.workType);
                }
            }

            map.PopFaction();
        }

        private static bool OrderJob(Pawn pawn, Job job, JobTag tag, bool shouldQueue)
        {
            bool interruptible = pawn.jobs.IsCurrentJobPlayerInterruptible();
            bool idle = pawn.mindState.IsIdle || pawn.CurJob == null || pawn.CurJob.def.isIdle;

            if (interruptible || (!shouldQueue && idle))
            {
                pawn.stances.CancelBusyStanceSoft();
                pawn.jobs.ClearQueuedJobs();

                if (job.TryMakePreToilReservations(pawn))
                {
                    pawn.jobs.jobQueue.EnqueueFirst(job, new JobTag?(tag));
                    if (pawn.jobs.curJob != null)
                        pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced);
                    else
                        pawn.jobs.CheckForJobOverride();
                    return true;
                }

                pawn.ClearReservationsForJob(job);
                return false;
            }
            else if (shouldQueue)
            {
                if (job.TryMakePreToilReservations(pawn))
                {
                    pawn.jobs.jobQueue.EnqueueLast(job, new JobTag?(tag));
                    return true;
                }

                pawn.ClearReservationsForJob(job);
                return false;
            }

            pawn.jobs.ClearQueuedJobs();
            if (job.TryMakePreToilReservations(pawn))
            {
                pawn.jobs.jobQueue.EnqueueLast(job, new JobTag?(tag));
                return true;
            }

            pawn.ClearReservationsForJob(job);
            return false;
        }

        private static void HandleDesignator(ScheduledCommand command, ByteReader data)
        {
            Map map = command.GetMap();
            if (map == null) return;

            int mode = data.ReadInt();
            string desName = data.ReadString();
            string buildDefName = data.ReadString();
            Designator designator = GetDesignator(desName, buildDefName);
            if (designator == null) return;

            string factionId = data.ReadString();

            map.PushFaction(factionId);
            Multiplayer.currentMap = map;

            VisibleMapGetPatch.visibleMap = map;
            VisibleMapSetPatch.ignore = true;

            try
            {
                if (SetDesignatorState(map, designator, data))
                {
                    if (mode == 0)
                    {
                        IntVec3 cell = map.cellIndices.IndexToCell(data.ReadInt());
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
                        zoneShuffled.SetValue(zone, true);
                }
            }
            finally
            {
                DesignatorInstallPatch.thingToInstall = null;
                VisibleMapSetPatch.ignore = false;
                VisibleMapGetPatch.visibleMap = null;

                Multiplayer.currentMap = null;
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

            return tickingWorld;
        }

        static void Postfix()
        {
            if (!tickingWorld) return;

            TickManager tickManager = Find.TickManager;

            while (OnMainThread.scheduledCmds.Count > 0 && OnMainThread.scheduledCmds.Peek().ticks == Timer)
            {
                ScheduledCommand cmd = OnMainThread.scheduledCmds.Dequeue();
                OnMainThread.ExecuteServerCmd(cmd, new ByteReader(cmd.data));
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

        public FactionWorldData()
        {
            researchManager = new ResearchManager();
            drugPolicyDatabase = new DrugPolicyDatabase();
            outfitDatabase = new OutfitDatabase();
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref researchManager, "researchManager");
            Scribe_Deep.Look(ref drugPolicyDatabase, "drugPolicyDatabase");
            Scribe_Deep.Look(ref outfitDatabase, "outfitDatabase");
        }
    }

    public class MultiplayerWorldComp : WorldComponent
    {
        public string worldId = Guid.NewGuid().ToString();
        public int sessionId = new System.Random().Next();
        public Dictionary<string, Faction> playerFactions = new Dictionary<string, Faction>();

        public Dictionary<string, FactionWorldData> factionData = new Dictionary<string, FactionWorldData>();

        private List<string> keyWorkingList;
        private List<Faction> valueWorkingList;

        public MultiplayerWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            ScribeUtil.Look(ref playerFactions, "playerFactions", LookMode.Value, LookMode.Reference, ref keyWorkingList, ref valueWorkingList);
            Scribe_Values.Look(ref TickPatch.timerInt, "timer");
            Scribe_Values.Look(ref sessionId, "sessionId");

            TimeSpeed timeSpeed = Find.TickManager.CurTimeSpeed;
            Scribe_Values.Look(ref timeSpeed, "timeSpeed");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Find.TickManager.CurTimeSpeed = timeSpeed;
        }

        public string GetUsernameByFaction(Faction faction)
        {
            return playerFactions.FirstOrDefault(pair => pair.Value == faction).Key;
        }
    }

    public class MultiplayerMapComp : MapComponent
    {
        public bool inEncounter;
        public IdBlock encounterIdBlock;

        public Dictionary<string, SlotGroupManager> factionSlotGroups = new Dictionary<string, SlotGroupManager>();
        public Dictionary<string, ZoneManager> factionZones = new Dictionary<string, ZoneManager>();
        public Dictionary<string, AreaManager> factionAreas = new Dictionary<string, AreaManager>();
        public Dictionary<string, DesignationManager> factionDesignations = new Dictionary<string, DesignationManager>();
        public Dictionary<string, ListerHaulables> factionHaulables = new Dictionary<string, ListerHaulables>();
        public Dictionary<string, ResourceCounter> factionResources = new Dictionary<string, ResourceCounter>();

        // for BetterSaver
        public List<Thing> loadedThings;

        public static bool tickingFactions;

        public MultiplayerMapComp(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            string ownerFactionId = map.ParentFaction.GetUniqueLoadID();

            factionAreas[ownerFactionId] = map.areaManager;
            factionDesignations[ownerFactionId] = map.designationManager;
            factionSlotGroups[ownerFactionId] = map.slotGroupManager;
            factionZones[ownerFactionId] = map.zoneManager;
            factionHaulables[ownerFactionId] = map.listerHaulables;
            factionResources[ownerFactionId] = map.resourceCounter;
        }

        public override void MapComponentTick()
        {
            if (Multiplayer.client == null) return;

            tickingFactions = true;

            foreach (KeyValuePair<string, ListerHaulables> p in factionHaulables)
            {
                map.PushFaction(p.Key);
                p.Value.ListerHaulablesTick();
                map.PopFaction();
            }

            foreach (KeyValuePair<string, ResourceCounter> p in factionResources)
            {
                map.PushFaction(p.Key);
                p.Value.ResourceCounterTick();
                map.PopFaction();
            }

            tickingFactions = false;
        }

        public void SetFaction(Faction faction)
        {
            if (faction == null) return;

            string factionId = faction.GetUniqueLoadID();
            if (!factionAreas.ContainsKey(factionId)) return;

            map.designationManager = factionDesignations.GetValueSafe(factionId);
            map.areaManager = factionAreas.GetValueSafe(factionId);
            map.zoneManager = factionZones.GetValueSafe(factionId);
            map.slotGroupManager = factionSlotGroups.GetValueSafe(factionId);
            map.listerHaulables = factionHaulables.GetValueSafe(factionId);
            map.resourceCounter = factionResources.GetValueSafe(factionId);
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref encounterIdBlock, "encounterIdBlock");

            // saving indicator
            bool isPlayerHome = map.IsPlayerHome;
            Scribe_Values.Look(ref isPlayerHome, "isPlayerHome", false, true);
        }
    }

    public static class FactionContext
    {
        public static Stack<Faction> stack = new Stack<Faction>();

        public static Faction Push(Faction faction)
        {
            if (faction == null || faction.def != Multiplayer.factionDef)
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

    public class MultiplayerThingComp : ThingComp
    {
        private bool homeThisTick;
        private string zoneName;

        public Dictionary<string, bool> factionForbidden = new Dictionary<string, bool>();

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned) return null;

            MultiplayerMapComp comp = parent.Map.GetComponent<MultiplayerMapComp>();

            string forbidden = "";
            foreach (KeyValuePair<string, bool> p in factionForbidden)
            {
                forbidden += p.Key + ":" + p.Value + ";";
            }

            return (
                "At home: " + homeThisTick + "\n" +
                "Zone name: " + zoneName + "\n" +
                "Forbidden: " + forbidden
                ).Trim();
        }

        public override void CompTick()
        {
            if (!parent.Spawned) return;

            homeThisTick = parent.Map.areaManager.Home[parent.Position];
            zoneName = parent.Map.zoneManager.ZoneAt(parent.Position)?.label;
        }

    }

}

