//extern alias zip;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.EarlyPatches;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using UnityEngine;
using UnityEngine.Scripting;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class Multiplayer
    {
        public static MultiplayerGame game;
        public static MultiplayerSession session;

        public static IConnection Client => session?.client;
        public static MultiplayerServer LocalServer => session?.localServer;
        public static PacketLogWindow WriterLog => session?.writerLog;
        public static PacketLogWindow ReaderLog => session?.readerLog;
        public static bool IsReplay => session?.replay ?? false;

        public static string username;
        public static Harmony harmony => MultiplayerMod.harmony;

        public static bool reloading;

        public static KeyBindingDef ToggleChatDef = KeyBindingDef.Named("MpToggleChat");

        public static IdBlock GlobalIdBlock => game.worldComp.globalIdBlock;
        public static MultiplayerWorldComp WorldComp => game.worldComp;

        public static bool ShowDevInfo => Prefs.DevMode && MultiplayerMod.settings.showDevInfo;

        public static Faction RealPlayerFaction
        {
            get => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;
            set => game.RealPlayerFaction = value;
        }

        public static bool ExecutingCmds => MultiplayerWorldComp.executingCmdWorld || AsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => MultiplayerWorldComp.tickingWorld || AsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static Map MapContext => AsyncTimeComp.tickingMap ?? AsyncTimeComp.executingCmdMap;

        public static bool dontSync;
        public static bool ShouldSync => InInterface && !dontSync;
        public static bool InInterface => Client != null && !Ticking && !ExecutingCmds && !reloading && Current.ProgramState == ProgramState.Playing && LongEventHandler.currentEvent == null;

        public static string ReplaysDir => GenFilePaths.FolderUnderSaveData("MpReplays");
        public static string DesyncsDir => GenFilePaths.FolderUnderSaveData("MpDesyncs");

        public static Stopwatch Clock = Stopwatch.StartNew();

        public static HashSet<string> xmlMods = new HashSet<string>();
        public static List<ModHashes> enabledModAssemblyHashes = new List<ModHashes>();
        public static Dictionary<string, DefInfo> localDefInfos;

        static Multiplayer()
        {
            Native.InitLmfPtr();

            if (GenCommandLine.CommandLineArgPassed("profiler"))
                SimpleProfiler.CheckAvailable();

            MpLog.info = str => Log.Message($"{username} {TickPatch.Timer} {str}");
            MpLog.error = str => Log.Error(str);

            SetUsername();
            CacheXMLMods();

            SimpleProfiler.Init(username);

            if (SteamManager.Initialized)
                SteamIntegration.InitCallbacks();

            Log.Message($"Multiplayer version {MpVersion.Version}");
            Log.Message($"Player's username: {username}");
            
            PlantWindSwayPatch.Init();

            var persistentObj = new GameObject();
            persistentObj.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(persistentObj);

            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientSteam, typeof(ClientSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientJoining, typeof(ClientJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientPlaying, typeof(ClientPlayingState));

            CollectCursorIcons();
            SyncSerialization.CollectTypes();

            DeepProfiler.Start("Multiplayer SyncGame");

            try
            {
                SyncGame.Init();

                var asm = Assembly.GetExecutingAssembly();

                Sync.RegisterAllAttributes(asm);
                PersistentDialog.BindAll(asm);
            }
            catch (Exception e)
            {
                Log.Error($"Exception during Sync initialization: {e}");
            }

            DeepProfiler.End();

            DeepProfiler.Start("Multiplayer MpPatches");

            try
            {
                harmony.DoAllMpPatches();
            }
            catch (Exception e)
            {
                Log.Error($"Exception during MpPatching: {e}");
            }

            DeepProfiler.End();

            DeepProfiler.Start("Multiplayer patches");

            try
            {
                DoPatches();
            }
            catch (Exception e)
            {
                Log.Error($"Exception during patching: {e}");
            }

            DeepProfiler.End();

            Log.messageQueue.maxMessages = 1000;

            DoubleLongEvent(() =>
            {
                CollectDefInfos();
                CollectModHashes();

                Sync.PostInitHandlers();
            }, "Loading"); // Right before the events from HandleCommandLine

            HandleCommandLine();

            if (MultiplayerMod.arbiterInstance)
            {
                RuntimeHelpers.RunClassConstructor(typeof(Text).TypeHandle);
            }

            CollectModFiles();

            MultiplayerMod.hasLoaded = true;

            Log.Message(GenFilePaths.ConfigFolderPath);
            Log.Message(""+JoinData.ModConfigPaths.Count());
        }

        public static ModFileDict modFiles = new ModFileDict();

        private static void CollectModFiles()
        {
            // todo check file path starts correctly, move to the long event?
            foreach (var mod in LoadedModManager.RunningModsListForReading.Select(m => (m, m.ModAssemblies())))
                foreach (var f in mod.Item2)
                {
                    var modId = mod.Item1.PackageIdPlayerFacing.ToLowerInvariant();
                    var relPath = f.FullName.IgnorePrefix(mod.Item1.RootDir.NormalizePath()).NormalizePath();
                    
                    modFiles.Add(modId, new ModFile(f.FullName, relPath, f.CRC32()));
                }

            foreach (var kv in LoadableXmlAssetCtorPatch.xmlAssetHashes){
                foreach (var f in kv.Value)
                    modFiles.Add(kv.Key, f);
            }
        }

        private static void CacheXMLMods()
        {
            foreach (var mod in ModLister.AllInstalledMods)
            {
                if (!mod.ModHasAssemblies())
                    xmlMods.Add(mod.RootDir.FullName);
            }
        }

        private static void SetUsername()
        {
            Multiplayer.username = MultiplayerMod.settings.username;

            if (Multiplayer.username == null)
            {
                if (SteamManager.Initialized) {
                    Multiplayer.username = SteamUtility.SteamPersonaName;
                } else {
                    Multiplayer.username = NameGenerator.GenerateName(RulePackDefOf.NamerTraderGeneral);
                }

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
            bool categoryNeedsAnnouncement = true;
            string category = null;

            void SetCategory(string str)
            {
                categoryNeedsAnnouncement = true;
                category = str;
            }

            void LogError(string str)
            {
                if (categoryNeedsAnnouncement) {
                    Log.Message($"Multiplayer :: {category}");
                }
                Log.Error(str);
            }

            SetCategory("Annotated patches");

            Assembly.GetCallingAssembly().GetTypes().Do(type => {
                // EarlyPatches are handled in MultiplayerMod.EarlyPatches
                if (type.Namespace != null && type.Namespace.EndsWith("EarlyPatches")) return;

                try {
                    harmony.CreateClassProcessor(type).Patch();
                } catch (Exception e) {
                    LogError($"FAIL: {type} with {e.InnerException}");
                }
            });

            SetCategory("General designation patches");

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
                        try
                        {
                            harmony.Patch(method, new HarmonyMethod(prefix), null, null, new HarmonyMethod(designatorFinalizer));
                        } catch (Exception e) {
                            LogError($"FAIL: {t.FullName}:{method.Name} with {e.InnerException}");
                        }
                    }
                }
            }

            SetCategory("Non-deterministic patches 1");

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
                var randomHair = typeof(PawnStyleItemChooser).GetMethod(nameof(PawnStyleItemChooser.RandomHairFor));

                var effectMethods = new MethodBase[] { subSustainerStart, sampleCtor, subSoundPlay, effecterTick, effecterTrigger, effecterCleanup, randomBoltMesh, drawTrackerCtor, randomHair };
                var moteMethods = typeof(MoteMaker).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(m => m.Name != "MakeBombardmentMote"); // Special case, just calls MakeBombardmentMote_NewTmp, prevents Hugslib complains
                var fleckMethods = typeof(FleckMaker).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(m => m.ReturnType == typeof(void));

                foreach (MethodBase m in effectMethods.Concat(moteMethods).Concat(fleckMethods))
                {
                    try
                    {
                        harmony.Patch(m, randPatchPrefix, randPatchPostfix);
                    } catch (Exception e) {
                        LogError($"FAIL: {m.DeclaringType.FullName}:{m.Name} with {e.InnerException}");
                    }
                }

            }

            SetCategory("Non-deterministic patches 2");

            // Set ThingContext and FactionContext (for pawns and buildings) in common Thing methods
            {
                var thingMethodPrefix = new HarmonyMethod(typeof(ThingMethodPatches).GetMethod("Prefix"));
                var thingMethodPostfix = new HarmonyMethod(typeof(ThingMethodPatches).GetMethod("Postfix"));
                var thingMethods = new[] { "Tick", "TickRare", "TickLong", "SpawnSetup", "TakeDamage", "Kill" };

                foreach (Type t in typeof(Thing).AllSubtypesAndSelf())
                {
                    foreach (string m in thingMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (method != null)
                        {
                            try
                            {
                                harmony.Patch(method, thingMethodPrefix, thingMethodPostfix);
                            } catch (Exception e) {
                                LogError($"FAIL: {method.DeclaringType.FullName}:{method.Name} with {e.InnerException}");
                            }
                        }
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

            SetCategory("Map time gui patches");

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
                    if (method != null && !method.IsAbstract)
                    {
                        try
                        {
                            harmony.Patch(method, setMapTimePrefix, setMapTimePostfix);
                        } catch (Exception e) {
                            LogError($"FAIL: {method.DeclaringType.FullName}:{method.Name} with {e.InnerException}");
                        }
                    }

                }
            }

            SetCategory("Mod patches");
            try {
                ModPatches.Init();
            } catch(Exception e) {
                LogError($"FAIL with {e}");
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

        internal static HashSet<Type> IgnoredVanillaDefTypes = new HashSet<Type>
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

            dict["ThingComp"] = GetDefInfo(SyncSerialization.thingCompTypes, TypeHash);
            dict["AbilityComp"] = GetDefInfo(SyncSerialization.abilityCompTypes, TypeHash);
            dict["Designator"] = GetDefInfo(SyncSerialization.designatorTypes, TypeHash);
            dict["WorldObjectComp"] = GetDefInfo(SyncSerialization.worldObjectCompTypes, TypeHash);
            dict["IStoreSettingsParent"] = GetDefInfo(SyncSerialization.storageParents, TypeHash);
            dict["IPlantToGrowSettable"] = GetDefInfo(SyncSerialization.plantToGrowSettables, TypeHash);

            dict["GameComponent"] = GetDefInfo(SyncSerialization.gameCompTypes, TypeHash);
            dict["WorldComponent"] = GetDefInfo(SyncSerialization.worldCompTypes, TypeHash);
            dict["MapComponent"] = GetDefInfo(SyncSerialization.mapCompTypes, TypeHash);

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

        private static DefInfo GetDefInfo<T>(IEnumerable<T> types, Func<T, int> hash)
        {
            return new DefInfo()
            {
                count = types.Count(),
                hash = types.Select(t => hash(t)).AggregateHash()
            };
        }

        public static void CollectModHashes()
        {
            enabledModAssemblyHashes.Clear();

            foreach (var mod in LoadedModManager.RunningModsListForReading)
            {
                var hashes = new ModHashes()
                {
                    assemblyHash = mod.ModAssemblies().CRC32(),
                    //xmlHash = LoadableXmlAssetCtorPatch.AggregateHash(mod),
                    aboutHash = new DirectoryInfo(Path.Combine(mod.RootDir, "About")).GetFiles().CRC32()
                };
                enabledModAssemblyHashes.Add(hashes);
            }
        }
    }

    public class ModHashes
    {
        public int assemblyHash, xmlHash, aboutHash;
    }

}

