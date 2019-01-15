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

        public MultiplayerMod(ModContentPack pack) : base(pack)
        {
            EarlyMarkNoInline();
            EarlyPatches();
            EarlyInit();

            settings = GetSettings<MpSettings>();
        }

        private void EarlyMarkNoInline()
        {
            foreach (var type in typeof(Multiplayer).Assembly.GetTypes())
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
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var firstMethod = asm.GetType("Harmony.AccessTools")?.GetMethod("FirstMethod");
                if (firstMethod != null)
                    harmony.Patch(firstMethod, new HarmonyMethod(typeof(AccessTools_FirstMethod_Patch), "Prefix"));
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
                AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly)),
                new HarmonyMethod(typeof(GetTypeInAnyAssemblyPatch), "Prefix"),
                new HarmonyMethod(typeof(GetTypeInAnyAssemblyPatch), "Postfix")
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
        }

        private void EarlyInit()
        {
            foreach (var thingMaker in DefDatabase<ThingSetMakerDef>.AllDefs)
            {
                CaptureThingSetMakers.captured.Add(thingMaker.root);

                if (thingMaker.root is ThingSetMaker_Sum sum)
                    sum.options.Select(o => o.thingSetMaker).Do(CaptureThingSetMakers.captured.Add);

                if (thingMaker.root is ThingSetMaker_Conditional cond)
                    CaptureThingSetMakers.captured.Add(cond.thingSetMaker);

                if (thingMaker.root is ThingSetMaker_RandomOption rand)
                    rand.options.Select(o => o.thingSetMaker).Do(CaptureThingSetMakers.captured.Add);
            }
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

            listing.End();
        }

        private void DoUsernameField(Listing_Standard listing)
        {
            GUI.SetNextControlName("UsernameField");

            string username = listing.TextEntryLabeled("MpUsername".Translate() + ":  ", settings.username);
            if (username.Length <= 15 && ServerJoiningState.UsernamePattern.IsMatch(username))
            {
                settings.username = username;
                Multiplayer.username = username;
            }

            if (Multiplayer.Client != null && GUI.GetNameOfFocusedControl() == "UsernameField")
                UI.UnfocusCurrentControl();
        }

        public override string SettingsCategory() => "Multiplayer";
    }

    public class MpSettings : ModSettings
    {
        public string username;
        public bool showCursors = true;
        public bool autoAcceptSteam;
        public bool transparentChat;
        public int autosaveSlots = 5;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref username, "username");
            Scribe_Values.Look(ref showCursors, "showCursors", true);
            Scribe_Values.Look(ref autoAcceptSteam, "autoAcceptSteam");
            Scribe_Values.Look(ref transparentChat, "transparentChat");
            Scribe_Values.Look(ref autosaveSlots, "autosaveSlots", 5);
        }
    }
}
