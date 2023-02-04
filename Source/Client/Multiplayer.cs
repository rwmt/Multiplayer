using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

using RimWorld;
using UnityEngine;
using Verse;

using Multiplayer.Common;
using System.Runtime.CompilerServices;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;

namespace Multiplayer.Client
{
    public class Multiplayer : Mod
    {
        public static Harmony harmony = new("multiplayer");
        public static MpSettings settings;

        public static MultiplayerGame game;
        public static MultiplayerSession session;

        public static ConnectionBase Client => session?.client;
        public static MultiplayerServer LocalServer { get; set; }
        public static PacketLogWindow WriterLog => session?.writerLog;
        public static PacketLogWindow ReaderLog => session?.readerLog;
        public static bool IsReplay => session?.replay ?? false;

        public static string username;

        public static bool reloading;

        public static IdBlock GlobalIdBlock => game.gameComp.globalIdBlock;
        public static MultiplayerGameComp GameComp => game.gameComp;
        public static MultiplayerWorldComp WorldComp => game.worldComp;

        public static bool ShowDevInfo => Prefs.DevMode && settings.showDevInfo;
        public static bool GhostMode => session is { ghostModeCheckbox: true };

        public static Faction RealPlayerFaction => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;

        public static bool ExecutingCmds => MultiplayerWorldComp.executingCmdWorld || AsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => MultiplayerWorldComp.tickingWorld || AsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
        public static Map MapContext => AsyncTimeComp.tickingMap ?? AsyncTimeComp.executingCmdMap;

        public static bool dontSync;
        public static bool ShouldSync => InInterface && !dontSync;
        public static bool InInterface =>
            Client != null
            && !Ticking
            && !ExecutingCmds
            && !reloading
            && Current.ProgramState == ProgramState.Playing
            && LongEventHandler.currentEvent == null;

        public static string ReplaysDir => GenFilePaths.FolderUnderSaveData("MpReplays");
        public static string DesyncsDir => GenFilePaths.FolderUnderSaveData("MpDesyncs");

        public static Stopwatch clock = Stopwatch.StartNew();

        public static bool arbiterInstance;
        public static bool loadingErrors;
        public static Stopwatch harmonyWatch = new();

        public static string restartConnect;
        public static bool restartConfigs;

        public Multiplayer(ModContentPack pack) : base(pack)
        {
            Native.EarlyInit();
            DisableOmitFramePointer();

            using (DeepProfilerWrapper.Section("Multiplayer CacheTypeHierarchy"))
                CacheTypeHierarchy();

            using (DeepProfilerWrapper.Section("Multiplayer CacheTypeByName"))
                CacheTypeByName();

            if (GenCommandLine.CommandLineArgPassed("profiler"))
            {
                SimpleProfiler.CheckAvailable();
                Log.Message($"Profiler: {SimpleProfiler.available}");
                SimpleProfiler.Init("prof");
            }

            if (GenCommandLine.CommandLineArgPassed("arbiter"))
            {
                ArbiterWindowFix.Run();
                arbiterInstance = true;
            }

            settings = GetSettings<MpSettings>();

            ProcessEnvironment();

            SyncDict.Init();

            EarlyPatches();
            InitSync();
            CheckInterfaceVersions();

            LongEventHandler.ExecuteWhenFinished(() => {
                // Double Execute ensures it'll run last.
                LongEventHandler.ExecuteWhenFinished(LatePatches);
            });

#if DEBUG
            Application.logMessageReceivedThreaded -= Log.Notify_MessageReceivedThreadedInternal;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DisableOmitFramePointer()
        {
            Native.mini_parse_debug_option("disable_omit_fp");
        }

        public const string RestartConnectVariable = "MultiplayerRestartConnect";
        public const string RestartConfigsVariable = "MultiplayerRestartConfigs";

        private static void ProcessEnvironment()
        {
            if (!Environment.GetEnvironmentVariable(RestartConnectVariable).NullOrEmpty())
            {
                restartConnect = Environment.GetEnvironmentVariable(RestartConnectVariable);
                Environment.SetEnvironmentVariable(RestartConnectVariable, ""); // Effectively unsets it
            }

            if (!Environment.GetEnvironmentVariable(RestartConfigsVariable).NullOrEmpty())
            {
                restartConfigs = Environment.GetEnvironmentVariable(RestartConfigsVariable) == "true";
                Environment.SetEnvironmentVariable(RestartConfigsVariable, "");
            }
        }

        internal static Dictionary<Type, List<Type>> subClasses = new();
        internal static Dictionary<Type, List<Type>> subClassesNonAbstract = new();
        internal static Dictionary<Type, List<Type>> implementations = new();

        private static void CacheTypeHierarchy()
        {
            foreach (var type in GenTypes.AllTypes)
            {
                for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
                {
                    subClasses.GetOrAddNew(baseType).Add(type);
                    if (!type.IsAbstract)
                        subClassesNonAbstract.GetOrAddNew(baseType).Add(type);
                }

                foreach (var i in type.GetInterfaces())
                    implementations.GetOrAddNew(i).Add(type);
            }
        }

        internal static Dictionary<string, Type> typeByName = new();
        internal static Dictionary<string, Type> typeByFullName = new();

        private static void CacheTypeByName()
        {
            foreach (var type in GenTypes.AllTypes)
            {
                if (!typeByName.ContainsKey(type.Name))
                    typeByName[type.Name] = type;

                if (!typeByFullName.ContainsKey(type.Name))
                    typeByFullName[type.FullName] = type;
            }
        }

        private static void EarlyPatches()
        {
            // Might fix some mod desyncs
            harmony.PatchMeasure(
                AccessTools.Constructor(typeof(Def), new Type[0]),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix)),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Postfix))
            );

            Assembly.GetCallingAssembly().GetTypes().Do(type => {
                if (type.IsDefined(typeof(EarlyPatchAttribute)))
                    harmony.CreateClassProcessor(type).Patch();
            });

