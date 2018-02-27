using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;
using Verse.Sound;

namespace Multiplayer
{
    [StaticConstructorOnStartup]
    public class Multiplayer
    {
        public const int DEFAULT_PORT = 30502;

        public static String username;
        public static Server server;
        public static Connection client;
        public static Connection localServerConnection;
        public static int scheduledActionDelay = 15; // in ticks

        public static byte[] savedWorld;
        public static byte[] mapsData;
        public static bool savingForEncounter;
        public static bool loadingEncounter;

        public static IdBlock mainBlock;

        public static int highestUniqueId = -1;

        public static Faction RealPlayerFaction => WorldComp.playerFactions[username];
        public static MultiplayerWorldComp WorldComp => Find.World.GetComponent<MultiplayerWorldComp>();

        public static FactionDef factionDef = FactionDef.Named("MultiplayerColony");

        public static Map currentMap;

        public static int Seed
        {
            set
            {
                RandSetSeedPatch.ignore = true;

                Rand.Seed = value;
                // it seems that the devs sometimes accidentally import UnityEngine.Random instead of Verse.Rand 
                UnityEngine.Random.InitState(value);

                RandSetSeedPatch.ignore = false;
            }
        }

        static Multiplayer()
        {
            GenCommandLine.TryGetCommandLineArg("username", out username);
            if (username == null)
                username = SteamUtility.SteamPersonaName;
            if (username == "???")
                username = "Player" + Rand.Range(0, 9999);

            Log.Message("Player's username: " + username);
            Log.Message("Processor: " + SystemInfo.processorType);

            //mono_profiler_load(@"default:time,stat,jit,file=f:\rimworld-prof.mprf");

            var gameobject = new GameObject();
            gameobject.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(gameobject);

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
                    Client.TryConnect(addr, Multiplayer.DEFAULT_PORT, (conn, e) =>
                    {
                        if (e != null)
                        {
                            Multiplayer.client = null;
                            return;
                        }

                        Multiplayer.client = conn;
                        conn.username = Multiplayer.username;
                        conn.SetState(new ClientWorldState(conn));
                    });
                }, "Connecting", false, null);
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

