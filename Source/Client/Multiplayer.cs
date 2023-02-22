using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

using RimWorld;
using UnityEngine;
using Verse;

using Multiplayer.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Util;

namespace Multiplayer.Client
{
    public static class Multiplayer
    {
        public static Harmony harmony = new("multiplayer");
        public static MpSettings settings;

        public static MultiplayerGame game;
        public static MultiplayerSession session;

        public static MultiplayerServer LocalServer { get; set; }
        public static Thread localServerThread;

        public static ConnectionBase Client => session?.client;
        public static PacketLogWindow WriterLog => session?.writerLog;
        public static PacketLogWindow ReaderLog => session?.readerLog;
        public static bool IsReplay => session?.replay ?? false;

        public static string username;

        public static bool reloading;

        public static IdBlock GlobalIdBlock => game.gameComp.globalIdBlock;
        public static MultiplayerGameComp GameComp => game.gameComp;
        public static MultiplayerWorldComp WorldComp => game.worldComp;
        public static WorldTimeComp WorldTime => game.worldTimeComp;

        public static bool ShowDevInfo => Prefs.DevMode && settings.showDevInfo;
        public static bool GhostMode => session is { ghostModeCheckbox: true };

        public static Faction RealPlayerFaction => Client != null ? game.RealPlayerFaction : Faction.OfPlayer;

        public static bool ExecutingCmds => WorldTimeComp.executingCmdWorld || AsyncTimeComp.executingCmdMap != null;
        public static bool Ticking => WorldTimeComp.tickingWorld || AsyncTimeComp.tickingMap != null || ConstantTicker.ticking;
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

        public static void InitMultiplayer()
        {
            Native.EarlyInit();
            DisableOmitFramePointer();

            MultiplayerLoader.Multiplayer.settingsWindowDrawer =
                rect => MpSettingsUI.DoSettingsWindowContents(settings, rect);

            using (DeepProfilerWrapper.Section("Multiplayer CacheTypeHierarchy"))
                TypeCache.CacheTypeHierarchy();

            using (DeepProfilerWrapper.Section("Multiplayer CacheTypeByName"))
                TypeCache.CacheTypeByName();

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

            ScribeLike.provider = new ScribeProvider();
            settings = MultiplayerLoader.Multiplayer.instance!.GetSettings<MpSettings>();

            EarlyInit.ProcessEnvironment();

            SyncDict.Init();

            EarlyInit.EarlyPatches(harmony);
            EarlyInit.InitSync();
            CheckInterfaceVersions();

            LongEventHandler.ExecuteWhenFinished(() => {
                // Double Execute ensures it'll run last.
                LongEventHandler.ExecuteWhenFinished(() => EarlyInit.LatePatches(harmony));
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
                localServerThread?.Join();
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
    }
}
