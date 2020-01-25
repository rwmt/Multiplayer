using Harmony;
using Harmony.ILCopying;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class MultiplayerMod : Mod
    {
        public static HarmonyInstance harmony = HarmonyInstance.Create("multiplayer");
        public static MpSettings settings;

        public static bool arbiterInstance;

        public MultiplayerMod(ModContentPack pack) : base(pack)
        {
            if (GenCommandLine.CommandLineArgPassed("arbiter"))
                arbiterInstance = true;

            EarlyMarkNoInline(typeof(Multiplayer).Assembly);
            EarlyPatches();

            CheckInterfaceVersions();

            settings = GetSettings<MpSettings>();

            if (MpVersion.IsDebug) {
                LongEventHandler.ExecuteWhenFinished(() => {
                    Log.Message("== Structure == \n" + Sync.syncWorkers.PrintStructure());
                });
            }
        }

        public static void EarlyMarkNoInline(Assembly asm)
        {
            foreach (var type in asm.GetTypes())
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
            // special case?
            MpUtil.MarkNoInlining(AccessTools.Method(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.Reset)));

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var firstMethod = asm.GetType("Harmony.AccessTools")?.GetMethod("FirstMethod");
                if (firstMethod != null)
                    harmony.Patch(firstMethod, new HarmonyMethod(typeof(AccessTools_FirstMethod_Patch), nameof(AccessTools_FirstMethod_Patch.Prefix)));

                if (asm == typeof(HarmonyPatch).Assembly) continue;

                var emitCallParameter = asm.GetType("Harmony.MethodPatcher")?.GetMethod("EmitCallParameter", AccessTools.all);
                if (emitCallParameter != null)
                    harmony.Patch(emitCallParameter, new HarmonyMethod(typeof(PatchHarmony), emitCallParameter.GetParameters().Length == 4 ? nameof(PatchHarmony.EmitCallParamsPrefix4) : nameof(PatchHarmony.EmitCallParamsPrefix5)));
            }

            {
                var prefix = new HarmonyMethod(AccessTools.Method(typeof(CaptureThingSetMakers), "Prefix"));
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_MarketValue)), prefix);
                harmony.Patch(AccessTools.Constructor(typeof(ThingSetMaker_Nutrition)), prefix);
            }

            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
                new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)),
                transpiler: new HarmonyMethod(typeof(ParseAndProcessXml_Patch), "Transpiler")
            );

            harmony.Patch(
                AccessTools.Method(typeof(XmlNode), "get_ChildNodes"),
                postfix: new HarmonyMethod(typeof(XmlNodeListPatch), nameof(XmlNodeListPatch.XmlNode_ChildNodes_Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.TryRegisterAllFrom)),
                new HarmonyMethod(typeof(XmlInheritance_Patch), "Prefix"),
                new HarmonyMethod(typeof(XmlInheritance_Patch), "Postfix")
            );

            harmony.Patch(
                AccessTools.Constructor(typeof(LoadableXmlAsset), new[] { typeof(string), typeof(string), typeof(string) }),
                new HarmonyMethod(typeof(LoadableXmlAssetCtorPatch), "Prefix")
            );

            // Cross os compatibility
            harmony.Patch (
                AccessTools.Method (typeof (DirectXmlLoader), nameof (DirectXmlLoader.XmlAssetsInModFolder)), null,
                new HarmonyMethod (typeof (XmlAssetsInModFolderPatch), "Postfix")
            );

            // Might fix some mod desyncs
            harmony.Patch(
                AccessTools.Constructor(typeof(Def), new Type[0]),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix)),
                new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Postfix))
            );
        }

        private string slotsBuffer;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.ColumnWidth = 220f;

            DoUsernameField(listing);
            listing.TextFieldNumericLabeled("MpAutosaveSlots".Translate() + ":  ", ref settings.autosaveSlots, ref slotsBuffer, 1f, 99f);

            listing.CheckboxLabeled("MpShowPlayerCursors".Translate(), ref settings.showCursors);
            listing.CheckboxLabeled("MpAutoAcceptSteam".Translate(), ref settings.autoAcceptSteam, "MpAutoAcceptSteamDesc".Translate());
            listing.CheckboxLabeled("MpTransparentChat".Translate(), ref settings.transparentChat);
            listing.CheckboxLabeled("MpAggressiveTicking".Translate(), ref settings.aggressiveTicking, "MpAggressiveTickingDesc".Translate());

            listing.CheckboxLabeled("MpArbiterSetting".Translate(), ref settings.arbiterSetting, "MpArbiterSettingDesc".Translate());

            var appendNameToAutosaveLabel = $"{"MpAppendNameToAutosave".Translate()}:  ";
            var appendNameToAutosaveLabelWidth = Text.CalcSize(appendNameToAutosaveLabel).x;
            var appendNameToAutosaveCheckboxWidth = appendNameToAutosaveLabelWidth + 30f;
            listing.CheckboxLabeled(appendNameToAutosaveLabel, ref settings.appendNameToAutosave);

            listing.CheckboxLabeled("MpPauseAutosaveCounter".Translate(), ref settings.pauseAutosaveCounter, "MpPauseAutosaveCounterDesc".Translate());

            if (Prefs.DevMode)
                listing.CheckboxLabeled("Show debug info", ref settings.showDevInfo);

            listing.End();
        }

        const string UsernameField = "UsernameField";

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName(UsernameField);

            string username = listing.TextEntryLabeled("MpUsername".Translate() + ":  ", settings.username);
            if (username.Length <= 15 && ServerJoiningState.UsernamePattern.IsMatch(username))
            {
                settings.username = username;
                Multiplayer.username = username;
            }

            if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == UsernameField)
                UI.UnfocusCurrentControl();
        }

        public override string SettingsCategory() => "Multiplayer";

        static void CheckInterfaceVersions()
        {
            var mpAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Multiplayer");
            var curVersion = new System.Version(
                (mpAssembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0] as AssemblyFileVersionAttribute).Version
            );

            Log.Message($"Current API version: {curVersion}");

            foreach (var mod in LoadedModManager.RunningMods) {
                if (!mod.LoadedAnyAssembly)
                    continue;

                if (mod.Name == "Multiplayer")
                    continue;

                Assembly assembly = mod.assemblies.loadedAssemblies.FirstOrDefault(a => a.GetName().Name == MpVersion.apiAssemblyName);

                if (assembly != null) {
                    var version = new Version(FileVersionInfo.GetVersionInfo(System.IO.Path.Combine(mod.AssembliesFolder, $"{MpVersion.apiAssemblyName}.dll")).FileVersion);

                    Log.Message($"Mod {mod.Name} has API client ({version})");

                    if (curVersion > version)
                        Log.Warning($"Mod {mod.Name} uses an older API version (mod: {version}, current: {curVersion})");
                    else if (curVersion < version)
                        Log.Error($"Mod {mod.Name} uses a newer API version! (mod: {version}, current: {curVersion})\nMake sure the Multiplayer mod is up to date");
                }
            }
        }
    }

    static class XmlAssetsInModFolderPatch
    {
        // Sorts the files before processing, ensures cross os compatibility
        static IEnumerable<LoadableXmlAsset> Postfix (IEnumerable<LoadableXmlAsset> __result)
        {
            var array = __result.ToArray ();

            Array.Sort (array, (x, y) => StringComparer.OrdinalIgnoreCase.Compare (x.name, y.name));

            return array;
        }
    }

    static class LoadableXmlAssetCtorPatch
    {
        public static List<Pair<LoadableXmlAsset, int>> xmlAssetHashes = new List<Pair<LoadableXmlAsset, int>>();

        static void Prefix(LoadableXmlAsset __instance, string contents)
        {
            xmlAssetHashes.Add(new Pair<LoadableXmlAsset, int>(__instance, GenText.StableStringHash(contents)));
        }
    }

    static class ModPreviewImagePatch
    {
        static bool Prefix() => !MpVersion.IsDebug && !MultiplayerMod.arbiterInstance;
    }

    static class PatchHarmony
    {
        static MethodInfo mpEmitCallParam = AccessTools.Method(typeof(MethodPatcher), "EmitCallParameter");

        public static bool EmitCallParamsPrefix4(ILGenerator il, MethodBase original, MethodInfo patch, Dictionary<string, LocalBuilder> variables)
        {
            mpEmitCallParam.Invoke(null, new object[] { il, original, patch, variables, false });
            return false;
        }

        public static bool EmitCallParamsPrefix5(ILGenerator il, MethodBase original, MethodInfo patch, Dictionary<string, LocalBuilder> variables, bool allowFirsParamPassthrough)
        {
            mpEmitCallParam.Invoke(null, new object[] { il, original, patch, variables, allowFirsParamPassthrough });
            return false;
        }
    }

    public class MpSettings : ModSettings
    {
        public string username;
        public bool showCursors = true;
        public bool autoAcceptSteam;
        public bool transparentChat;
        public int autosaveSlots = 5;
        public bool aggressiveTicking = true;
        public bool showDevInfo;
        public string serverAddress = "127.0.0.1";
        public bool appendNameToAutosave;
        public bool pauseAutosaveCounter = true;
        public ServerSettings serverSettings;
        public bool arbiterSetting;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref username, "username");
            Scribe_Values.Look(ref showCursors, "showCursors", true);
            Scribe_Values.Look(ref autoAcceptSteam, "autoAcceptSteam");
            Scribe_Values.Look(ref transparentChat, "transparentChat");
            Scribe_Values.Look(ref autosaveSlots, "autosaveSlots", 5);
            Scribe_Values.Look(ref aggressiveTicking, "aggressiveTicking", true);
            Scribe_Values.Look(ref showDevInfo, "showDevInfo");
            Scribe_Values.Look(ref serverAddress, "serverAddress", "127.0.0.1");
            Scribe_Values.Look(ref pauseAutosaveCounter, "pauseAutosaveCounter", true);

            Scribe_Values.Look(ref arbiterSetting, "arbiterSetting");

            Scribe_Deep.Look(ref serverSettings, "serverSettings");

            if (serverSettings == null)
                serverSettings = new ServerSettings();
        }
    }
}