#if DEBUG
            DebugPatches.Init();
#endif
        }

        private static void InitSync()
        {
            using (DeepProfilerWrapper.Section("Multiplayer CollectTypes"))
                SyncSerialization.Init();

            using (DeepProfilerWrapper.Section("Multiplayer SyncGame"))
                SyncGame.Init();

            using (DeepProfilerWrapper.Section("Multiplayer Sync register attributes"))
                Sync.RegisterAllAttributes(typeof(Multiplayer).Assembly);

            using (DeepProfilerWrapper.Section("Multiplayer Sync validation"))
                Sync.ValidateAll();
        }

        private static void LatePatches()
        {
            // optimization, cache DescendantThingDefs
            harmony.PatchMeasure(
                AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
            );

            // optimization, cache ThisAndChildCategoryDefs
            harmony.PatchMeasure(
                AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
            );

            if (MpVersion.IsDebug) {
                Log.Message("== Structure == \n" + SyncDict.syncWorkers.PrintStructure());
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Multiplayer";

        private static void CheckInterfaceVersions()
        {
            var mpAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Multiplayer");
            var curVersion = new Version(
                (mpAssembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0] as AssemblyFileVersionAttribute).Version
            );

            Log.Message($"Current MultiplayerAPI version: {curVersion}");

            foreach (var mod in LoadedModManager.RunningMods) {
                if (mod.assemblies.loadedAssemblies.NullOrEmpty())
                    continue;

                if (mod.Name == "Multiplayer")
                    continue;

                // Test if mod is using multiplayer api
                if (!mod.assemblies.loadedAssemblies.Any(a => a.GetName().Name == MpVersion.ApiAssemblyName)) {
                    continue;
                }

                // Retrieve the original dll
                var info = MultiplayerData.GetModAssemblies(mod)
                    .Select(f => FileVersionInfo.GetVersionInfo(f.FullName))
                    .FirstOrDefault(v => v.ProductName == "Multiplayer");

                if (info == null) {
                    // There are certain mods that don't include the API, namely compat
                    // Can we test them?
                    continue;
                }

                var version = new Version(info.FileVersion);

                Log.Message($"Mod {mod.Name} has MultiplayerAPI client ({version})");

                if (curVersion > version)
                    Log.Warning($"Mod {mod.Name} uses an older API version (mod: {version}, current: {curVersion})");
                else if (curVersion < version)
                    Log.Error($"Mod {mod.Name} uses a newer API version! (mod: {version}, current: {curVersion})\nMake sure the Multiplayer mod is up to date");
            }
        }

        public static void StopMultiplayerAndClearAllWindows()
        {
            StopMultiplayer();
            MpUI.ClearWindowStack();
        }

        public static void StopMultiplayer()
        {
            Log.Message($"Stopping multiplayer session from {new StackTrace().GetFrame(1).GetMethod().FullDescription()}");

            OnMainThread.ClearScheduled();
            LongEventHandler.ClearQueuedEvents();

            if (session != null)
            {
                session.Stop();
                session = null;
                Prefs.Apply();
            }

            if (LocalServer != null)
            {
                LocalServer.running = false;
                LocalServer.serverThread?.Join();
                LocalServer.TryStop();
                LocalServer = null;
            }

            game?.OnDestroy();
            game = null;

            TickPatch.Reset();

            Find.WindowStack?.WindowOfType<ServerBrowser>()?.Cleanup(true);
            SyncFieldUtil.ClearAllBufferedChanges();

            if (arbiterInstance)
            {
                arbiterInstance = false;
                Application.Quit();
            }
        }

        public static void WriteSettingsToDisk()
        {
            LoadedModManager.GetMod<Multiplayer>().WriteSettings();
        }
    }
}