            /*var exposablePrefix = new HarmonyMethod(typeof(ExposableProfiler).GetMethod("Prefix"));
            var exposablePostfix = new HarmonyMethod(typeof(ExposableProfiler).GetMethod("Postfix"));

            foreach (Type t in typeof(IExposable).AllImplementing())
            {
                MethodInfo method = t.GetMethod("ExposeData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (method != null && !method.IsAbstract && !method.DeclaringType.IsGenericTypeDefinition && method.DeclaringType.Name != "Map")
                {
                   /Log.Message("exposable: " + t.FullName);
                    harmony.Patch(method, exposablePrefix, exposablePostfix);
                }
            }*/
        }

        public static IdBlock NextIdBlock()
        {
            int blockSize = 25000;
            int blockStart = highestUniqueId;
            highestUniqueId = highestUniqueId + blockSize;
            Log.Message("New id block " + blockStart + " of size " + blockSize);

            return new IdBlock(blockStart, blockSize);
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

    public static class ExposableProfiler
    {
        public static Dictionary<Type, List<double>> timing = new Dictionary<Type, List<double>>();
        public static List<long> stack = new List<long>();

        public static void Prefix(IExposable __instance)
        {
            stack.Add(Stopwatch.GetTimestamp());
        }

        public static void Postfix(IExposable __instance)
        {
            if (!timing.TryGetValue(__instance.GetType(), out List<double> list))
            {
                list = new List<double>();
                timing[__instance.GetType()] = list;
            }

            list.Add(Stopwatch.GetTimestamp() - stack.Last());
            stack.RemoveLast();
        }
    }

    public class IdBlock : IExposable
    {
        public int blockStart;
        public int blockSize;
        public int mapTile = -1; // for encounters

        public int current;

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
                Multiplayer.client.Send(Packets.CLIENT_ID_BLOCK_REQUEST, new object[] { mapTile });
                Log.Message("Sent id block request at " + current);
            }

            return current;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref blockStart, "blockStart");
            Scribe_Values.Look(ref blockSize, "blockSize");
            Scribe_Values.Look(ref mapTile, "mapTile");

            if (Scribe.mode == LoadSaveMode.LoadingVars)
                current = blockStart;
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
        public const int CLIENT_MAP_STATE_DEBUG = 10;

        public const int SERVER_WORLD_DATA = 0;
        public const int SERVER_ACTION_SCHEDULE = 1;
        public const int SERVER_NEW_FACTION = 2;
        public const int SERVER_NEW_WORLD_OBJ = 3;
        public const int SERVER_MAP_REQUEST = 4;
        public const int SERVER_MAP_RESPONSE = 5;
        public const int SERVER_NOTIFICATION = 6;
        public const int SERVER_NEW_ID_BLOCK = 7;
        public const int SERVER_TIME_CONTROL = 8;
    }

    public enum ServerAction
    {
        TIME_SPEED, MAP_ID_BLOCK, DRAFT, FORBID, DESIGNATOR, ORDER_JOB, DELETE_ZONE,
        LONG_ACTION_SCHEDULE, LONG_ACTION_END,
        SPAWN_PAWN
    }

    public class ScheduledServerAction
    {
        public readonly ServerAction action;
        public readonly int ticks;
        public readonly byte[] data;

        public ScheduledServerAction(ServerAction action, int ticks, byte[] data)
        {
            this.ticks = ticks;
            this.action = action;
            this.data = data;
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
                    Multiplayer.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_SCHEDULE, extra), Connection);
                });
            }
            else if (id == Packets.CLIENT_WORLD_LOADED)
            {
                Connection.SetState(new ServerPlayingState(this.Connection));

                OnMainThread.Enqueue(() =>
                {
                    Multiplayer.savedWorld = null;

                    byte[] extra = ScribeUtil.WriteSingle(OnMainThread.currentLongAction);
                    Multiplayer.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, ServerPlayingState.GetServerActionMsg(ServerAction.LONG_ACTION_END, extra));

                    Log.Message("World sending finished");
                });
            }
            else if (id == Packets.CLIENT_USERNAME)
            {
                OnMainThread.Enqueue(() => Connection.username = data.ReadString());
            }
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
                    byte[] extra = data.ReadPrefixedBytes();

                    Multiplayer.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(action, extra));
                });
            }
            else if (id == Packets.CLIENT_NEW_WORLD_OBJ)
            {
                Multiplayer.server.SendToAll(Packets.SERVER_NEW_WORLD_OBJ, data.GetBytes(), Connection);
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
                    Log.Message("Encounter request");

                    int tile = data.ReadInt();
                    Settlement settlement = Find.WorldObjects.SettlementAt(tile);
                    if (settlement == null) return;
                    Faction faction = settlement.Faction;
                    string defender = Find.World.GetComponent<MultiplayerWorldComp>().GetUsername(faction);
                    if (defender == null) return;
                    Connection conn = Multiplayer.server.GetByUsername(defender);
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
                    LongActionEncounter encounter = ((LongActionEncounter)OnMainThread.currentLongAction);

                    Connection attacker = Multiplayer.server.GetByUsername(encounter.attacker);
                    Connection defender = Multiplayer.server.GetByUsername(encounter.defender);

                    defender.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());
                    attacker.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());

                    encounter.waitingFor.Add(defender.username);
                    encounter.waitingFor.Add(attacker.username);

                    /*Faction attackerFaction = Find.World.GetComponent<MultiplayerWorldComp>().playerFactions[encounter.attacker];
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

                    FactionContext.Pop();*/
                });
            }
            else if (id == Packets.CLIENT_MAP_LOADED)
            {
                OnMainThread.Enqueue(() =>
                {
                    LongActionEncounter encounter = ((LongActionEncounter)OnMainThread.currentLongAction);

                    if (encounter.waitingFor.Remove(Connection.username) && encounter.waitingFor.Count == 0)
                    {
                        byte[] extra = ScribeUtil.WriteSingle(OnMainThread.currentLongAction);
                        Multiplayer.server.SendToAll(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(ServerAction.LONG_ACTION_END, extra));
                    }
                });
            }
            else if (id == Packets.CLIENT_ID_BLOCK_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    int tile = data.ReadInt();

                    if (tile == -1)
                    {
                        IdBlock nextBlock = Multiplayer.NextIdBlock();
                        Connection.Send(Packets.SERVER_NEW_ID_BLOCK, ScribeUtil.WriteSingle(nextBlock));
                    }
                    else
                    {
                        Encounter encounter = Encounter.GetByTile(tile);
                        if (Connection.username != encounter.defender) return;

                        IdBlock nextBlock = Multiplayer.NextIdBlock();
                        nextBlock.mapTile = tile;

                        foreach (string player in encounter.GetPlayers())
                        {
                            byte[] extra = ScribeUtil.WriteSingle(nextBlock);
                            Multiplayer.server.GetByUsername(player).Send(Packets.SERVER_ACTION_SCHEDULE, GetServerActionMsg(ServerAction.MAP_ID_BLOCK, extra));
                        }
                    }
                });
            }
            else if (id == Packets.CLIENT_MAP_STATE_DEBUG)
            {
                OnMainThread.Enqueue(() => { Log.Message("Got map state " + Connection.username + " " + data.GetBytes().Length); });

                ThreadPool.QueueUserWorkItem(stateInfo =>
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
        }

        public override void Disconnect()
        {
        }

        public static string GetPlayerMapsPath(string username)
        {
            string worldfolder = Path.Combine(Path.Combine(GenFilePaths.SaveDataFolderPath, "MpSaves"), Find.World.GetComponent<MultiplayerWorldComp>().worldId);
            DirectoryInfo directoryInfo = new DirectoryInfo(worldfolder);
            if (!directoryInfo.Exists)
                directoryInfo.Create();
            return Path.Combine(worldfolder, username + ".maps");
        }

        public static object[] GetServerActionMsg(ServerAction action, byte[] extra)
        {
            return new object[] { action, TickPatch.timer + Multiplayer.scheduledActionDelay, extra };
        }
    }

    public class ClientWorldState : ConnectionState
    {
        public ClientWorldState(Connection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, new object[] { Multiplayer.username });
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
                    Multiplayer.savedWorld = data.ReadPrefixedBytes();
                    Multiplayer.mapsData = data.ReadPrefixedBytes();

                    Log.Message("World size: " + Multiplayer.savedWorld.Length + ", Maps size: " + Multiplayer.mapsData.Length);

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
                    Multiplayer.mainBlock = ScribeUtil.ReadSingle<IdBlock>(data.GetBytes());
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
            else if (id == Packets.SERVER_NEW_FACTION)
            {
                OnMainThread.Enqueue(() =>
                {
                    string owner = data.ReadString();
                    Faction faction = ScribeUtil.ReadSingle<Faction>(data.ReadPrefixedBytes());

                    Find.FactionManager.Add(faction);
                    Find.World.GetComponent<MultiplayerWorldComp>().playerFactions[owner] = faction;
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

                    Multiplayer.savingForEncounter = true;
                    ScribeUtil.StartWriting();
                    Scribe.EnterNode("data");
                    Map map = settlement.Map;
                    Scribe_Deep.Look(ref map, "map");
                    byte[] mapData = ScribeUtil.FinishWriting();
                    Multiplayer.savingForEncounter = false;

                    Current.ProgramState = ProgramState.MapInitializing;
                    foreach (Thing t in new List<Thing>(map.listerThings.AllThings))
                        t.DeSpawn();
                    Current.ProgramState = ProgramState.Playing;

                    Current.Game.DeinitAndRemoveMap(map);

                    Multiplayer.client.Send(Packets.CLIENT_MAP_RESPONSE, mapData);
                });
            }
            else if (id == Packets.SERVER_MAP_RESPONSE)
            {
                OnMainThread.Enqueue(() =>
                {
                    Multiplayer.loadingEncounter = true;
                    Current.ProgramState = ProgramState.MapInitializing;
                    Multiplayer.Seed = Find.TickManager.TicksGame;

                    Log.Message("Encounter map size: " + data.GetBytes().Length);

                    ScribeUtil.StartLoading(data.GetBytes());
                    ScribeUtil.SupplyCrossRefs();
                    Map map = null;
                    Scribe_Deep.Look(ref map, "map");
                    ScribeUtil.FinishLoading();

                    Current.Game.AddMap(map);
                    map.FinalizeLoading();

                    Faction ownerFaction = map.info.parent.Faction;
                    MultiplayerWorldComp worldComp = Find.World.GetComponent<MultiplayerWorldComp>();
                    MultiplayerMapComp mapComp = map.GetComponent<MultiplayerMapComp>();

                    string ownerFactionId = ownerFaction.GetUniqueLoadID();
                    string playerFactionId = Multiplayer.RealPlayerFaction.GetUniqueLoadID();

                    mapComp.inEncounter = true;

                    RandPatches.Ignore = true;
                    Rand.PushState();

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
                    }

                    RandPatches.Ignore = false;
                    Rand.PopState();

                    Multiplayer.loadingEncounter = false;
                    Current.ProgramState = ProgramState.Playing;

                    Find.World.renderer.wantedMode = WorldRenderMode.None;
                    Current.Game.VisibleMap = map;

                    Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
                    Log.Message("map loaded rand " + Rand.Int);
                });
            }
            else if (id == Packets.SERVER_NEW_ID_BLOCK)
            {
                OnMainThread.Enqueue(() =>
                {
                    IdBlock block = ScribeUtil.ReadSingle<IdBlock>(data.GetBytes());
                    if (block.mapTile != -1)
                        Find.WorldObjects.MapParentAt(block.mapTile).Map.GetComponent<MultiplayerMapComp>().encounterIdBlock = block;
                    else
                        Multiplayer.mainBlock = block;
                });
            }
            else if (id == Packets.SERVER_TIME_CONTROL)
            {
                OnMainThread.Enqueue(() =>
                {
                    int tickUntil = data.ReadInt();
                    TickPatch.tickUntil = tickUntil;
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

                ScheduledServerAction schdl = new ScheduledServerAction(action, ticks, extraBytes);
                OnMainThread.ScheduleAction(schdl);
            });
        }

        // Currently covers:
        // - settling after joining
        public static void SyncClientWorldObj(WorldObject obj)
        {
            byte[] data = ScribeUtil.WriteSingle(obj);
            Multiplayer.client.Send(Packets.CLIENT_NEW_WORLD_OBJ, data);
        }
    }

    public class OnMainThread : MonoBehaviour
    {
        private static Queue<Action> queue = new Queue<Action>();
        private static Queue<Action> tempQueue = new Queue<Action>();

        public static readonly Queue<ScheduledServerAction> longActionRelated = new Queue<ScheduledServerAction>();
        public static readonly Queue<ScheduledServerAction> scheduledActions = new Queue<ScheduledServerAction>();

        public static readonly List<LongAction> longActions = new List<LongAction>();
        public static LongAction currentLongAction;

        private static readonly FieldInfo zoneShuffled = typeof(Zone).GetField("cellsShuffled", BindingFlags.NonPublic | BindingFlags.Instance);

        public static List<byte[]> replayActions = new List<byte[]>();

        public void Update()
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

            if (Multiplayer.client == null) return;

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
            Multiplayer.Seed = Find.TickManager.TicksGame;
            RandPatch.current = "server action";

            ServerAction action = actionReq.action;

            if (action == ServerAction.TIME_SPEED)
            {
                TimeSpeed speed = (TimeSpeed)data.ReadByte();
                TickUpdatePatch.SetSpeed(speed);
                Log.Message(Multiplayer.username + " set speed " + speed + " " + TickPatch.timer + " " + Find.TickManager.TicksGame);
            }

            if (action == ServerAction.MAP_ID_BLOCK)
            {
                IdBlock block = ScribeUtil.ReadSingle<IdBlock>(actionReq.data);
                Map map = Find.WorldObjects.MapParentAt(block.mapTile)?.Map;
                if (map != null)
                {
                    map.GetComponent<MultiplayerMapComp>().encounterIdBlock = block;
                    Log.Message(Multiplayer.username + "encounter id block set");
                }
            }

            if (action == ServerAction.DESIGNATOR)
            {
                HandleDesignator(actionReq, data);
            }

            if (action == ServerAction.ORDER_JOB)
            {
                HandleOrderJob(actionReq, data);
            }

            if (action == ServerAction.DELETE_ZONE)
            {
                string factionId = data.ReadString();
                string mapId = data.ReadString();
                string zoneId = data.ReadString();

                Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
                if (map == null) return;

                map.PushFaction(factionId);
                map.zoneManager.AllZones.FirstOrDefault(z => z.label == zoneId)?.Delete();
                map.PopFaction();
            }

            if (action == ServerAction.SPAWN_PAWN)
            {
                int tile = data.ReadInt();
                string factionId = data.ReadString();

                Map map = Find.WorldObjects.SettlementAt(tile).Map;
                map.PushFaction(Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId));
                Pawn pawn = ScribeUtil.ReadSingle<Pawn>(data.ReadPrefixedBytes());

                IntVec3 spawn = CellFinderLoose.TryFindCentralCell(map, 7, 10, (IntVec3 x) => !x.Roofed(map));
                GenSpawn.Spawn(pawn, spawn, map);
                map.PopFaction();
                Log.Message("spawned " + pawn);
            }

            if (action == ServerAction.FORBID)
            {
                string mapId = data.ReadString();
                string thingId = data.ReadString();
                string factionId = data.ReadString();
                bool value = data.ReadBool();

                Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
                if (map == null) return;

                ThingWithComps thing = map.listerThings.AllThings.FirstOrDefault(t => t.GetUniqueLoadID() == thingId) as ThingWithComps;
                if (thing == null) return;

                CompForbiddable forbiddable = thing.GetComp<CompForbiddable>();
                if (forbiddable == null) return;

                map.PushFaction(factionId);
                forbiddable.Forbidden = value;
                map.PopFaction();
            }

            if (action == ServerAction.DRAFT)
            {
                string mapId = data.ReadString();
                string pawnId = data.ReadString();
                bool draft = data.ReadBool();

                Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
                if (map == null) return;

                Pawn pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.GetUniqueLoadID() == pawnId);
                if (pawn == null) return;

                DraftSetPatch.dontHandle = true;
                map.PushFaction(pawn.Faction);
                pawn.drafter.Drafted = draft;
                map.PopFaction();
                DraftSetPatch.dontHandle = false;
            }

            RandPatch.current = null;

            replayActions.Add(Server.GetBytes(actionReq.action, actionReq.ticks, actionReq.data));
        }

        private static void HandleOrderJob(ScheduledServerAction actionReq, ByteReader data)
        {
            string mapId = data.ReadString();
            Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
            if (map == null) return;

            string pawnId = data.ReadString();
            Pawn pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.GetUniqueLoadID() == pawnId);
            if (pawn == null) return;

            Job job = ScribeUtil.ReadSingle<Job>(data.ReadPrefixedBytes());
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

        private static void HandleDesignator(ScheduledServerAction actionReq, ByteReader data)
        {
            int mode = data.ReadInt();
            string desName = data.ReadString();
            string buildDefName = data.ReadString();
            Designator designator = GetDesignator(desName, buildDefName);
            if (designator == null) return;

            string mapId = data.ReadString();
            string factionId = data.ReadString();

            Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
            if (map == null) return;

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

        private static void AddLongAction(LongAction action)
        {
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
        private static int lastTicksSend;
        public static int tickUntil;
        public static int timer;
        public static float acc;

        public static bool ticking;

        static bool Prefix()
        {
            ticking = Multiplayer.client == null || Multiplayer.server != null || timer + 1 < tickUntil;
            return ticking;
        }

        static void Postfix()
        {
            if (!ticking) return;

            TickManager tickManager = Find.TickManager;
            MultiplayerWorldComp worldComp = Find.World.GetComponent<MultiplayerWorldComp>();

            while (OnMainThread.longActionRelated.Count > 0 && OnMainThread.longActionRelated.Peek().ticks == timer)
                OnMainThread.ExecuteLongActionRelated(OnMainThread.longActionRelated.Dequeue());

            while (OnMainThread.scheduledActions.Count > 0 && OnMainThread.scheduledActions.Peek().ticks == timer && tickManager.CurTimeSpeed != TimeSpeed.Paused)
            {
                ScheduledServerAction action = OnMainThread.scheduledActions.Dequeue();
                OnMainThread.ExecuteServerAction(action, new ByteReader(action.data));
            }

            if (Multiplayer.server != null)
                if (timer - lastTicksSend > Multiplayer.scheduledActionDelay / 2)
                {
                    Multiplayer.server.SendToAll(Packets.SERVER_TIME_CONTROL, new object[] { timer + Multiplayer.scheduledActionDelay });
                    lastTicksSend = timer;
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

            ticking = false;

            if (tickManager.TickRateMultiplier > 0)
            {
                acc += 1f / tickManager.TickRateMultiplier;
                if (acc > 1f)
                {
                    timer++;
                    acc -= 1f;
                }
            }
        }
    }

    public class MultiplayerWorldComp : WorldComponent
    {
        public string worldId = Guid.NewGuid().ToString();
        public int sessionId = new System.Random().Next();
        public Dictionary<string, Faction> playerFactions = new Dictionary<string, Faction>();

        public Dictionary<string, ResearchManager> factionResearch = new Dictionary<string, ResearchManager>();
        public Dictionary<string, DrugPolicyDatabase> factionDrugPolicies = new Dictionary<string, DrugPolicyDatabase>();
        public Dictionary<string, OutfitDatabase> factionOutfits = new Dictionary<string, OutfitDatabase>();

        private List<string> keyWorkingList;
        private List<Faction> valueWorkingList;

        public MultiplayerWorldComp(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref worldId, "worldId");
            ScribeUtil.Look(ref playerFactions, "playerFactions", LookMode.Value, LookMode.Reference, ref keyWorkingList, ref valueWorkingList);
            Scribe_Values.Look(ref Multiplayer.highestUniqueId, "highestUniqueId");

            // saving for joining players
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref TickPatch.timer, "timer");
            Scribe_Values.Look(ref TickPatch.acc, "acc");
        }

        public string GetUsername(Faction faction)
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

