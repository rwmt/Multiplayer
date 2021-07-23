using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Xml;

using HarmonyLib;

using RimWorld;
using UnityEngine;
using Verse;

using Multiplayer.Common;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using Multiplayer.Client.Desyncs;

namespace Multiplayer.Client
{
    public class MultiplayerMod : Mod
    {
        public static Harmony harmony = new Harmony("multiplayer");
        public static MpSettings settings;

        public static bool arbiterInstance;
        public static bool hasLoaded;

        public MultiplayerMod(ModContentPack pack) : base(pack)
        {
            Native.EarlyInit();

            Native.mini_parse_debug_option("disable_omit_fp");

            if (GenCommandLine.CommandLineArgPassed("arbiter")) {
                ArbiterWindowFix.Run();

                arbiterInstance = true;
            }

            MpUtil.MarkNoInlining(AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.PauseOnLoad)));

            //EarlyMarkNoInline(typeof(Multiplayer).Assembly);
            EarlyPatches();
            CheckInterfaceVersions();

            settings = GetSettings<MpSettings>();

            LongEventHandler.ExecuteWhenFinished(() => {
                // Double Execute ensures it'll run last.
                LongEventHandler.ExecuteWhenFinished(LatePatches);
            });
        }

        private void EarlyPatches()
        {
            // special case?
            // Harmony 2.0 should be handling NoInlining already... test without
            //MpUtil.MarkNoInlining(AccessTools.Method(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.Reset)));

            // TODO: 20200229 must evaluate if this is still required
            /*foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var firstMethod = asm.GetType("Harmony.AccessTools")?.GetMethod("FirstMethod");
                if (firstMethod != null)
                    harmony.Patch(firstMethod, new HarmonyMethod(typeof(AccessTools_FirstMethod_Patch), nameof(AccessTools_FirstMethod_Patch.Prefix)));

                if (asm == typeof(HarmonyPatch).Assembly) continue;
            }*/

            Assembly.GetCallingAssembly().GetTypes().Do(type => {
                if (type.Namespace != null && type.Namespace.EndsWith("EarlyPatches"))
                    harmony.CreateClassProcessor(type).Patch();
            });

            // Might fix some mod desyncs
            harmony.Patch(
                AccessTools.Constructor(typeof(Def), new Type[0]),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix)),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Postfix))
            );

            harmony.Patch(
                AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)),
                new HarmonyMethod(typeof(MultiplayerMod), nameof(Prefixfactionman))
            );

            harmony.Patch(
                AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.IsPlayer)),
                new HarmonyMethod(typeof(MultiplayerMod), nameof(Prefixfactionman))
            );

            /*harmony.Patch(
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Int)),
                postfix: new HarmonyMethod(typeof(DeferredStackTracing), nameof(DeferredStackTracing.Postfix))
            );

            harmony.Patch(
                AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value)),
                postfix: new HarmonyMethod(typeof(DeferredStackTracing), nameof(DeferredStackTracing.Postfix))
            );*/

            //ForeignRand.DoPatches();
        }

        static void Prefixfactionman()
        {
            if (Scribe.mode != LoadSaveMode.Inactive) {
                string trace = new StackTrace().ToString();
                if (!trace.Contains("SetInitialPsyfocusLevel") &&
                    !trace.Contains("Pawn_NeedsTracker.ShouldHaveNeed") &&
                    !trace.Contains("FactionManager.ExposeData"))
                    Log.Message($"factionman call {trace}", true);
            }
        }

        private void LatePatches()
        {
            // optimization, cache DescendantThingDefs
            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
            );

            // optimization, cache ThisAndChildCategoryDefs
            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
            );

            if (MpVersion.IsDebug) {
                Log.Message("== Structure == \n" + Sync.syncWorkers.PrintStructure());
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Multiplayer";

        static void CheckInterfaceVersions()
        {
            var mpAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Multiplayer");
            var curVersion = new System.Version(
                (mpAssembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0] as AssemblyFileVersionAttribute).Version
            );

            Log.Message($"Current MultiplayerAPI version: {curVersion}");

            foreach (var mod in LoadedModManager.RunningMods) {
                if (mod.assemblies.loadedAssemblies.NullOrEmpty())
                    continue;

                if (mod.Name == "Multiplayer")
                    continue;

                // Test if mod is using multiplayer api
                if (!mod.assemblies.loadedAssemblies.Any(a => a.GetName().Name == MpVersion.apiAssemblyName)) {
                    continue;
                }

                // Retrieve the original dll
                var info = mod.ModAssemblies()
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
    }

    static class ArbiterWindowFix
    {
        const string LpWindowName = "Rimworld by Ludeon Studios";
        const string LpBatchModeClassName = "Unity.BatchModeWindow";

        [DllImport("User32")]
        static extern int SetParent(int hwnd, int nCmdShow);

        [DllImport("User32")]
        static extern IntPtr FindWindowA(string lpClassName, string lpWindowName);

        internal static void Run()
        {
            if (!Environment.OSVersion.Platform.Equals(PlatformID.Win32NT)) {
                return;
            }

            SetParent(FindWindowA(LpBatchModeClassName, LpWindowName).ToInt32(), -3);
        }
    }

}
