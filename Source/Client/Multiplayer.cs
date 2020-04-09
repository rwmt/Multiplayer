//extern alias zip;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RestSharp;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
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
        public static Harmony harmony => MultiplayerMod.harmony;

        public static bool reloading;

        public static FactionDef FactionDef = FactionDef.Named("MultiplayerColony");
        public static FactionDef DummyFactionDef = FactionDef.Named("MultiplayerDummy");
        public static KeyBindingDef ToggleChatDef = KeyBindingDef.Named("MpToggleChat");

        public static IdBlock GlobalIdBlock => game.worldComp.globalIdBlock;
        public static Faction DummyFaction => game.dummyFaction;
        public static MultiplayerWorldComp WorldComp => game.worldComp;

        public static bool ShowDevInfo => MpVersion.IsDebug || (Prefs.DevMode && MultiplayerMod.settings.showDevInfo);

        public static Faction RealPlayerFaction
        {
            get => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;
            set => game.RealPlayerFaction = value;
        }

        public static bool ExecutingCmds => MultiplayerWorldComp.executingCmdWorld || MapAsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => MultiplayerWorldComp.tickingWorld || MapAsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static Map MapContext => MapAsyncTimeComp.tickingMap ?? MapAsyncTimeComp.executingCmdMap;

        public static bool dontSync;
        public static bool ShouldSync => InInterface && !dontSync;
        public static bool InInterface => Client != null && !Ticking && !ExecutingCmds && !reloading && Current.ProgramState == ProgramState.Playing && LongEventHandler.currentEvent == null;

        public static string ReplaysDir => GenFilePaths.FolderUnderSaveData("MpReplays");
        public static string DesyncsDir => GenFilePaths.FolderUnderSaveData("MpDesyncs");

        public static Stopwatch Clock = Stopwatch.StartNew();

        public static HashSet<string> xmlMods = new HashSet<string>();
        public static List<ModHashes> enabledModAssemblyHashes = new List<ModHashes>();
        public static Dictionary<string, DefInfo> localDefInfos;
        public static Dictionary<string, int> modsCompatibility = new Dictionary<string, int>();  // workshopID: compatNumber [0-4]

        static Multiplayer()
        {
            if (GenCommandLine.CommandLineArgPassed("profiler"))
                SimpleProfiler.CheckAvailable();

            MpLog.info = str => Log.Message($"{username} {TickPatch.Timer} {str}");
            MpLog.error = str => Log.Error(str);

            SetUsername();

            foreach (var mod in ModLister.AllInstalledMods)
            {
                if (!mod.ModHasAssemblies())
                    xmlMods.Add(mod.RootDir.FullName);
            }

            SimpleProfiler.Init(username);

            if (SteamManager.Initialized)
                SteamIntegration.InitCallbacks();

            Log.Message($"Multiplayer version {MpVersion.Version}");
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
            Sync.CollectTypes();

            try
            {
                SyncHandlers.Init();

                var asm = Assembly.GetExecutingAssembly();

                Sync.RegisterAllAttributes(asm);
                PersistentDialog.BindAll(asm);
            }
            catch (Exception e)
            {
                Log.Error($"Exception during Sync initialization: {e}");
            }

            try {
                harmony.DoAllMpPatches();
            } catch (Exception e) {
                Log.Error($"Exception during MpPatching: {e}");
            }

            try
            {
                DoPatches();
            }
            catch (Exception e)
            {
                Log.Error($"Exception during patching: {e}");
            }

            Log.messageQueue.maxMessages = 1000;

            DoubleLongEvent(() =>
            {
                CollectDefInfos();
                CollectModHashes();

                Sync.InitHandlers();
            }, "Loading"); // right before the arbiter connects

            HandleCommandLine();

            if (MultiplayerMod.arbiterInstance) {
                RuntimeHelpers.RunClassConstructor(typeof(Text).TypeHandle);
            }
            else {
                UpdateModCompatibilityDb();
            }
        }

        private static void UpdateModCompatibilityDb()
        {
            Task.Run(() => {
                var client = new RestClient("http://neb.nebtown.info:51412/mod-compatibility?version=1.1");
                try {
                    var rawResponse = client.Get(new RestRequest($"", DataFormat.Json));
                    modsCompatibility = SimpleJson.DeserializeObject<Dictionary<string, int>>(rawResponse.Content);
                    Log.Message($"MP: successfully fetched {modsCompatibility.Count} mods compatibility info");
                }
                catch (Exception e) {
                    Log.Warning($"MP: updating mod compatibility list failed {e.Message} {e.StackTrace}");
                }
            });
        }

        private static void SetUsername()
        {
            Multiplayer.username = MultiplayerMod.settings.username;

            if (Multiplayer.username == null && SteamManager.Initialized)
            {
                Multiplayer.username = SteamUtility.SteamPersonaName;
                Multiplayer.username = new Regex("[^a-zA-Z0-9_]").Replace(Multiplayer.username, string.Empty);
                Multiplayer.username = Multiplayer.username.TrimmedToLength(15);

                MultiplayerMod.settings.username = Multiplayer.username;
                MultiplayerMod.settings.Write();
            }

            if (GenCommandLine.TryGetCommandLineArg("username", out string username))
                Multiplayer.username = username;
            else if (Multiplayer.username == null || Multiplayer.username.Length < 3 || MpVersion.IsDebug)
                Multiplayer.username = "Player" + Rand.Range(0, 9999);
        }

        private static void DoubleLongEvent(Action action, string textKey)
        {
            LongEventHandler.QueueLongEvent(() => LongEventHandler.QueueLongEvent(action, textKey, false, null), textKey, false, null);
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

                DoubleLongEvent(() => ClientUtil.TryConnect(ip, port), "Connecting");
            }

            if (GenCommandLine.CommandLineArgPassed("arbiter"))
            {
                username = "The Arbiter";
                Prefs.VolumeGame = 0;
            }

            if (GenCommandLine.TryGetCommandLineArg("replay", out string replay))
            {
                DoubleLongEvent(() =>
                {
                    Replay.LoadReplay(Replay.ReplayFile(replay), true, () =>
                    {
                        var rand = Find.Maps.Select(m => m.AsyncTime().randState).Select(s => $"{s} {(uint)s} {s >> 32}");

                        Log.Message($"timer {TickPatch.Timer}");
                        Log.Message($"world rand {WorldComp.randState} {(uint)WorldComp.randState} {WorldComp.randState >> 32}");
                        Log.Message($"map rand {rand.ToStringSafeEnumerable()} | {Find.Maps.Select(m => m.AsyncTime().mapTicks).ToStringSafeEnumerable()}");

                        Application.Quit();
                    });
                }, "Replay");
            }

            if (GenCommandLine.CommandLineArgPassed("printsync"))
            {
                ExtendDirectXmlSaver.extend = true;
                DirectXmlSaver.SaveDataObject(new SyncContainer(), "SyncHandlers.xml");
                ExtendDirectXmlSaver.extend = false;
            }
        }

        public class SyncContainer
        {
            public List<SyncHandler> handlers = Sync.handlers;
        }

        private static void DoPatches()
        {
            //harmony.PatchAll();

            Log.Message("Anotated patches");

            // this is a temporary report, must be removed or made debug only
            var report = new List<(Type, Exception)>();
            Assembly.GetCallingAssembly().GetTypes().Do(delegate (Type type) {
                try {
                    harmony.CreateClassProcessor(type).Patch();

                    report.Add((type, null));
                } catch (Exception e) {
                    report.Add((type, e));
                }
            });
            foreach(var entry in report) {
                if (entry.Item2 != null) {
                    Log.Error($"FAIL: {entry.Item1} with {entry.Item2.InnerException}");
                } else if (false) {
                    Log.Message($"PASS: {entry.Item1}");
                }
            }

            Log.Message("General designation patches");

            // General designation handling
            {
                var designatorFinalizer = AccessTools.Method(typeof(DesignatorPatches), "DesignateFinalizer");
                var designatorMethods = new[] {
                     "DesignateSingleCell",
                     "DesignateMultiCell",
                     "DesignateThing",
                     };

                foreach (Type t in typeof(Designator).AllSubtypesAndSelf())
                {
                    foreach (string m in designatorMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (method == null) continue;

                        MethodInfo prefix = AccessTools.Method(typeof(DesignatorPatches), m);
                        try {
                            harmony.Patch(method, new HarmonyMethod(prefix), null, null, new HarmonyMethod(designatorFinalizer));
                        } catch (Exception e) {
                            Log.Error($"FAIL: {t.FullName}:{method.Name} with {e.InnerException}");
                        }
                    }
                }
            }

            Log.Message("non-deterministic patches");

            // Remove side effects from methods which are non-deterministic during ticking (e.g. camera dependent motes and sound effects)
            {
                var randPatchPrefix = new HarmonyMethod(typeof(RandPatches), "Prefix");
                var randPatchPostfix = new HarmonyMethod(typeof(RandPatches), "Postfix");

                var subSustainerStart = AccessTools.Method(typeof(SubSustainer), "<.ctor>b__12_0");
                var sampleCtor = typeof(Sample).GetConstructor(new[] { typeof(SubSoundDef) });
                var subSoundPlay = typeof(SubSoundDef).GetMethod(nameof(SubSoundDef.TryPlay));
                var effecterTick = typeof(Effecter).GetMethod(nameof(Effecter.EffectTick));
                var effecterTrigger = typeof(Effecter).GetMethod(nameof(Effecter.Trigger));
                var effecterCleanup = typeof(Effecter).GetMethod(nameof(Effecter.Cleanup));
                var randomBoltMesh = typeof(LightningBoltMeshPool).GetProperty(nameof(LightningBoltMeshPool.RandomBoltMesh)).GetGetMethod();
                var drawTrackerCtor = typeof(Pawn_DrawTracker).GetConstructor(new[] { typeof(Pawn) });
                var randomHair = typeof(PawnHairChooser).GetMethod(nameof(PawnHairChooser.RandomHairDefFor));

                var effectMethods = new MethodBase[] { subSustainerStart, sampleCtor, subSoundPlay, effecterTick, effecterTrigger, effecterCleanup, randomBoltMesh, drawTrackerCtor, randomHair };
                var moteMethods = typeof(MoteMaker).GetMethods(BindingFlags.Static | BindingFlags.Public);

                foreach (MethodBase m in effectMethods.Concat(moteMethods)) {
                    try {
                        harmony.Patch(m, randPatchPrefix, randPatchPostfix);
                    } catch (Exception e) {
                        Log.Error($"FAIL: {m.GetType().FullName}:{m.Name} with {e.InnerException}");
                    }
                }

            }

            Log.Message("non-deterministic patches");

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
                        if (method != null) {
                            try {
                                harmony.Patch(method, thingMethodPrefix, thingMethodPostfix);
                            } catch (Exception e) {
                                Log.Error($"FAIL: {method.GetType().FullName}:{method.Name} with {e.InnerException}");
                            }
                        }
                    }
                }
            }

            Log.Message("floating point patches");

            // Full precision floating point saving
            {
                var doubleSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.DoubleSave_Prefix)));
                var floatSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.FloatSave_Prefix)));
                var valueSaveMethod = typeof(Scribe_Values).GetMethod(nameof(Scribe_Values.Look));

                harmony.Patch(valueSaveMethod.MakeGenericMethod(typeof(double)), doubleSavePrefix, null);
                harmony.Patch(valueSaveMethod.MakeGenericMethod(typeof(float)), floatSavePrefix, null);
            }

            Log.Message("map time gui patches");

            // Set the map time for GUI methods depending on it
            {
                var setMapTimePrefix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTimeForUI), "Prefix"));
                var setMapTimePostfix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTimeForUI), "Postfix"));

                var windowMethods = new[] { "DoWindowContents", "WindowUpdate" };
                foreach (string m in windowMethods)
                    harmony.Patch(typeof(MainTabWindow_Inspect).GetMethod(m), setMapTimePrefix, setMapTimePostfix);

                foreach (var t in typeof(InspectTabBase).AllSubtypesAndSelf())
                {
                    var method = t.GetMethod("FillTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (method != null && !method.IsAbstract) {
                        try {
                            harmony.Patch(method, setMapTimePrefix, setMapTimePostfix);
                        } catch (Exception e) {
                            Log.Error($"FAIL: {method.GetType().FullName}:{method.Name} with {e.InnerException}");
                        }
                    }

                }
            }

            Log.Message("Mod patches");

            ModPatches.Init();
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

        public static void HandleReceive(ByteReader data, bool reliable)
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

        private static HashSet<Type> IgnoredVanillaDefTypes = new HashSet<Type>
        {
            typeof(FeatureDef), typeof(HairDef),
            typeof(MainButtonDef), typeof(PawnTableDef),
            typeof(TransferableSorterDef), typeof(ConceptDef),
            typeof(InstructionDef), typeof(EffecterDef),
            typeof(ImpactSoundTypeDef), typeof(KeyBindingCategoryDef),
            typeof(KeyBindingDef), typeof(RulePackDef),
            typeof(ScatterableDef), typeof(ShaderTypeDef),
            typeof(SongDef), typeof(SoundDef),
            typeof(SubcameraDef), typeof(PawnColumnDef)
        };

        private static void CollectDefInfos()
        {
            var dict = new Dictionary<string, DefInfo>();

            int TypeHash(Type type) => GenText.StableStringHash(type.FullName);

            dict["ThingComp"] = GetDefInfo(Sync.thingCompTypes, TypeHash);
            dict["Designator"] = GetDefInfo(Sync.designatorTypes, TypeHash);
            dict["WorldObjectComp"] = GetDefInfo(Sync.worldObjectCompTypes, TypeHash);
            dict["IStoreSettingsParent"] = GetDefInfo(Sync.storageParents, TypeHash);
            dict["IPlantToGrowSettable"] = GetDefInfo(Sync.plantToGrowSettables, TypeHash);

            dict["GameComponent"] = GetDefInfo(Sync.gameCompTypes, TypeHash);
            dict["WorldComponent"] = GetDefInfo(Sync.worldCompTypes, TypeHash);
            dict["MapComponent"] = GetDefInfo(Sync.mapCompTypes, TypeHash);

            dict["PawnBio"] = GetDefInfo(SolidBioDatabase.allBios, b => b.name.GetHashCode());
            dict["Backstory"] = GetDefInfo(BackstoryDatabase.allBackstories.Keys, b => b.GetHashCode());

            foreach (var defType in GenTypes.AllLeafSubclasses(typeof(Def)))
            {
                if (defType.Assembly != typeof(Game).Assembly) continue;
                if (IgnoredVanillaDefTypes.Contains(defType)) continue;

                var defs = GenDefDatabase.GetAllDefsInDatabaseForDef(defType);
                dict.Add(defType.Name, GetDefInfo(defs, d => GenText.StableStringHash(d.defName)));
            }

            localDefInfos = dict;
        }

        private static void CollectModHashes()
        {
            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                var hashes = new ModHashes()
                {
                    assemblyHash = mod.ModListAssemblies().CRC32(),
                    xmlHash = LoadableXmlAssetCtorPatch.AggregateHash(mod),
                    aboutHash = new DirectoryInfo(Path.Combine(mod.RootDir, "About")).GetFiles().CRC32()
                };
                enabledModAssemblyHashes.Add(hashes);
            }

            LoadableXmlAssetCtorPatch.ClearHashBag();
        }

        private static DefInfo GetDefInfo<T>(IEnumerable<T> types, Func<T, int> hash)
        {
            return new DefInfo()
            {
                count = types.Count(),
                hash = types.Select(t => hash(t)).AggregateHash()
            };
        }
    }

    public class ModHashes
    {
        public int assemblyHash, xmlHash, aboutHash;
    }

    public static class FactionContext
    {
        private static Stack<Faction> stack = new Stack<Faction>();

        public static Faction Push(Faction newFaction)
        {
            if (newFaction == null || (newFaction.def != Multiplayer.FactionDef && !newFaction.def.isPlayer))
            {
                stack.Push(null);
                return null;
            }

            stack.Push(Find.FactionManager.OfPlayer);
            Set(newFaction);

            return newFaction;
        }

        public static Faction Pop()
        {
            Faction f = stack.Pop();
            if (f != null)
                Set(f);
            return f;
        }

        public static void Set(Faction newFaction)
        {
            var playerFaction = Find.FactionManager.OfPlayer;
            var factionDef = playerFaction.def;
            playerFaction.def = Multiplayer.FactionDef;
            newFaction.def = factionDef;
            Find.FactionManager.ofPlayer = newFaction;
        }
    }

}

