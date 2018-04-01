using Harmony;
using Ionic.Zlib;
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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        public static int scheduledCmdDelay = 15; // in ticks

        public static byte[] savedGame;
        public static byte[] savedPlayerMaps;
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
                RandSetSeedPatch.dontLog = true;

                Rand.Seed = value;
                UnityEngine.Random.InitState(value);

                RandSetSeedPatch.dontLog = false;
            }
        }

        static Multiplayer()
        {
            SimpleProfiler.Init();

            GenCommandLine.TryGetCommandLineArg("username", out username);
            if (username == null)
                username = SteamUtility.SteamPersonaName;
            if (username == "???")
                username = "Player" + Rand.Range(0, 9999);

            Log.Message("Player's username: " + username);
            Log.Message("Processor: " + SystemInfo.processorType);

            var gameobject = new GameObject();
            gameobject.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(gameobject);

            DoPatches();

            //print_profiler("profiler_" + Multiplayer.username + "_startup.txt");
            //stop_profiler();

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
                        conn.State = new ClientWorldState(conn);
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

            byte[] compressedMaps = GZipStream.CompressBuffer(ScribeUtil.XmlToByteArray(mapsNode));
            Multiplayer.client.Send(Packets.CLIENT_AUTOSAVED_DATA, false, compressedMaps);

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
            int blockSize = 30000;
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

        private int current;

        public IdBlock() { }

        public IdBlock(int blockStart, int blockSize, int mapTile = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapTile = mapTile;
        }

        public int NextId()
        {
            current++;
            if (current > blockSize * 0.95)
            {
                Multiplayer.client.Send(Packets.CLIENT_ID_BLOCK_REQUEST, new object[] { mapTile });
                Log.Message("Sent id block request at " + current);
            }

            return blockStart + current;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref blockStart, "blockStart");
            Scribe_Values.Look(ref blockSize, "blockSize");
            Scribe_Values.Look(ref mapTile, "mapTile");
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
        public const int CLIENT_COMMAND = 2;
        public const int CLIENT_USERNAME = 3;
        public const int CLIENT_NEW_WORLD_OBJ = 4;
        public const int CLIENT_AUTOSAVED_DATA = 5;
        public const int CLIENT_ENCOUNTER_REQUEST = 6;
        public const int CLIENT_MAP_RESPONSE = 7;
        public const int CLIENT_MAP_LOADED = 8;
        public const int CLIENT_ID_BLOCK_REQUEST = 9;
        public const int CLIENT_MAP_STATE_DEBUG = 10;

        public const int SERVER_WORLD_DATA = 0;
        public const int SERVER_COMMAND = 1;
        public const int SERVER_NEW_FACTION = 2;
        public const int SERVER_NEW_WORLD_OBJ = 3;
        public const int SERVER_MAP_REQUEST = 4;
        public const int SERVER_MAP_RESPONSE = 5;
        public const int SERVER_NOTIFICATION = 6;
        public const int SERVER_NEW_ID_BLOCK = 7;
        public const int SERVER_TIME_CONTROL = 8;
        public const int SERVER_DISCONNECT_REASON = 9;
    }

    public class GlobalScopeAttribute : Attribute
    {
    }

    public enum CommandType
    {
        [GlobalScope]
        WORLD_TIME_SPEED,
        [GlobalScope]
        AUTOSAVE,

        // Map scope
        MAP_TIME_SPEED,
        MAP_ID_BLOCK,
        DRAFT,
        FORBID,
        DESIGNATOR,
        ORDER_JOB,
        DELETE_ZONE,
        SPAWN_PAWN
    }

    public class ScheduledCommand
    {
        public readonly CommandType type;
        public readonly int ticks;
        public readonly byte[] data;

        public ScheduledCommand(CommandType type, int ticks, byte[] data)
        {
            this.ticks = ticks;
            this.type = type;
            this.data = data;
        }
    }

    public class ServerWorldState : ConnectionState
    {
        private static Regex UsernamePattern = new Regex(@"^[a-zA-Z0-9_]+$");

        public ServerWorldState(Connection connection) : base(connection)
        {
        }

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.CLIENT_USERNAME)
            {
                OnMainThread.Enqueue(() =>
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
                });
            }
            else if (id == Packets.CLIENT_REQUEST_WORLD)
            {
                OnMainThread.Enqueue(() =>
                {
                    // todo compress
                    byte[][] cmds = OnMainThread.cmdsSinceLastAutosave.ToArray();
                    byte[] packetData = Server.GetBytes(TickPatch.tickUntil, cmds, Multiplayer.savedGame);

                    Log.Message("World response sent: " + packetData.Length + " " + cmds.Length);

                    Connection.Send(Packets.SERVER_WORLD_DATA, packetData);
                });
            }
            else if (id == Packets.CLIENT_WORLD_LOADED)
            {
                Connection.State = new ServerPlayingState(Connection);
            }
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

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.CLIENT_COMMAND)
            {
                OnMainThread.Enqueue(() =>
                {
                    CommandType cmd = (CommandType)data.ReadInt();
                    byte[] extra = data.ReadPrefixedBytes();

                    Multiplayer.server.SendToAll(Packets.SERVER_COMMAND, GetServerCommandMsg(cmd, extra));
                });
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

                    // send the encounter map
                    // setup id blocks
                });
            }
            else if (id == Packets.CLIENT_MAP_RESPONSE)
            {
                OnMainThread.Enqueue(() =>
                {
                    // setup the encounter

                    /*LongActionEncounter encounter = ((LongActionEncounter)OnMainThread.currentLongAction);

                    Connection attacker = Multiplayer.server.GetByUsername(encounter.attacker);
                    Connection defender = Multiplayer.server.GetByUsername(encounter.defender);

                    defender.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());
                    attacker.Send(Packets.SERVER_MAP_RESPONSE, data.GetBytes());

                    encounter.waitingFor.Add(defender.username);
                    encounter.waitingFor.Add(attacker.username);*/

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
                    // map loaded
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
                            Multiplayer.server.GetByUsername(player).Send(Packets.SERVER_COMMAND, GetServerCommandMsg(CommandType.MAP_ID_BLOCK, extra));
                        }
                    }
                });
            }
            else if (id == Packets.CLIENT_MAP_STATE_DEBUG)
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
            else if (id == Packets.CLIENT_AUTOSAVED_DATA)
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
            }
        }

        public override void Disconnected()
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

        public static object[] GetServerCommandMsg(CommandType cmdType, byte[] extra)
        {
            return new object[] { cmdType, TickPatch.Timer + Multiplayer.scheduledCmdDelay, extra };
        }
    }

    public class ClientWorldState : ConnectionState
    {
        public ClientWorldState(Connection connection) : base(connection)
        {
            connection.Send(Packets.CLIENT_USERNAME, Multiplayer.username);
            connection.Send(Packets.CLIENT_REQUEST_WORLD);
        }

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.SERVER_WORLD_DATA)
            {
                Connection.State = new ClientPlayingState(Connection);

                OnMainThread.Enqueue(() =>
                {
                    int tickUntil = data.ReadInt();

                    int cmdsLen = data.ReadInt();
                    for (int i = 0; i < cmdsLen; i++)
                    {
                        byte[] cmd = data.ReadPrefixedBytes();
                        ClientPlayingState.HandleActionSchedule(new ByteReader(cmd));
                    }

                    Multiplayer.savedGame = data.ReadPrefixedBytes();

                    TickPatch.tickUntil = tickUntil;
                    LoadPatch.gameToLoad = ScribeUtil.GetDocument(Multiplayer.savedGame);

                    Log.Message("World size: " + Multiplayer.savedGame.Length + ", " + cmdsLen + " scheduled actions");

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
                                Log.Message("catchup start " + catchupStart + " " + Find.TickManager.TicksGame + " " + Find.TickManager.CurTimeSpeed);
                                CatchUp();
                                catchingUp.WaitOne();

                                Multiplayer.client.Send(Packets.CLIENT_WORLD_LOADED);
                            }, "Catching up", true, null);
                        });
                    }, "Play", "Loading the game", true, null);
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
                LongEventHandler.SetCurrentEventText("Catching up (" + percent + "%)");

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

        public override void Message(int id, ByteReader data)
        {
            if (id == Packets.SERVER_COMMAND)
            {
                OnMainThread.Enqueue(() =>
                {
                    HandleActionSchedule(data);
                });
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
            else if (id == Packets.SERVER_MAP_REQUEST)
            {
                OnMainThread.Enqueue(() =>
                {
                    int tile = data.ReadInt();
                    Settlement settlement = Find.WorldObjects.SettlementAt(tile);

                    //SaveCompressible.disableCompression = true;
                    ScribeUtil.StartWriting();
                    Scribe.EnterNode("data");
                    Map map = settlement.Map;
                    Scribe_Deep.Look(ref map, "map");
                    byte[] mapData = ScribeUtil.FinishWriting();
                    //SaveCompressible.disableCompression = false;

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

        public override void Disconnected()
        {
        }

        public static void HandleActionSchedule(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt();
            int ticks = data.ReadInt();
            byte[] extraBytes = data.ReadPrefixedBytes();

            ScheduledCommand schdl = new ScheduledCommand(cmd, ticks, extraBytes);
            OnMainThread.ScheduleCommand(schdl);
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

        public static readonly Queue<ScheduledCommand> scheduledCmds = new Queue<ScheduledCommand>();

        private static readonly FieldInfo zoneShuffled = typeof(Zone).GetField("cellsShuffled", BindingFlags.NonPublic | BindingFlags.Instance);

        public static List<byte[]> cmdsSinceLastAutosave = new List<byte[]>();

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
            lock (queue)
                queue.Enqueue(action);
        }

        public static void ScheduleCommand(ScheduledCommand cmd)
        {
            scheduledCmds.Enqueue(cmd);
            cmdsSinceLastAutosave.Add(Server.GetBytes(cmd.type, cmd.ticks, cmd.data));
        }

        public static void ExecuteServerCmd(ScheduledCommand cmd, ByteReader data)
        {
            Multiplayer.Seed = Find.TickManager.TicksGame;
            RandPatch.current = "server cmd";

            CommandType cmdType = cmd.type;

            if (cmdType == CommandType.WORLD_TIME_SPEED)
            {
                TimeSpeed prevSpeed = Find.TickManager.CurTimeSpeed;
                TimeSpeed speed = (TimeSpeed)data.ReadByte();
                TimeChangeCommandPatch.SetSpeed(speed);

                Log.Message(Multiplayer.username + " set speed " + speed + " " + TickPatch.Timer + " " + Find.TickManager.TicksGame);

                if (prevSpeed == TimeSpeed.Paused)
                    TickPatch.timerInt = cmd.ticks;
            }

            if (cmdType == CommandType.AUTOSAVE)
            {
                TimeChangeCommandPatch.SetSpeed(TimeSpeed.Paused);

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

                    cmdsSinceLastAutosave.Clear();
                }, "Autosaving", false, null);

                // todo unpause after everyone completes the autosave
            }

            if (cmdType == CommandType.MAP_ID_BLOCK)
            {
                IdBlock block = ScribeUtil.ReadSingle<IdBlock>(cmd.data);
                Map map = Find.WorldObjects.MapParentAt(block.mapTile)?.Map;
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
                string factionId = data.ReadString();
                string mapId = data.ReadString();
                string zoneId = data.ReadString();

                Map map = Find.Maps.FirstOrDefault(m => m.GetUniqueLoadID() == mapId);
                if (map == null) return;

                map.PushFaction(factionId);
                map.zoneManager.AllZones.FirstOrDefault(z => z.label == zoneId)?.Delete();
                map.PopFaction();
            }

            if (cmdType == CommandType.SPAWN_PAWN)
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

            if (cmdType == CommandType.FORBID)
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

            if (cmdType == CommandType.DRAFT)
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
        }

        private static void HandleOrderJob(ScheduledCommand command, ByteReader data)
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

        private static void HandleDesignator(ScheduledCommand command, ByteReader data)
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
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch(nameof(TickManager.DoSingleTick))]
    public static class TickPatch
    {
        public static int tickUntil;
        public static double timerInt;

        public static int Timer => (int)timerInt;

        public static bool ticking;

        private static int lastTicksSend;
        private static float currentTickRate;

        static bool Prefix()
        {
            ticking = Multiplayer.client == null || Multiplayer.server != null || Timer < tickUntil;
            currentTickRate = Find.TickManager.TickRateMultiplier;

            return ticking;
        }

        static void Postfix()
        {
            if (!ticking) return;

            TickManager tickManager = Find.TickManager;

            while (OnMainThread.scheduledCmds.Count > 0 && OnMainThread.scheduledCmds.Peek().ticks == Timer)
            {
                ScheduledCommand cmd = OnMainThread.scheduledCmds.Dequeue();
                OnMainThread.ExecuteServerCmd(cmd, new ByteReader(cmd.data));
            }

            if (Multiplayer.server != null)
                if (Timer - lastTicksSend > Multiplayer.scheduledCmdDelay / 2)
                {
                    Multiplayer.server.SendToAll(Packets.SERVER_TIME_CONTROL, new object[] { Timer + Multiplayer.scheduledCmdDelay });
                    lastTicksSend = Timer;
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

            if (currentTickRate >= 1)
                timerInt += 1f / currentTickRate;
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
            Scribe_Values.Look(ref TickPatch.timerInt, "timer");

            // saving for joining players
            Scribe_Values.Look(ref sessionId, "sessionId");

            TimeSpeed timeSpeed = Find.TickManager.CurTimeSpeed;
            Scribe_Values.Look(ref timeSpeed, "timeSpeed");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Find.TickManager.CurTimeSpeed = timeSpeed;
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

